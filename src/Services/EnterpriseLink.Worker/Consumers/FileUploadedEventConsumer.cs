using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Worker.Batch;
using EnterpriseLink.Worker.Idempotency;
using EnterpriseLink.Worker.MultiTenancy;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Storage;
using MassTransit;

namespace EnterpriseLink.Worker.Consumers;

/// <summary>
/// MassTransit consumer that subscribes to the <see cref="FileUploadedEvent"/> queue
/// and orchestrates asynchronous CSV processing for the EnterpriseLink Worker service.
///
/// <para><b>Subscription topology</b></para>
/// MassTransit binds this consumer to a durable RabbitMQ queue named
/// <c>file-uploaded-processing</c>. The queue is bound to the fanout exchange
/// published by the Ingestion service for <see cref="FileUploadedEvent"/>.
///
/// <para><b>Message handling pipeline</b></para>
/// <code>
/// RabbitMQ queue — FileUploadedEvent received
///   │
///   ▼  1. Validate required fields (UploadId, TenantId, StoragePath, FileName)
///   ▼  2. Guard: UploadedAt must not be more than 5 minutes in the future
///   ▼  3. Idempotency check via IUploadIdempotencyGuard.TryBeginAsync
///          → already Completed: return (message acked, no-op)
///          → new or retry: claim the upload, continue
///   ▼  4. Set tenant context (WorkerTenantContext.TenantId = message.TenantId)
///   ▼  5. Resolve absolute file path via IFileStorageResolver
///   ▼  6. Stream CSV rows via ICsvStreamingParser (IAsyncEnumerable — O(1) memory)
///         → batch-insert to SQL Server via IBatchRowInserter (commit every N rows)
///   ▼  7. Mark upload complete via IUploadIdempotencyGuard.CompleteAsync
///   ▼
/// Message acknowledged — removed from queue
/// </code>
///
/// <para><b>Memory model</b></para>
/// <see cref="ICsvStreamingParser.ParseAsync"/> returns an <c>IAsyncEnumerable</c>
/// that is passed directly to <see cref="IBatchRowInserter.InsertAsync"/>. Only one
/// <see cref="ParsedRow"/> and one batch buffer exist in memory at any time.
/// Files of 5 GB or more are handled without out-of-memory risk.
///
/// <para><b>Error handling</b></para>
/// If <see cref="Consume"/> throws, MassTransit's consumer-level retry policy retries
/// with exponential back-off before moving the message to the dead-letter queue.
/// <see cref="IUploadIdempotencyGuard.FailAsync"/> is called before re-throwing so
/// the next retry can distinguish a failed attempt from a concurrent claim.
///
/// <para><b>Idempotency</b></para>
/// <see cref="FileUploadedEvent.UploadId"/> is the idempotency key. Duplicate messages
/// (RabbitMQ re-delivery, exactly-once guarantee breach) are detected and silently
/// acknowledged — no duplicate <c>Transaction</c> rows are inserted.
///
/// <para><b>Tenant isolation</b>
/// All downstream database operations are scoped to
/// <see cref="FileUploadedEvent.TenantId"/> via <see cref="WorkerTenantContext"/>.
/// Cross-tenant writes are prevented by EF Core's <c>ApplyTenantId</c> interceptor
/// and SQL Server Row-Level Security.
/// </para>
/// </summary>
public sealed class FileUploadedEventConsumer : IConsumer<FileUploadedEvent>
{
    private readonly WorkerTenantContext _tenantContext;
    private readonly IFileStorageResolver _storageResolver;
    private readonly ICsvStreamingParser _csvParser;
    private readonly IBatchRowInserter _batchInserter;
    private readonly IUploadIdempotencyGuard _idempotencyGuard;
    private readonly ILogger<FileUploadedEventConsumer> _logger;

    /// <summary>Initialises the consumer with its required dependencies.</summary>
    /// <param name="tenantContext">
    /// Mutable tenant context; set from the message before any DB operation.
    /// </param>
    /// <param name="storageResolver">
    /// Resolves relative storage paths from the event to absolute filesystem paths.
    /// </param>
    /// <param name="csvParser">
    /// Streaming CSV parser — yields rows one at a time, supporting 5 GB+ files.
    /// </param>
    /// <param name="batchInserter">
    /// Batch inserter — commits every <c>BatchSize</c> rows in a single round-trip.
    /// </param>
    /// <param name="idempotencyGuard">
    /// Guards against duplicate processing of the same upload.
    /// </param>
    /// <param name="logger">
    /// Structured logger — all properties are logged with correlation context.
    /// </param>
    public FileUploadedEventConsumer(
        WorkerTenantContext tenantContext,
        IFileStorageResolver storageResolver,
        ICsvStreamingParser csvParser,
        IBatchRowInserter batchInserter,
        IUploadIdempotencyGuard idempotencyGuard,
        ILogger<FileUploadedEventConsumer> logger)
    {
        _tenantContext = tenantContext;
        _storageResolver = storageResolver;
        _csvParser = csvParser;
        _batchInserter = batchInserter;
        _idempotencyGuard = idempotencyGuard;
        _logger = logger;
    }

    /// <summary>
    /// Handles an incoming <see cref="FileUploadedEvent"/> message from the RabbitMQ queue.
    ///
    /// <para>
    /// MassTransit automatically acknowledges the message when this method returns
    /// without throwing. If an exception escapes, MassTransit applies the configured
    /// retry policy before moving the message to the dead-letter queue.
    /// </para>
    /// </summary>
    /// <param name="context">
    /// The MassTransit consume context providing the message payload, message ID,
    /// headers, and retry count.
    /// </param>
    public async Task Consume(ConsumeContext<FileUploadedEvent> context)
    {
        var message = context.Message;
        var cancellationToken = context.CancellationToken;

        // ── Step 1: Validate required message fields ───────────────────────────
        // Defensive guard against malformed or partially-delivered messages.
        if (message.UploadId == Guid.Empty ||
            message.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(message.StoragePath) ||
            string.IsNullOrWhiteSpace(message.FileName))
        {
            _logger.LogError(
                "Received malformed FileUploadedEvent — required fields missing or empty. " +
                "MessageId={MessageId} UploadId={UploadId} TenantId={TenantId}",
                context.MessageId, message.UploadId, message.TenantId);

            throw new InvalidOperationException(
                $"FileUploadedEvent {context.MessageId} is malformed: " +
                "UploadId, TenantId, StoragePath and FileName are all required.");
        }

        // ── Step 2: Guard — UploadedAt must not be in the future ──────────────
        const int clockSkewGraceMinutes = 5;
        if (message.UploadedAt > DateTimeOffset.UtcNow.AddMinutes(clockSkewGraceMinutes))
        {
            _logger.LogError(
                "Received FileUploadedEvent with a future UploadedAt timestamp — " +
                "possible clock skew or tampered message. " +
                "MessageId={MessageId} UploadId={UploadId} UploadedAt={UploadedAt} UtcNow={UtcNow}",
                context.MessageId, message.UploadId, message.UploadedAt, DateTimeOffset.UtcNow);

            throw new InvalidOperationException(
                $"FileUploadedEvent {context.MessageId} has a future UploadedAt " +
                $"({message.UploadedAt:O}) which exceeds the allowed clock-skew " +
                $"grace of {clockSkewGraceMinutes} minutes.");
        }

        // ── Step 3: Log structured receipt ────────────────────────────────────
        _logger.LogInformation(
            "FileUploadedEvent received. " +
            "UploadId={UploadId} TenantId={TenantId} FileName={FileName} " +
            "FileSizeBytes={FileSizeBytes} DataRowCount={DataRowCount} " +
            "SourceSystem={SourceSystem} StoragePath={StoragePath} " +
            "UploadedAt={UploadedAt} RetryCount={RetryCount}",
            message.UploadId,
            message.TenantId,
            message.FileName,
            message.FileSizeBytes,
            message.DataRowCount,
            message.SourceSystem,
            message.StoragePath,
            message.UploadedAt,
            context.GetRetryCount());

        // ── Step 4: Idempotency check ─────────────────────────────────────────
        // TryBeginAsync atomically inserts a Processing row with UploadId as PK.
        // Returns false if the upload is already Completed (duplicate message).
        // Returns true for new uploads or retries (Processing/Failed status).
        var claimed = await _idempotencyGuard.TryBeginAsync(
            message.UploadId,
            message.TenantId,
            message.SourceSystem,
            cancellationToken);

        if (!claimed)
        {
            // Upload already completed by a previous consumer invocation.
            // Acknowledge the message silently — no rows are inserted.
            _logger.LogInformation(
                "Upload already processed — skipping duplicate message. UploadId={UploadId}",
                message.UploadId);
            return;
        }

        // ── Step 5: Set tenant context for downstream DB operations ───────────
        // WorkerTenantContext is scoped — setting TenantId here propagates to all
        // services within this MassTransit message scope (AppDbContext, query filters,
        // ApplyTenantId interceptor, and the SQL Server RLS interceptor).
        _tenantContext.TenantId = message.TenantId;

        // ── Step 6: Resolve path → stream CSV → batch insert ──────────────────
        int rowsInserted;

        try
        {
            // ResolveFullPath validates the relative path and prevents path traversal.
            var fullPath = _storageResolver.ResolveFullPath(message.StoragePath);

            _logger.LogDebug(
                "Resolved storage path. StoragePath={StoragePath} FullPath={FullPath} UploadId={UploadId}",
                message.StoragePath, fullPath, message.UploadId);

            // ParseAsync returns a lazy IAsyncEnumerable — rows are not buffered.
            // InsertAsync streams them into batches of BatchSize, committing each
            // batch with a single SaveChangesAsync. Memory is bounded by BatchSize.
            var rows = _csvParser.ParseAsync(fullPath, cancellationToken);

            rowsInserted = await _batchInserter.InsertAsync(
                rows,
                message.TenantId,
                message.UploadId,
                message.SourceSystem,
                cancellationToken);

            // ── Step 7: Mark complete ─────────────────────────────────────────
            await _idempotencyGuard.CompleteAsync(message.UploadId, rowsInserted, cancellationToken);
        }
        catch (Exception)
        {
            // Mark the upload as failed before re-throwing so that MassTransit's
            // retry policy delivers the message again with Failed status, allowing
            // the idempotency guard to reset to Processing on the next attempt.
            // CancellationToken.None: the consume context token may already be cancelled.
            await _idempotencyGuard.FailAsync(message.UploadId, CancellationToken.None);
            throw;
        }

        // ── Integrity hint: log row count mismatch ────────────────────────────
        if (rowsInserted != message.DataRowCount)
        {
            _logger.LogWarning(
                "Row count mismatch after batch insert. " +
                "Expected={Expected} Actual={Actual} UploadId={UploadId} TenantId={TenantId}",
                message.DataRowCount, rowsInserted, message.UploadId, message.TenantId);
        }

        _logger.LogInformation(
            "FileUploadedEvent handled successfully. UploadId={UploadId} RowsInserted={RowsInserted}",
            message.UploadId, rowsInserted);
    }
}
