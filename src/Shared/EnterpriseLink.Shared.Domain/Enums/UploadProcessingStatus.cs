namespace EnterpriseLink.Shared.Domain.Enums;

/// <summary>
/// Processing lifecycle states for a <see cref="Entities.ProcessedUpload"/> record.
///
/// <para>
/// The state machine is intentionally simple: a record starts as
/// <see cref="Processing"/> and transitions to either <see cref="Completed"/>
/// (all rows inserted) or <see cref="Failed"/> (unrecoverable error after retries).
/// <see cref="Failed"/> records may be retried by re-delivering the original
/// <c>FileUploadedEvent</c> message — the idempotency guard resets the status
/// back to <see cref="Processing"/> on each retry.
/// </para>
/// </summary>
public enum UploadProcessingStatus
{
    /// <summary>
    /// A worker instance has claimed this upload and is currently inserting rows.
    /// Only one worker should hold this state at a time — the database PK prevents
    /// a second worker from inserting a duplicate row.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// All batches were committed successfully.
    /// Duplicate messages for this upload are silently skipped.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Processing failed after all MassTransit retries were exhausted.
    /// The upload can be retried by re-delivering the original message.
    /// </summary>
    Failed = 3,
}
