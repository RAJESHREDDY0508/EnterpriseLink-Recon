namespace EnterpriseLink.Shared.Contracts.Events;

/// <summary>
/// Integration event published by the Ingestion service when a CSV file has been
/// successfully validated and stored. Consumed by the Worker service to begin
/// asynchronous CSV parsing, row validation, and persistence.
///
/// <para><b>Message flow</b></para>
/// <code>
/// Ingestion Service
///   │  POST /api/ingestion/upload succeeds
///   │  File stored at {tenantId}/{uploadId}/{fileName}
///   │
///   ├─ Publish FileUploadedEvent ──► RabbitMQ exchange: "file-uploaded"
///   │                                    │
///   │                                    ▼
///   │                              Worker Service
///   │                              (parse CSV, validate rows, persist)
///   │
///   └─ Return UploadResult to client immediately (non-blocking)
/// </code>
///
/// <para><b>Idempotency</b></para>
/// <see cref="UploadId"/> is the idempotency key. Consumers must check whether a
/// message with this <see cref="UploadId"/> has already been processed before
/// beginning work. MassTransit's outbox pattern (future story) will guarantee
/// at-least-once delivery; consumer idempotency guards against duplicate processing.
///
/// <para><b>Versioning</b></para>
/// New optional properties may be added without breaking existing consumers.
/// Removing or renaming properties is a breaking change and requires a new event type
/// (e.g. <c>FileUploadedEventV2</c>) with a migration period.
///
/// <para><b>Tenant isolation</b></para>
/// <see cref="TenantId"/> is always present and validated before publish.
/// Workers must scope all read and write operations to this tenant ID and must
/// not cross tenant boundaries under any condition.
/// </summary>
public sealed record FileUploadedEvent
{
    /// <summary>
    /// Unique identifier for this upload session. Used as the idempotency key by
    /// the Worker service to prevent duplicate processing of the same file.
    /// </summary>
    public required Guid UploadId { get; init; }

    /// <summary>
    /// Internal EnterpriseLink tenant identifier. All rows parsed from the file
    /// must be persisted with this <c>TenantId</c> to enforce row-level isolation.
    /// </summary>
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Relative storage path returned by <c>IFileStorageService.StoreAsync</c>.
    /// Format: <c>{tenantId}/{uploadId}/{fileName}</c>.
    ///
    /// <para>
    /// The Worker service resolves the absolute path or blob URI by combining this
    /// value with the configured storage root — the event never carries absolute paths
    /// or infrastructure-specific URIs.
    /// </para>
    /// </summary>
    public required string StoragePath { get; init; }

    /// <summary>
    /// Original file name as provided by the client (sanitised by the storage service).
    /// Included for logging and audit purposes.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// File size in bytes. Used by the Worker to allocate appropriate processing
    /// resources and to validate the stored file was not truncated during transfer.
    /// </summary>
    public required long FileSizeBytes { get; init; }

    /// <summary>
    /// Number of data rows detected by the Ingestion service (header excluded).
    /// Used by the Worker as a pre-validation hint — if the Worker counts a
    /// different number of rows after parsing, a data integrity alert is raised.
    /// </summary>
    public required int DataRowCount { get; init; }

    /// <summary>
    /// Identifies the upstream system of record that produced the file.
    /// Determines which column mapping and validation rules the Worker applies.
    /// </summary>
    public required string SourceSystem { get; init; }

    /// <summary>
    /// UTC timestamp at which the Ingestion service accepted, validated, and stored
    /// the file. Used for SLA tracking and dead-letter analysis.
    /// </summary>
    public required DateTimeOffset UploadedAt { get; init; }
}
