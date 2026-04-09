using EnterpriseLink.Shared.Domain.Enums;

namespace EnterpriseLink.Shared.Domain.Entities;

/// <summary>
/// Idempotency record that tracks the processing lifecycle of a single file upload.
///
/// <para>
/// One <see cref="ProcessedUpload"/> row is inserted per <c>FileUploadedEvent.UploadId</c>
/// before processing begins. If a duplicate message arrives (e.g. RabbitMQ re-delivery
/// after a network partition), the worker discovers the existing row and skips
/// re-processing, preventing duplicate <see cref="Transaction"/> inserts.
/// </para>
///
/// <para><b>Lifecycle</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="UploadProcessingStatus.Processing"/>: row inserted at claim time.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="UploadProcessingStatus.Completed"/>: updated after all batches
///       are committed; <see cref="RowsInserted"/> reflects the final count.
///     </description>
///   </item>
///   <item>
///     <description>
///       <see cref="UploadProcessingStatus.Failed"/>: updated when an unrecoverable
///       error occurs; the record is retained for diagnostics.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Uniqueness guarantee</b></para>
/// <see cref="UploadId"/> is the primary key (value-generated never — it is taken
/// directly from the event). The database-level primary key constraint prevents two
/// concurrent worker instances from both claiming the same upload, even under race
/// conditions at millisecond granularity.
///
/// <para><b>Tenant isolation</b></para>
/// Unlike <see cref="Transaction"/> and <see cref="User"/>, this entity does NOT
/// implement <see cref="MultiTenancy.ITenantScoped"/>. The idempotency check must find
/// an <see cref="UploadId"/> regardless of which tenant the caller's context is scoped
/// to, so no tenant-partition query filter is applied. <see cref="TenantId"/> is stored
/// for audit purposes only.
/// </para>
/// </summary>
public sealed class ProcessedUpload : AuditableEntity
{
    /// <summary>
    /// Idempotency key — equals <c>FileUploadedEvent.UploadId</c>.
    /// Primary key; value is supplied externally (not database-generated).
    /// </summary>
    public Guid UploadId { get; set; }

    /// <summary>
    /// Tenant that owns this upload.
    /// Set from <c>FileUploadedEvent.TenantId</c>; stored for audit queries.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>Processing lifecycle state.</summary>
    public UploadProcessingStatus Status { get; set; }

    /// <summary>
    /// Total rows inserted across all batches.
    /// Zero while <see cref="Status"/> is <see cref="UploadProcessingStatus.Processing"/>;
    /// updated to the final count when transitioning to
    /// <see cref="UploadProcessingStatus.Completed"/>.
    /// </summary>
    public int RowsInserted { get; set; }

    /// <summary>
    /// Upstream system identifier from the event (e.g. "SalesForce", "SAP").
    /// Stored for diagnostics and future source-system routing rules.
    /// </summary>
    public string SourceSystem { get; set; } = string.Empty;

    // ── Navigation ─────────────────────────────────────────────────────────────

    /// <summary>Owning tenant (populated when explicitly loaded via <c>Include</c>).</summary>
    public Tenant? Tenant { get; set; }
}
