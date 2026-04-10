using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Dashboard.Queries;

/// <summary>
/// Query parameters for the <c>GET /api/audit-logs</c> Audit Log endpoint.
///
/// <para>
/// All filters are optional and combinable. Time-range filters (<see cref="From"/>
/// and <see cref="To"/>) use the <c>AuditLog.OccurredAt</c> indexed column and are
/// efficient even against large audit tables.
/// </para>
/// </summary>
public sealed class AuditLogQuery
{
    /// <summary>1-based page number. Defaults to <c>1</c>.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Maximum records per page. Capped at 100. Defaults to <c>20</c>.
    /// </summary>
    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Optional entity type filter (e.g. <c>"Transaction"</c>, <c>"Tenant"</c>).
    /// Case-sensitive — matches the C# class name stored in <c>AuditLog.EntityType</c>.
    /// </summary>
    public string? EntityType { get; init; }

    /// <summary>
    /// Optional tenant filter. Omit to return audit entries across all tenants.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Optional action filter. Accepted values: <c>Added</c>, <c>Modified</c>, <c>Deleted</c>.
    /// Case-sensitive — matches the EF Core <c>EntityState.ToString()</c> value.
    /// </summary>
    public string? Action { get; init; }

    /// <summary>
    /// Inclusive lower bound for <c>OccurredAt</c>. Omit to include the oldest records.
    /// </summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>
    /// Inclusive upper bound for <c>OccurredAt</c>. Omit to include the most recent records.
    /// </summary>
    public DateTimeOffset? To { get; init; }
}
