using System.Security.Claims;
using EnterpriseLink.Auth.Claims;
using EnterpriseLink.Auth.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EnterpriseLink.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="EnterpriseLinkClaimsTransformation"/>.
///
/// <para>
/// These tests verify that the claims transformation pipeline correctly:
/// <list type="bullet">
///   <item><description>Maps the Entra <c>tid</c> claim to the internal <c>tenant_id</c> claim.</description></item>
///   <item><description>Maps Entra <c>roles</c> claims to <see cref="ClaimTypes.Role"/>.</description></item>
///   <item><description>Is idempotent — a second call with an already-enriched principal is a no-op.</description></item>
///   <item><description>Never mutates the original principal — always operates on a clone.</description></item>
/// </list>
/// </para>
///
/// <para><b>Test strategy</b></para>
/// <list type="bullet">
///   <item><description>
///     All tests build a <see cref="ClaimsPrincipal"/> in-process — no HTTP pipeline or
///     real Entra token is required.
///   </description></item>
///   <item><description>
///     <see cref="ITenantMappingService"/> is mocked to decouple mapping logic from
///     file/database configuration.
///   </description></item>
/// </list>
/// </summary>
public sealed class ClaimsTransformationTests
{
    private static readonly Guid EntraTenantId =
        new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid InternalTenantId =
        new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Builds a transformer backed by a mock that resolves <paramref name="resolved"/>.</summary>
    private static EnterpriseLinkClaimsTransformation BuildTransformer(Guid? resolved = null)
    {
        var mapper = new Mock<ITenantMappingService>();
        mapper
            .Setup(m => m.MapEntraTenant(It.IsAny<string>()))
            .Returns(resolved);

        return new EnterpriseLinkClaimsTransformation(
            mapper.Object,
            NullLogger<EnterpriseLinkClaimsTransformation>.Instance);
    }

    /// <summary>Builds a principal with the specified claims.</summary>
    private static ClaimsPrincipal BuildPrincipal(
        string? tid = null,
        string[]? roles = null,
        bool includeTenantId = false)
    {
        var claims = new List<Claim>();

        if (tid is not null)
            claims.Add(new Claim("tid", tid));

        if (roles is not null)
            claims.AddRange(roles.Select(r => new Claim("roles", r)));

        if (includeTenantId)
            claims.Add(new Claim(EnterpriseLinkClaimsTransformation.TenantIdClaimType, InternalTenantId.ToString()));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    // ── tenant_id mapping ─────────────────────────────────────────────────────

    /// <summary>
    /// When the <c>tid</c> claim maps to a registered tenant, the enriched principal
    /// must contain a <c>tenant_id</c> claim with the internal GUID.
    /// </summary>
    [Fact]
    public async Task TransformAsync_adds_tenant_id_claim_when_tid_maps_to_registered_tenant()
    {
        var transformer = BuildTransformer(resolved: InternalTenantId);
        var principal = BuildPrincipal(tid: EntraTenantId.ToString());

        var result = await transformer.TransformAsync(principal);

        result.HasClaim(EnterpriseLinkClaimsTransformation.TenantIdClaimType, InternalTenantId.ToString())
              .Should().BeTrue("tenant_id must be present when the Entra tid is registered");
    }

    /// <summary>
    /// When the Entra <c>tid</c> has no registered mapping, no <c>tenant_id</c> claim
    /// is added — the controller will reject the request with 401.
    /// </summary>
    [Fact]
    public async Task TransformAsync_omits_tenant_id_claim_when_tid_is_not_registered()
    {
        var transformer = BuildTransformer(resolved: null);
        var principal = BuildPrincipal(tid: EntraTenantId.ToString());

        var result = await transformer.TransformAsync(principal);

        result.HasClaim(c => c.Type == EnterpriseLinkClaimsTransformation.TenantIdClaimType)
              .Should().BeFalse("unregistered tenants must not receive a tenant_id claim");
    }

    /// <summary>
    /// When the principal contains no <c>tid</c> claim (malformed or spoofed token),
    /// the transformation completes without error and adds no <c>tenant_id</c>.
    /// </summary>
    [Fact]
    public async Task TransformAsync_omits_tenant_id_when_tid_claim_is_absent()
    {
        var transformer = BuildTransformer(resolved: InternalTenantId);
        var principal = BuildPrincipal(tid: null); // no tid claim

        var result = await transformer.TransformAsync(principal);

        result.HasClaim(c => c.Type == EnterpriseLinkClaimsTransformation.TenantIdClaimType)
              .Should().BeFalse("absent tid must not produce a tenant_id claim");
    }

    // ── roles mapping ─────────────────────────────────────────────────────────

    /// <summary>
    /// Entra <c>roles</c> claims must be mirrored as <see cref="ClaimTypes.Role"/>
    /// so that <c>[Authorize(Roles = "...")]</c> and <c>User.IsInRole()</c> work natively.
    /// </summary>
    [Fact]
    public async Task TransformAsync_maps_entra_roles_to_ClaimTypes_Role()
    {
        var transformer = BuildTransformer(resolved: InternalTenantId);
        var principal = BuildPrincipal(
            tid: EntraTenantId.ToString(),
            roles: ["Operator", "Auditor"]);

        var result = await transformer.TransformAsync(principal);

        result.IsInRole("Operator").Should().BeTrue("Operator role must be mapped to ClaimTypes.Role");
        result.IsInRole("Auditor").Should().BeTrue("Auditor role must be mapped to ClaimTypes.Role");
    }

    /// <summary>
    /// When the principal has no <c>roles</c> claim, no <see cref="ClaimTypes.Role"/>
    /// claims are added — the transformation must still complete without error.
    /// </summary>
    [Fact]
    public async Task TransformAsync_succeeds_without_roles_claim()
    {
        var transformer = BuildTransformer(resolved: InternalTenantId);
        var principal = BuildPrincipal(tid: EntraTenantId.ToString());

        var result = await transformer.TransformAsync(principal);

        result.Claims.Where(c => c.Type == ClaimTypes.Role)
              .Should().BeEmpty("no roles in token should mean no ClaimTypes.Role claims");
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    /// <summary>
    /// A second call on an already-enriched principal must return it unchanged
    /// (no duplicate claims). ASP.NET Core does not guarantee a single call per request.
    /// </summary>
    [Fact]
    public async Task TransformAsync_is_idempotent_and_returns_original_principal_on_second_call()
    {
        var transformer = BuildTransformer(resolved: InternalTenantId);
        var principal = BuildPrincipal(
            tid: EntraTenantId.ToString(),
            roles: ["Operator"]);

        // First call enriches the principal.
        var enriched = await transformer.TransformAsync(principal);

        // Second call should detect tenant_id is already present and return as-is.
        var secondCall = await transformer.TransformAsync(enriched);

        var tenantIdClaims = secondCall.Claims
            .Where(c => c.Type == EnterpriseLinkClaimsTransformation.TenantIdClaimType)
            .ToList();

        tenantIdClaims.Should().HaveCount(1, "idempotency guard must prevent duplicate tenant_id claims");
    }

    // ── Clone isolation ───────────────────────────────────────────────────────

    /// <summary>
    /// The transformation must never mutate the original principal.
    /// The returned object must be a distinct clone.
    /// </summary>
    [Fact]
    public async Task TransformAsync_returns_a_clone_and_does_not_mutate_original()
    {
        var transformer = BuildTransformer(resolved: InternalTenantId);
        var principal = BuildPrincipal(tid: EntraTenantId.ToString());

        var result = await transformer.TransformAsync(principal);

        result.Should().NotBeSameAs(principal,
            "TransformAsync must return a clone, not the original principal");

        principal.HasClaim(c => c.Type == EnterpriseLinkClaimsTransformation.TenantIdClaimType)
                 .Should().BeFalse("the original principal must not be mutated");
    }
}
