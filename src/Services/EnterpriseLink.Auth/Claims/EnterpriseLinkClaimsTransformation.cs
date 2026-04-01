using System.Security.Claims;
using EnterpriseLink.Auth.Services;
using Microsoft.AspNetCore.Authentication;

namespace EnterpriseLink.Auth.Claims;

/// <summary>       
/// ASP.NET Core <see cref="IClaimsTransformation"/> that enriches the authenticated
/// principal with EnterpriseLink-specific claims after Entra ID token validation.
///
/// <para><b>What this does</b></para>
/// <list type="number">
///   <item>
///     <description>
///       <b>Maps <c>tid</c> → <c>tenant_id</c></b><br/>
///       Entra ID issues a <c>tid</c> claim containing the customer's Entra directory GUID.
///       This transformer calls <see cref="ITenantMappingService"/> to resolve the
///       corresponding internal EnterpriseLink TenantId and adds it as a
///       <c>tenant_id</c> claim. All downstream services and
///       <c>HttpTenantContext</c> read <c>tenant_id</c>, not <c>tid</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Maps <c>roles</c> → <see cref="ClaimTypes.Role"/></b><br/>
///       Entra ID application roles are emitted in a <c>roles</c> claim array.
///       ASP.NET Core's <c>[Authorize(Roles = "...")]</c> and
///       <c>User.IsInRole()</c> both read <see cref="ClaimTypes.Role"/>
///       (<c>http://schemas.microsoft.com/ws/2008/06/identity/claims/role</c>).
///       This transformer bridges the two claim types so role-based authorization
///       works natively without any changes to controllers.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Execution order</b></para>
/// <code>
/// UseAuthentication()          → validates JWT, populates User with Entra claims
///   └─ IClaimsTransformation   → THIS class: adds tenant_id + ClaimTypes.Role
/// UseTenantResolution()        → TenantMiddleware reads tenant_id from User claims
/// UseAuthorization()           → [Authorize(Roles=...)] uses ClaimTypes.Role
/// </code>
///
/// <para><b>Idempotency</b></para>
/// ASP.NET Core does not guarantee that <see cref="TransformAsync"/> is called only once
/// per request. This implementation checks for an existing <c>tenant_id</c> claim
/// before adding any new claims, making repeated calls safe.
///
/// <para><b>Clone pattern</b></para>
/// Microsoft recommends cloning the principal before modification to avoid mutating
/// a shared instance. The original principal is read-only; all enrichment is done
/// on the clone's identity.
///
/// <para><b>Registration</b></para>
/// <code>
/// // Program.cs — must be registered as Scoped (same lifetime as IClaimsTransformation)
/// builder.Services.AddScoped&lt;IClaimsTransformation, EnterpriseLinkClaimsTransformation&gt;();
/// </code>
/// </summary>
public sealed class EnterpriseLinkClaimsTransformation : IClaimsTransformation
{
    /// <summary>The claim type added to the principal for the internal TenantId.</summary>
    public const string TenantIdClaimType = "tenant_id";

    /// <summary>The Entra ID claim that identifies the customer's directory.</summary>
    private const string EntraTenantIdClaimType = "tid";

    /// <summary>The Entra ID claim that carries application role assignments.</summary>
    private const string EntraRolesClaimType = "roles";

    private readonly ITenantMappingService _tenantMapper;
    private readonly ILogger<EnterpriseLinkClaimsTransformation> _logger;

    /// <summary>
    /// Initialises the transformer with its required services.
    /// </summary>
    /// <param name="tenantMapper">
    /// Maps Entra directory GUIDs (<c>tid</c> claim) to EnterpriseLink internal TenantIds.
    /// </param>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    public EnterpriseLinkClaimsTransformation(
        ITenantMappingService tenantMapper,
        ILogger<EnterpriseLinkClaimsTransformation> logger)
    {
        _tenantMapper = tenantMapper;
        _logger = logger;
    }

    /// <summary>
    /// Enriches the validated Entra ID principal with EnterpriseLink-specific claims.
    /// </summary>
    /// <param name="principal">The principal produced by Entra ID JWT validation.</param>
    /// <returns>
    /// An enriched clone of <paramref name="principal"/> with <c>tenant_id</c> and
    /// <see cref="ClaimTypes.Role"/> claims added, or the original principal unchanged
    /// if this method has already run for this request (idempotency guard).
    /// </returns>
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // ── Idempotency guard ─────────────────────────────────────────────────
        // IClaimsTransformation can be called multiple times per request.
        // If tenant_id is already present we've already enriched this principal.
        if (principal.HasClaim(c => c.Type == TenantIdClaimType))
            return Task.FromResult(principal);

        // ── Clone ─────────────────────────────────────────────────────────────
        // Build a genuinely independent identity to guarantee the original principal
        // is never mutated. ClaimsPrincipal.Clone() can share the underlying _claims
        // list on some runtimes; creating a new ClaimsIdentity via its copy constructor
        // always produces a fresh, independent list.
        var sourceIdentity = principal.Identity as ClaimsIdentity
            ?? new ClaimsIdentity(principal.Claims);
        var clonedIdentity = sourceIdentity.Clone();
        var cloned = new ClaimsPrincipal(clonedIdentity);
        var identity = clonedIdentity;

        // ── Step 1: Map tid → tenant_id ───────────────────────────────────────
        var entraTid = principal.FindFirst(EntraTenantIdClaimType)?.Value;

        if (!string.IsNullOrWhiteSpace(entraTid))
        {
            var internalTenantId = _tenantMapper.MapEntraTenant(entraTid);

            if (internalTenantId.HasValue)
            {
                identity.AddClaim(
                    new Claim(TenantIdClaimType, internalTenantId.Value.ToString()));

                _logger.LogDebug(
                    "Claims transformation: tid={EntraTid} → tenant_id={TenantId}",
                    entraTid, internalTenantId.Value);
            }
            else
            {
                // Tenant not registered — no tenant_id claim added.
                // AuthController.ExchangeToken will reject with 401 when it maps.
                _logger.LogWarning(
                    "Claims transformation: Entra tenant {EntraTid} has no mapping. " +
                    "tenant_id claim will be absent.",
                    entraTid);
            }
        }

        // ── Step 2: Map roles → ClaimTypes.Role ───────────────────────────────
        // Entra app roles arrive as a JSON array in the "roles" claim.
        // Microsoft.Identity.Web parses these into individual Claim("roles", value) entries.
        // We add a parallel ClaimTypes.Role claim so [Authorize(Roles=...)] works natively.
        // Materialize to a list before iterating — FindAll returns a lazy enumerator
        // over the identity's claims list, and calling AddClaim inside the loop would
        // trigger "collection was modified" on runtimes where the iterator holds a
        // direct reference to the live list.
        var rolesCopied = 0;
        foreach (var roleClaim in principal.FindAll(EntraRolesClaimType).ToList())
        {
            if (!identity.HasClaim(ClaimTypes.Role, roleClaim.Value))
            {
                identity.AddClaim(new Claim(ClaimTypes.Role, roleClaim.Value));
                rolesCopied++;
            }
        }

        if (rolesCopied > 0)
        {
            _logger.LogDebug(
                "Claims transformation: {RoleCount} role(s) mapped to ClaimTypes.Role.",
                rolesCopied);
        }

        return Task.FromResult(cloned);
    }
}
