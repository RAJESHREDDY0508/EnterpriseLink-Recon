using EnterpriseLink.Shared.Contracts.Events;
using MassTransit;

namespace EnterpriseLink.Worker.Consumers;

/// <summary>
/// MassTransit consumer that subscribes to the <see cref="FileUploadedEvent"/> queue
/// and orchestrates asynchronous CSV processing for the EnterpriseLink Worker service.
///
/// <para><b>Subscription topology</b></para>
/// MassTransit binds this consumer to a durable RabbitMQ queue named
/// <c>EnterpriseLink.Worker.Consumers:FileUploadedEventConsumer</c> (derived from
/// the fully-qualified consumer type name). The queue is bound to the fanout exchange
/// created by the Ingestion service publisher for <see cref="FileUploadedEvent"/>.
///
/// <para><b>Message handling pipeline</b></para>
/// <code>
/// RabbitMQ queue
///   │  FileUploadedEvent received
///   ▼
/// FileUploadedEventConsumer.Consume()
///   │  1. Validate message fields are non-null / non-empty
///   │  2. Log structured receipt (UploadId, TenantId, FileName, DataRowCount)
///   │  3. [Story 2] Parse CSV rows from StoragePath
///   │  4. [Story 3] Validate and persist rows with tenant isolation
///   │  5. Acknowledge message (MassTransit auto-acks on successful return)
///   ▼
/// Message acknowledged → removed from queue
/// </code>
///
/// <para><b>Error handling</b></para>
/// If <see cref="Consume"/> throws, MassTransit's consumer-level retry policy
/// (configured in <see cref="Extensions.WorkerMessagingExtensions.AddWorkerMessaging"/>)
/// retries with exponential back-off. After all retries are exhausted the message is
/// moved to the dead-letter queue for manual inspection and reprocessing.
///
/// <para><b>Idempotency</b></para>
/// <see cref="FileUploadedEvent.UploadId"/> is the idempotency key. Story 2 will add
/// a persistence check to detect and skip already-processed uploads. This consumer
/// is intentionally stateless so it can scale horizontally across multiple Worker instances.
///
/// <para><b>Tenant isolation</b></para>
/// All downstream operations (Story 2+) must be scoped to
/// <see cref="FileUploadedEvent.TenantId"/>. Cross-tenant reads or writes must never
/// occur regardless of the content of the message.
/// </summary>
public sealed class FileUploadedEventConsumer : IConsumer<FileUploadedEvent>
{
    private readonly ILogger<FileUploadedEventConsumer> _logger;

    /// <summary>
    /// Initialises the consumer with its required dependencies.
    /// </summary>
    /// <param name="logger">Structured logger — all properties are logged with correlation context.</param>
    public FileUploadedEventConsumer(ILogger<FileUploadedEventConsumer> logger)
    {
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

        // ── Step 1: Validate required message fields ───────────────────────────
        // Defensive guard against malformed or partially-delivered messages.
        // MassTransit schema validation (Story 4) catches most issues at the broker
        // boundary, but runtime guards prevent null-reference propagation into
        // downstream processing logic.
        if (message.UploadId == Guid.Empty ||
            message.TenantId == Guid.Empty ||
            string.IsNullOrWhiteSpace(message.StoragePath) ||
            string.IsNullOrWhiteSpace(message.FileName))
        {
            _logger.LogError(
                "Received malformed FileUploadedEvent — required fields missing or empty. " +
                "MessageId={MessageId} UploadId={UploadId} TenantId={TenantId}",
                context.MessageId, message.UploadId, message.TenantId);

            // Throw to trigger retry → dead-letter. Do not silently discard.
            throw new InvalidOperationException(
                $"FileUploadedEvent {context.MessageId} is malformed: " +
                "UploadId, TenantId, StoragePath and FileName are all required.");
        }

        // ── Guard: UploadedAt must not be in the future ────────────────────────
        // A future timestamp indicates a clock-skew issue on the publishing node, a
        // maliciously crafted message, or a misconfigured time zone. Processing such
        // a message would corrupt SLA tracking and dead-letter analysis timestamps.
        // A 5-minute grace window absorbs normal NTP drift between microservice hosts.
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

        // ── Step 2: Log structured receipt ────────────────────────────────────
        // All properties are logged as named fields so Seq / Elastic can correlate
        // ingestion logs (from the Ingestion service) with processing logs (here)
        // using UploadId as the correlation key.
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

        // ── Step 3: Dispatch to processing pipeline ───────────────────────────
        // Story 2: IFileProcessor.ProcessAsync(message, cancellationToken)
        // Story 3: ITenantRowPersistor.PersistAsync(tenantId, rows, cancellationToken)
        //
        // For Story 1 the acceptance criterion is "subscribes to queue + handles messages".
        // The handler acknowledges the message successfully so the queue drains correctly.
        await Task.CompletedTask;

        _logger.LogInformation(
            "FileUploadedEvent handled successfully. UploadId={UploadId}",
            message.UploadId);
    }
}
