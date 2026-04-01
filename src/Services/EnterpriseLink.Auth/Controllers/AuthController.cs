using EnterpriseLink.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;
using System.Security.Claims;

namespace EnterpriseLink.Auth.Controllers;

/// <summary>
/// Handles Entra ID token validation and tenant-aware identity resolution
/// for the EnterpriseLink Recon platform.
///
/// <para><b>Authentication Flow</b></para>
/// <code>
/// ┌──────────┐   1. User credentials    ┌───────────┐
/// │  Client  │ ────────────────────────▶│  Entra ID │
/// │          │                          │  (Azure)  │
/// │          │ ◀──────────────────────── │           │
/// │          │   2. JWT (Entra token)   └───────────┘
/// │          │
/// │          │   3. POST /api/auth/token/exchange
/// │          │      Authorization: Bearer {entra-token}
/// │          │ ────────────────────────▶┌────────────┐
/// │          │                          │Auth Service│
/// │          │                          │ (this)     │
/// │          │ ◀──────────────────────── │            │
/// │          │   4. { tenantId, roles } └────────────┘
/// └──────────┘
/// </code>
///
/// <para><b>Security notes</b></para>
/// <list type="bullet">
///   <item><description>
///     All endpoints require a valid Entra ID JWT. Microsoft.Identity.Web validates the
///     token signature, audience, issuer, and expiry before the action method is invoked.
///   </description></item>
///   <item><description>
///     The <c>tid</c> claim in the Entra token identifies the customer's Entra directory,
///     not our internal tenant. <see cref="ITenantMappingService"/> bridges the two.
///   </description></item>
///   <item><description>
///     No credentials or secrets are stored or returned by this service.
///   </description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : ControllerBase
{
    /// <summary>
    /// Required scope for token exchange. Clients must request this scope when obtaining
    /// a token from Entra ID. Validated by <see cref="RequiredScopeAttribute"/>.
    /// </summary>
    private const string ExchangeScope = "access_as_user";

    private readonly ITenantMappingService _tenantMapper;
    private readonly ILogger<AuthController> _logger;

    /// <summary>
    /// Initialises the controller with its required dependencies.
    /// </summary>
    /// <param name="tenantMapper">Maps Entra tenant IDs to internal EnterpriseLink TenantIds.</param>
    /// <param name="logger">Structured logger.</param>
    public AuthController(
        ITenantMappingService tenantMapper,
        ILogger<AuthController> logger)
    {
        _tenantMapper = tenantMapper;
        _logger = logger;
    }

    // ── POST /api/auth/token/exchange ─────────────────────────────────────────

    /// <summary>
    /// Exchanges a validated Entra ID JWT for the caller's EnterpriseLink tenant identity.
    ///
    /// <para>
    /// The client presents an Entra ID Bearer token. Microsoft.Identity.Web validates it
    /// automatically. This endpoint then maps the Entra <c>tid</c> claim to the
    /// EnterpriseLink internal TenantId and returns identity information that downstream
    /// services use for tenant-scoped operations.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with tenant identity payload on success.<br/>
    /// <c>401 Unauthorized</c> if the Entra token is invalid or the tenant is not registered.
    /// </returns>
    /// <response code="200">Tenant identity successfully resolved.</response>
    /// <response code="401">Token invalid, expired, or tenant not registered in EnterpriseLink.</response>
    [HttpPost("token/exchange")]
    [Authorize]
    [RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
    [ProducesResponseType(typeof(TokenExchangeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult ExchangeToken()
    {
        var entraTenantId = User.FindFirst("tid")?.Value;
        var objectId = User.FindFirst("oid")?.Value;
        var email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst(ClaimTypes.Email)?.Value;
        var name = User.FindFirst("name")?.Value;

        if (string.IsNullOrEmpty(entraTenantId) || string.IsNullOrEmpty(objectId))
        {
            _logger.LogWarning(
                "Token exchange rejected: missing required claims. " +
                "tid={EntraTenantId} oid={ObjectId}",
                entraTenantId, objectId);
            return Unauthorized(new { error = "Required claims are missing from the token." });
        }

        var internalTenantId = _tenantMapper.MapEntraTenant(entraTenantId);
        if (internalTenantId is null)
        {
            // Return 401 (not 403) to avoid leaking whether the tenant exists.
            _logger.LogWarning(
                "Token exchange rejected: Entra tenant {EntraTenantId} is not registered.",
                entraTenantId);
            return Unauthorized(new { error = "Tenant is not registered in EnterpriseLink." });
        }

        var roles = User.Claims
            .Where(c => c.Type is ClaimTypes.Role or "roles")
            .Select(c => c.Value)
            .ToArray();

        _logger.LogInformation(
            "Token exchange successful. EntraTenant={EntraTenantId} → TenantId={TenantId} User={ObjectId}",
            entraTenantId, internalTenantId, objectId);

        return Ok(new TokenExchangeResponse(
            TenantId: internalTenantId.Value,
            UserId: objectId,
            Email: email,
            DisplayName: name,
            Roles: roles,
            IssuedAt: DateTimeOffset.UtcNow));
    }

    // ── GET /api/auth/me ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the identity information of the currently authenticated user
    /// as decoded from their Entra ID JWT.
    ///
    /// <para>
    /// Useful for client applications to verify their token is accepted and to
    /// display user context without storing state on the client.
    /// </para>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with the caller's identity claims.<br/>
    /// <c>401 Unauthorized</c> if no valid Bearer token is provided.
    /// </returns>
    /// <response code="200">Identity claims decoded from the validated Entra ID token.</response>
    /// <response code="401">No valid Bearer token provided.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Me()
    {
        return Ok(new MeResponse(
            ObjectId: User.FindFirst("oid")?.Value,
            Email: User.FindFirst("preferred_username")?.Value
                   ?? User.FindFirst(ClaimTypes.Email)?.Value,
            DisplayName: User.FindFirst("name")?.Value,
            EntraTenantId: User.FindFirst("tid")?.Value,
            Scopes: User.FindFirst("scp")?.Value?.Split(' ') ?? Array.Empty<string>(),
            TokenExpiry: GetTokenExpiry()));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private DateTimeOffset? GetTokenExpiry()
    {
        var expClaim = User.FindFirst("exp")?.Value;
        if (long.TryParse(expClaim, out var exp))
            return DateTimeOffset.FromUnixTimeSeconds(exp);
        return null;
    }
}

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>
/// Response payload for a successful <c>POST /api/auth/token/exchange</c> request.
/// </summary>
/// <param name="TenantId">EnterpriseLink internal TenantId. Use this in all downstream API calls.</param>
/// <param name="UserId">Entra ID Object ID of the authenticated user (stable across credential changes).</param>
/// <param name="Email">User's email address (may be null if not present in the token).</param>
/// <param name="DisplayName">User's display name from Entra ID directory.</param>
/// <param name="Roles">Application roles assigned to the user in the Entra ID App Registration.</param>
/// <param name="IssuedAt">Server-side UTC timestamp of the exchange operation.</param>
public sealed record TokenExchangeResponse(
    Guid TenantId,
    string UserId,
    string? Email,
    string? DisplayName,
    string[] Roles,
    DateTimeOffset IssuedAt);

/// <summary>
/// Response payload for <c>GET /api/auth/me</c>.
/// Reflects the decoded contents of the caller's Entra ID JWT.
/// </summary>
/// <param name="ObjectId">Entra ID Object ID — the stable, immutable identifier for the user.</param>
/// <param name="Email">Primary email / UPN from the token.</param>
/// <param name="DisplayName">Full display name from the Entra ID directory.</param>
/// <param name="EntraTenantId">Entra directory tenant ID (<c>tid</c> claim).</param>
/// <param name="Scopes">OAuth2 scopes granted to this token.</param>
/// <param name="TokenExpiry">UTC expiry time of the presented token.</param>
public sealed record MeResponse(
    string? ObjectId,
    string? Email,
    string? DisplayName,
    string? EntraTenantId,
    string[] Scopes,
    DateTimeOffset? TokenExpiry);
