using EnterpriseLink.Shared.Contracts.Events;
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
/// created by the Ingestion service publisher for <see cref="FileUploadedEvent"/>.
///
/// <para><b>Message handling pipeline</b></para>
/// <code>
/// RabbitMQ queue
///   │  FileUploadedEvent received
///   ▼
/// FileUploadedEventConsumer.Consume()
///   │  1. Validate message fields are non-null / non-empty
///   │  2. Guard: UploadedAt must not be more than 5 minutes in the future
///   │  3. Resolve absolute file path via IFileStorageResolver
///   │  4. Stream CSV rows via ICsvStreamingParser (IAsyncEnumerable — O(1) memory)
///   │  5. [Story 3] Batch-insert rows; [Story 4] Idempotency check
///   │  6. Acknowledge message (MassTransit auto-acks on successful return)
///   ▼
/// Message acknowledged → removed from queue
/// </code>
///
/// <para><b>Memory model</b></para>
/// <see cref="ICsvStreamingParser.ParseAsync"/> returns an <c>IAsyncEnumerable</c>
/// that yields one <see cref="ParsedRow"/> at a time. The consumer processes each row
/// inside the <c>await foreach</c> loop body and the row object is immediately eligible
/// for GC. Files of 5 GB or more are handled without out-of-memory exceptions.
///
/// <para><b>Error handling</b></para>
/// If <see cref="Consume"/> throws, MassTransit's consumer-level retry policy
/// (configured in <see cref="Extensions.WorkerMessagingExtensions.AddWorkerMessaging"/>)
/// retries with exponential back-off. After all retries are exhausted the message is
/// moved to the dead-letter queue for manual inspection and reprocessing.
///
/// <para><b>Idempotency</b></para>
/// <see cref="FileUploadedEvent.UploadId"/> is the idempotency key. Story 4 will add
/// a persistence check to detect and skip already-processed uploads.
///
/// <para><b>Tenant isolation</b></para>
/// All downstream operations (Story 3+) must be scoped to
/// <see cref="FileUploadedEvent.TenantId"/>. Cross-tenant reads or writes must never
/// occur regardless of the content of the message.
/// </summary>
public sealed class FileUploadedEventConsumer : IConsumer<FileUploadedEvent>
{
    private readonly IFileStorageResolver _storageResolver;
    private readonly ICsvStreamingParser _csvParser;
    private readonly ILogger<FileUploadedEventConsumer> _logger;

    /// <summary>
    /// Initialises the consumer with its required dependencies.
    /// </summary>
    /// <param name="storageResolver">
    /// Resolves relative storage paths from the event to absolute filesystem paths.
    /// </param>
    /// <param name="csvParser">
    /// Streaming CSV parser — yields rows one at a time, supporting 5 GB+ files.
    /// </param>
    /// <param name="logger">Structured logger — all properties are logged with correlation context.</param>
    public FileUploadedEventConsumer(
        IFileStorageResolver storageResolver,
        ICsvStreamingParser csvParser,
        ILogger<FileUploadedEventConsumer> logger)
    {
        _storageResolver = storageResolver;
        _csvParser = csvParser;
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

        // ── Step 4: Resolve absolute path + stream CSV rows ───────────────────
        // ResolveFullPath validates the relative path and prevents path traversal.
        // ICsvStreamingParser.ParseAsync streams one row at a time (O(1) memory),
        // handling files of any size including 5 GB+ without out-of-memory risk.
        var fullPath = _storageResolver.ResolveFullPath(message.StoragePath);

        _logger.LogDebug(
            "Resolved storage path. StoragePath={StoragePath} FullPath={FullPath} UploadId={UploadId}",
            message.StoragePath, fullPath, message.UploadId);

        var rowsProcessed = 0;

        await foreach (var row in _csvParser.ParseAsync(fullPath, cancellationToken))
        {
            rowsProcessed++;

            // ── Story 3: ITenantRowPersistor.PersistAsync(tenantId, row, ct)
            // ── Story 4: Idempotency check before persisting
            //
            // Placeholder: row is yielded and immediately eligible for GC.
            // The compiler does not optimise away the iteration — rows are
            // consumed from the async iterator, exercising the full streaming path.
            _ = row;
        }

        // ── Integrity hint: log mismatch between expected and actual row count ─
        if (rowsProcessed != message.DataRowCount)
        {
            _logger.LogWarning(
                "Row count mismatch. Expected={Expected} Actual={Actual} " +
                "UploadId={UploadId} TenantId={TenantId}",
                message.DataRowCount, rowsProcessed, message.UploadId, message.TenantId);
        }

        _logger.LogInformation(
            "FileUploadedEvent handled successfully. " +
            "UploadId={UploadId} RowsProcessed={RowsProcessed}",
            message.UploadId, rowsProcessed);
    }
}
