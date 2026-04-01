using EnterpriseLink.Auth.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseLink.Auth.Controllers;

/// <summary>
/// Demonstrates and validates the EnterpriseLink role-based access control (RBAC)
/// model by exposing one endpoint per access tier.
///
/// <para>
/// In production services (Ingestion, Worker, etc.) the same <c>[Authorize(Policy = ...)]</c>
/// attributes from <see cref="PolicyNames"/> are applied to the business endpoints directly.
/// This controller is the canonical reference implementation for the Auth service.
/// </para>
///
/// <para><b>Role → Endpoint Matrix</b></para>
/// <code>
/// Endpoint                      │ Admin │ Auditor │ Operator │ Vendor
/// ──────────────────────────────┼───────┼─────────┼──────────┼───────
/// GET  /api/rbac/admin-panel    │  ✓    │         │          │
/// GET  /api/rbac/audit-reports  │  ✓    │    ✓    │          │
/// POST /api/rbac/transactions   │  ✓    │         │    ✓     │  ✓
/// GET  /api/rbac/operations     │       │         │    ✓     │
/// </code>
///
/// <para>
/// <b>Authentication prerequisite:</b>
/// Every policy includes <c>RequireAuthenticatedUser()</c>. Requests without a valid
/// Entra ID Bearer token receive <c>401 Unauthorized</c> before role evaluation runs.
/// Role mismatches yield <c>403 Forbidden</c>.
/// </para>
/// </summary>
[ApiController]
[Route("api/rbac")]
[Produces("application/json")]
public sealed class RbacController : ControllerBase
{
    private readonly ILogger<RbacController> _logger;

    /// <summary>
    /// Initialises the controller.
    /// </summary>
    /// <param name="logger">Structured logger.</param>
    public RbacController(ILogger<RbacController> logger)
    {
        _logger = logger;
    }

    // ── GET /api/rbac/admin-panel ─────────────────────────────────────────────

    /// <summary>
    /// Admin-only panel for tenant configuration and user management.
    ///
    /// <para>
    /// Restricted to the <c>Admin</c> role. Auditors, Operators, and Vendors
    /// receive <c>403 Forbidden</c>.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with admin context on success.<br/>
    /// <c>401 Unauthorized</c> if no valid token is provided.<br/>
    /// <c>403 Forbidden</c> if the caller does not hold the Admin role.
    /// </returns>
    /// <response code="200">Admin panel data returned.</response>
    /// <response code="401">No valid Entra ID Bearer token provided.</response>
    /// <response code="403">Caller does not hold the Admin role.</response>
    [HttpGet("admin-panel")]
    [Authorize(Policy = PolicyNames.RequireAdmin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetAdminPanel()
    {
        _logger.LogInformation("Admin panel accessed by {User}", User.Identity?.Name);
        return Ok(new RbacResponse(
            Endpoint: "admin-panel",
            AllowedRoles: [Roles.Admin],
            CallerRoles: GetCallerRoles(),
            Message: "Tenant administration panel — full control access."));
    }

    // ── GET /api/rbac/audit-reports ───────────────────────────────────────────

    /// <summary>
    /// Compliance and audit reports, readable by Admins and Auditors.
    ///
    /// <para>
    /// Uses the <see cref="PolicyNames.RequireAuditAccess"/> composite policy
    /// (Admin OR Auditor). Operators and Vendors receive <c>403 Forbidden</c>.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with report context on success.<br/>
    /// <c>401 Unauthorized</c> if no valid token is provided.<br/>
    /// <c>403 Forbidden</c> if the caller is neither Admin nor Auditor.
    /// </returns>
    /// <response code="200">Audit report data returned.</response>
    /// <response code="401">No valid Entra ID Bearer token provided.</response>
    /// <response code="403">Caller holds neither the Admin nor Auditor role.</response>
    [HttpGet("audit-reports")]
    [Authorize(Policy = PolicyNames.RequireAuditAccess)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetAuditReports()
    {
        _logger.LogInformation("Audit reports accessed by {User}", User.Identity?.Name);
        return Ok(new RbacResponse(
            Endpoint: "audit-reports",
            AllowedRoles: [Roles.Admin, Roles.Auditor],
            CallerRoles: GetCallerRoles(),
            Message: "Compliance and audit report view — read-only access."));
    }

    // ── POST /api/rbac/transactions ───────────────────────────────────────────

    /// <summary>
    /// Submits a transaction for processing. Accessible to Admins, Operators, and Vendors.
    ///
    /// <para>
    /// Uses the <see cref="PolicyNames.RequireOperationAccess"/> composite policy
    /// (Admin OR Operator OR Vendor). Auditors receive <c>403 Forbidden</c>
    /// because they are read-only participants.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with submission confirmation on success.<br/>
    /// <c>401 Unauthorized</c> if no valid token is provided.<br/>
    /// <c>403 Forbidden</c> if the caller is an Auditor or unauthenticated.
    /// </returns>
    /// <response code="200">Transaction accepted for processing.</response>
    /// <response code="401">No valid Entra ID Bearer token provided.</response>
    /// <response code="403">Caller holds only the Auditor role (read-only).</response>
    [HttpPost("transactions")]
    [Authorize(Policy = PolicyNames.RequireOperationAccess)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult SubmitTransaction()
    {
        _logger.LogInformation("Transaction submitted by {User}", User.Identity?.Name);
        return Ok(new RbacResponse(
            Endpoint: "transactions",
            AllowedRoles: [Roles.Admin, Roles.Operator, Roles.Vendor],
            CallerRoles: GetCallerRoles(),
            Message: "Transaction accepted for processing."));
    }

    // ── GET /api/rbac/operations ──────────────────────────────────────────────

    /// <summary>
    /// Day-to-day operations dashboard for Operators only.
    ///
    /// <para>
    /// Restricted to the <c>Operator</c> role. Admins, Auditors, and Vendors
    /// receive <c>403 Forbidden</c>. Admins who need this view should be assigned
    /// both the Admin and Operator roles in the Entra App Registration.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with operations context on success.<br/>
    /// <c>401 Unauthorized</c> if no valid token is provided.<br/>
    /// <c>403 Forbidden</c> if the caller does not hold the Operator role.
    /// </returns>
    /// <response code="200">Operations dashboard data returned.</response>
    /// <response code="401">No valid Entra ID Bearer token provided.</response>
    /// <response code="403">Caller does not hold the Operator role.</response>
    [HttpGet("operations")]
    [Authorize(Policy = PolicyNames.RequireOperator)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetOperationsDashboard()
    {
        _logger.LogInformation("Operations dashboard accessed by {User}", User.Identity?.Name);
        return Ok(new RbacResponse(
            Endpoint: "operations",
            AllowedRoles: [Roles.Operator],
            CallerRoles: GetCallerRoles(),
            Message: "Day-to-day operations and reconciliation dashboard."));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string[] GetCallerRoles() =>
        User.Claims
            .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
            .Select(c => c.Value)
            .ToArray();
}

// ── Response DTO ──────────────────────────────────────────────────────────────

/// <summary>
/// Standard response payload for RBAC-protected endpoints. Includes the
/// caller's resolved roles so clients can inspect access context.
/// </summary>
/// <param name="Endpoint">The name of the accessed endpoint.</param>
/// <param name="AllowedRoles">Roles permitted to access this endpoint.</param>
/// <param name="CallerRoles">Roles held by the authenticated caller.</param>
/// <param name="Message">Human-readable description of the endpoint.</param>
public sealed record RbacResponse(
    string Endpoint,
    string[] AllowedRoles,
    string[] CallerRoles,
    string Message);
