using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;

namespace EnterpriseLink.Dashboard.Services;

/// <summary>
/// Provides read-only access to the audit trail for the Audit Logs dashboard.
///
/// <para>
/// Queries the <c>AuditLogs</c> table. Because <c>DashboardTenantContext</c>
/// has <c>HasTenant = false</c>, the <c>AppDbContext</c> AuditLog query filter already
/// passes all rows, so cross-tenant visibility is available without explicit
/// <c>IgnoreQueryFilters()</c>. Optional per-request filters narrow the result set.
/// </para>
///
/// <para><b>Acceptance criterion: UI displays real-time data (Audit Logs module)</b></para>
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Returns a paginated list of audit log entries, optionally filtered by entity type,
    /// tenant, action, and time range. Results are ordered by <c>OccurredAt</c> descending.
    /// </summary>
    /// <param name="query">Filter and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of <see cref="AuditLogDto"/> records.</returns>
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default);
}
