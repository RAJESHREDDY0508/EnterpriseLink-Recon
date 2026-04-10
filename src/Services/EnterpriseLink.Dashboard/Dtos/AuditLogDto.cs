namespace EnterpriseLink.Dashboard.Dtos;

/// <summary>
/// Read-only projection of an <c>AuditLog</c> entry for the Audit Logs dashboard.
///
/// <para>
/// Contains the complete before/after state snapshot for a single entity change,
/// enabling compliance officers and auditors to reconstruct the exact state of any
/// <c>AuditableEntity</c> at any point in time.
/// </para>
/// </summary>
/// <param name="AuditLogId">Primary key of the audit entry.</param>
/// <param name="EntityType">
/// Short class name of the changed entity (e.g. <c>"Transaction"</c>, <c>"Tenant"</c>).
/// </param>
/// <param name="EntityId">
/// Primary-key value(s) of the changed entity, comma-separated for composite keys.
/// </param>
/// <param name="TenantId">
/// Owning tenant at the time of the change. <c>null</c> for non-tenant-scoped entities
/// such as <c>Tenant</c> itself.
/// </param>
/// <param name="Action">
/// EF Core change-tracker action: <c>"Added"</c>, <c>"Modified"</c>, or <c>"Deleted"</c>.
/// Soft deletes appear as <c>"Modified"</c> with <c>IsDeleted=true</c> in <see cref="NewValues"/>.
/// </param>
/// <param name="OldValues">
/// JSON dictionary of property values <em>before</em> the change. <c>null</c> for inserts.
/// </param>
/// <param name="NewValues">
/// JSON dictionary of property values <em>after</em> the change. <c>null</c> for hard deletes.
/// </param>
/// <param name="OccurredAt">UTC timestamp when the change was committed.</param>
public sealed record AuditLogDto(
    Guid AuditLogId,
    string EntityType,
    string EntityId,
    Guid? TenantId,
    string Action,
    string? OldValues,
    string? NewValues,
    DateTimeOffset OccurredAt);
