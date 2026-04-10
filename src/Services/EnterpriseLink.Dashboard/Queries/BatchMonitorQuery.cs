using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Dashboard.Queries;

/// <summary>
/// Query parameters for the <c>GET /api/uploads</c> Batch Monitor endpoint.
///
/// <para>
/// All filters are optional. When omitted the endpoint returns all upload records
/// across all tenants ordered by <c>CreatedAt</c> descending (most recent first).
/// </para>
/// </summary>
public sealed class BatchMonitorQuery
{
    /// <summary>1-based page number. Defaults to <c>1</c>.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Maximum records per page. Capped at 100 to prevent runaway queries.
    /// Defaults to <c>20</c>.
    /// </summary>
    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Optional status filter. Accepted values: <c>Processing</c>, <c>Completed</c>, <c>Failed</c>.
    /// Case-insensitive. Omit to return all statuses.
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Optional tenant filter. When supplied, only uploads owned by this tenant are returned.
    /// Omit to return uploads from all tenants (cross-tenant admin view).
    /// </summary>
    public Guid? TenantId { get; init; }
}
