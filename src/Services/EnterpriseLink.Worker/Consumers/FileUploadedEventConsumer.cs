using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Worker.Batch;
using EnterpriseLink.Worker.Idempotency;
using EnterpriseLink.Worker.MultiTenancy;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Storage;
using EnterpriseLink.Worker.Validation;
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
///   ▼  1. Validate required message fields (UploadId, TenantId, StoragePath, FileName)
///   ▼  2. Guard: UploadedAt must not be more than 5 minutes in the future
///   ▼  3. Idempotency check via IUploadIdempotencyGuard.TryBeginAsync
///          → already Completed: return (message acked, no-op)
///          → new or retry: claim the upload, continue
///   ▼  4. Set tenant context (WorkerTenantContext.TenantId = message.TenantId)
///   ▼  5. Resolve absolute file path via IFileStorageResolver
///   ▼  6. Stream CSV rows via ICsvStreamingParser → classify via IValidationPipeline:
///            a. Schema validation  — required fields enforced
///            b. Business rules     — extensible rule framework
///            c. Duplicate detection — hash-based fingerprint within upload
///   ▼  7. Batch-insert valid rows to SQL Server via IBatchRowInserter
///   ▼  8. Persist invalid rows to InvalidTransactions via IInvalidRowPersister
///   ▼  9. Mark upload complete via IUploadIdempotencyGuard.CompleteAsync
///   ▼
/// Message acknowledged — removed from queue
/// </code>
///
/// <para><b>Memory model</b></para>
/// <see cref="ICsvStreamingParser.ParseAsync"/> returns an <c>IAsyncEnumerable</c>
/// that is consumed by <see cref="IValidationPipeline.ClassifyAsync"/> which
/// materialises two lists (valid + invalid). For enterprise batch sizes
/// (≤1 million rows) both lists fit comfortably in memory.
///
/// <para><b>Error handling</b></para>
/// If <see cref="Consume"/> throws, MassTransit's consumer-level retry policy retries
/// with exponential back-off before moving the message to the dead-letter queue.
/// <see cref="IUploadIdempotencyGuard.FailAsync"/> is called before re-throwing so
/// the next retry can distinguish a failed attempt from a concurrent claim.
///
/// <para><b>Idempotency</b></para>
/// <c>FileUploadedEvent.UploadId</c> is the idempotency key. Duplicate messages
/// (RabbitMQ re-delivery, exactly-once guarantee breach) are detected and silently
/// acknowledged — no duplicate <c>Transaction</c> rows are inserted.
///
/// <para><b>Tenant isolation</b>
/// All downstream database operations are scoped to
/// <c>FileUploadedEvent.TenantId</c> via <see cref="WorkerTenantContext"/>.
/// Cross-tenant writes are prevented by EF Core's <c>ApplyTenantId</c> interceptor
/// and SQL Server Row-Level Security.
/// </para>
/// </summary>
public sealed class FileUploadedEventConsumer : IConsumer<FileUploadedEvent>
{
    private readonly WorkerTenantContext _tenantContext;
    private readonly IFileStorageResolver _storageResolver;
    private readonly ICsvStreamingParser _csvParser;
    private readonly IValidationPipeline _validationPipeline;
    private readonly IBatchRowInserter _batchInserter;
    private readonly IInvalidRowPersister _invalidRowPersister;
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
    /// <param name="validationPipeline">
    /// Three-stage validation pipeline: schema → business rules → duplicate detection.
    /// </param>
    /// <param name="batchInserter">
    /// Batch inserter — commits valid rows every <c>BatchSize</c> rows in a single round-trip.
    /// </param>
    /// <param name="invalidRowPersister">
    /// Persists rejected rows to the <c>InvalidTransactions</c> table.
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
        IValidationPipeline validationPipeline,
        IBatchRowInserter batchInserter,
        IInvalidRowPersister invalidRowPersister,
        IUploadIdempotencyGuard idempotencyGuard,
        ILogger<FileUploadedEventConsumer> logger)
    {
        _tenantContext = tenantContext;
        _storageResolver = storageResolver;
        _csvParser = csvParser;
        _validationPipeline = validationPipeline;
        _batchInserter = batchInserter;
        _invalidRowPersister = invalidRowPersister;
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
            message.UploadId, message.TenantId, message.FileName,
            message.FileSizeBytes, message.DataRowCount,
            message.SourceSystem, message.StoragePath,
            message.UploadedAt, context.GetRetryCount());

        // ── Step 4: Idempotency check ─────────────────────────────────────────
        var claimed = await _idempotencyGuard.TryBeginAsync(
            message.UploadId, message.TenantId, message.SourceSystem, cancellationToken);

        if (!claimed)
        {
            _logger.LogInformation(
                "Upload already processed — skipping duplicate message. UploadId={UploadId}",
                message.UploadId);
            return;
        }

        // ── Step 5: Set tenant context ────────────────────────────────────────
        _tenantContext.TenantId = message.TenantId;

        // ── Steps 6-8: Resolve → Parse → Validate → Insert → Persist errors ──
        int rowsInserted;
        int invalidCount;

        try
        {
            var fullPath = _storageResolver.ResolveFullPath(message.StoragePath);

            _logger.LogDebug(
                "Resolved storage path. StoragePath={StoragePath} FullPath={FullPath} UploadId={UploadId}",
                message.StoragePath, fullPath, message.UploadId);

            var rows = _csvParser.ParseAsync(fullPath, cancellationToken);

            // ── Validation pipeline ────────────────────────────────────────────
            var (valid, invalid) = await _validationPipeline.ClassifyAsync(rows, cancellationToken);

            _logger.LogInformation(
                "Validation complete. UploadId={UploadId} ValidRows={ValidRows} InvalidRows={InvalidRows}",
                message.UploadId, valid.Count, invalid.Count);

            // ── Batch insert valid rows ────────────────────────────────────────
            rowsInserted = await _batchInserter.InsertAsync(
                ToAsyncEnumerable(valid),
                message.TenantId,
                message.UploadId,
                message.SourceSystem,
                cancellationToken);

            // ── Persist invalid rows ───────────────────────────────────────────
            invalidCount = await _invalidRowPersister.PersistAsync(
                invalid, message.UploadId, cancellationToken);

            // ── Step 9: Mark complete ──────────────────────────────────────────
            await _idempotencyGuard.CompleteAsync(message.UploadId, rowsInserted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "FileUploadedEvent processing failed — marking upload as failed. " +
                "UploadId={UploadId} TenantId={TenantId}",
                message.UploadId, message.TenantId);

            await _idempotencyGuard.FailAsync(message.UploadId, CancellationToken.None);
            throw;
        }

        if (rowsInserted != message.DataRowCount)
        {
            _logger.LogWarning(
                "Row count mismatch after batch insert. " +
                "Expected={Expected} Actual={Actual} Invalid={Invalid} " +
                "UploadId={UploadId} TenantId={TenantId}",
                message.DataRowCount, rowsInserted, invalidCount,
                message.UploadId, message.TenantId);
        }

        _logger.LogInformation(
            "FileUploadedEvent handled successfully. UploadId={UploadId} " +
            "RowsInserted={RowsInserted} InvalidRows={InvalidRows}",
            message.UploadId, rowsInserted, invalidCount);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

#pragma warning disable CS1998
    private static async IAsyncEnumerable<ParsedRow> ToAsyncEnumerable(IReadOnlyList<ParsedRow> rows)
    {
        foreach (var row in rows)
            yield return row;
    }
#pragma warning restore CS1998
}
