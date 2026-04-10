using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseLink.Dashboard.Controllers;

/// <summary>
/// Exposes the audit trail for the Audit Logs dashboard module.
///
/// <para>
/// The endpoint supports rich filtering — entity type, action, tenant, and time
/// range — enabling both point-in-time lookups ("what changed to Transaction X at
/// 14:32?") and broad compliance sweeps ("all inserts in the last 7 days").
/// </para>
///
/// <para><b>Story 3 — Frontend Dashboard (Audit Logs module)</b></para>
/// <para>Acceptance criterion: <b>UI displays real-time data</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <c>GET /api/audit-logs</c> — paginated audit entries with optional
///       entity type, action, tenant, and time-range filters.
///     </description>
///   </item>
/// </list>
/// </summary>
[ApiController]
[Route("api/audit-logs")]
[Produces("application/json")]
public sealed class AuditLogController : ControllerBase
{
    private readonly IAuditLogService _service;
    private readonly ILogger<AuditLogController> _logger;

    /// <summary>
    /// Initialises the controller with its required dependencies.
    /// </summary>
    /// <param name="service">Audit log query service.</param>
    /// <param name="logger">Structured logger.</param>
    public AuditLogController(
        IAuditLogService service,
        ILogger<AuditLogController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ── GET /api/audit-logs ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of audit log entries, ordered by occurrence time
    /// (most recent first).
    ///
    /// <para>
    /// All filter parameters are optional and combinable:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><c>entityType</c> — e.g. <c>Transaction</c>, <c>Tenant</c></description></item>
    ///   <item><description><c>action</c> — <c>Added</c>, <c>Modified</c>, or <c>Deleted</c></description></item>
    ///   <item><description><c>tenantId</c> — restrict to a single tenant</description></item>
    ///   <item><description><c>from</c> / <c>to</c> — ISO 8601 UTC time range on <c>OccurredAt</c></description></item>
    /// </list>
    /// </summary>
    /// <param name="query">Filter and pagination parameters.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="PagedResult{T}"/> of <see cref="AuditLogDto"/>.
    /// </returns>
    /// <response code="200">Paginated audit log returned.</response>
    /// <response code="400">Query parameters failed validation.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AuditLogDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAuditLogsAsync(
        [FromQuery] AuditLogQuery query,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "AuditLog: listing entries. Page={Page} EntityType={EntityType} " +
            "Action={Action} TenantId={TenantId} From={From} To={To}",
            query.Page, query.EntityType, query.Action, query.TenantId, query.From, query.To);

        var result = await _service.GetAuditLogsAsync(query, cancellationToken);
        return Ok(result);
    }
}
