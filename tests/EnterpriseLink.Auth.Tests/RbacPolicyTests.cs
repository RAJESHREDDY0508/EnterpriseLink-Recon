using System.Security.Claims;
using EnterpriseLink.Auth.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseLink.Auth.Tests;

/// <summary>
/// Verifies the EnterpriseLink RBAC policy definitions using
/// <see cref="IAuthorizationService"/> directly.
///
/// <para>
/// Testing policies at the <see cref="IAuthorizationService"/> level is the correct
/// unit test boundary: it proves the access-control rules are correct without spinning
/// up an HTTP pipeline or controllers.
/// </para>
///
/// <para><b>Coverage matrix</b></para>
/// <code>
/// Policy                │ Admin │ Auditor │ Operator │ Vendor │ None
/// ──────────────────────┼───────┼─────────┼──────────┼────────┼──────
/// RequireAdmin          │  ✓    │  ✗      │  ✗       │  ✗     │  ✗
/// RequireAuditor        │  ✗    │  ✓      │  ✗       │  ✗     │  ✗
/// RequireVendor         │  ✗    │  ✗      │  ✗       │  ✓     │  ✗
/// RequireOperator       │  ✗    │  ✗      │  ✓       │  ✗     │  ✗
/// RequireAuditAccess    │  ✓    │  ✓      │  ✗       │  ✗     │  ✗
/// RequireOperationAccess│  ✓    │  ✗      │  ✓       │  ✓     │  ✗
/// </code>
/// </summary>
public sealed class RbacPolicyTests
{
    // ── Test infrastructure ───────────────────────────────────────────────────

    /// <summary>
    /// Builds an <see cref="IAuthorizationService"/> pre-configured with the
    /// production RBAC policy definitions, identical to those in Program.cs.
    /// </summary>
    private static IAuthorizationService BuildAuthorizationService()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyNames.RequireAdmin, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Admin));

            options.AddPolicy(PolicyNames.RequireAuditor, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Auditor));

            options.AddPolicy(PolicyNames.RequireVendor, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Vendor));

            options.AddPolicy(PolicyNames.RequireOperator, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Operator));

            options.AddPolicy(PolicyNames.RequireAuditAccess, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Admin, Roles.Auditor));

            options.AddPolicy(PolicyNames.RequireOperationAccess, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireRole(Roles.Admin, Roles.Operator, Roles.Vendor));
        });

        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    /// <summary>
    /// Builds an authenticated <see cref="ClaimsPrincipal"/> bearing the specified roles.
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal(params string[] roles)
    {
        var claims = roles
            .Select(r => new Claim(ClaimTypes.Role, r))
            .ToList<Claim>();

        // authenticationType != null → IsAuthenticated == true
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer"));
    }

    /// <summary>Unauthenticated principal (no authentication type set).</summary>
    private static ClaimsPrincipal AnonymousPrincipal() =>
        new(new ClaimsIdentity());

    // ── Helper: evaluate and assert ───────────────────────────────────────────

    private static async Task ShouldSucceed(
        IAuthorizationService auth, ClaimsPrincipal principal, string policy)
    {
        var result = await auth.AuthorizeAsync(principal, resource: null, policy);
        result.Succeeded.Should().BeTrue(
            $"principal with roles [{string.Join(", ", principal.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value))}] " +
            $"should be allowed by policy '{policy}'");
    }

    private static async Task ShouldFail(
        IAuthorizationService auth, ClaimsPrincipal principal, string policy)
    {
        var result = await auth.AuthorizeAsync(principal, resource: null, policy);
        result.Succeeded.Should().BeFalse(
            $"principal with roles [{string.Join(", ", principal.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value))}] " +
            $"should be denied by policy '{policy}'");
    }

    // ── RequireAdmin ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RequireAdmin_allows_Admin()
    {
        var auth = BuildAuthorizationService();
        await ShouldSucceed(auth, BuildPrincipal(Roles.Admin), PolicyNames.RequireAdmin);
    }

    [Theory]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Vendor)]
    [InlineData(Roles.Operator)]
    public async Task RequireAdmin_denies_non_Admin(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, BuildPrincipal(role), PolicyNames.RequireAdmin);
    }

    [Fact]
    public async Task RequireAdmin_denies_unauthenticated()
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, AnonymousPrincipal(), PolicyNames.RequireAdmin);
    }

    // ── RequireAuditor ────────────────────────────────────────────────────────

    [Fact]
    public async Task RequireAuditor_allows_Auditor()
    {
        var auth = BuildAuthorizationService();
        await ShouldSucceed(auth, BuildPrincipal(Roles.Auditor), PolicyNames.RequireAuditor);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Vendor)]
    [InlineData(Roles.Operator)]
    public async Task RequireAuditor_denies_non_Auditor(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, BuildPrincipal(role), PolicyNames.RequireAuditor);
    }

    // ── RequireVendor ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RequireVendor_allows_Vendor()
    {
        var auth = BuildAuthorizationService();
        await ShouldSucceed(auth, BuildPrincipal(Roles.Vendor), PolicyNames.RequireVendor);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Operator)]
    public async Task RequireVendor_denies_non_Vendor(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, BuildPrincipal(role), PolicyNames.RequireVendor);
    }

    // ── RequireOperator ───────────────────────────────────────────────────────

    [Fact]
    public async Task RequireOperator_allows_Operator()
    {
        var auth = BuildAuthorizationService();
        await ShouldSucceed(auth, BuildPrincipal(Roles.Operator), PolicyNames.RequireOperator);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Auditor)]
    [InlineData(Roles.Vendor)]
    public async Task RequireOperator_denies_non_Operator(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, BuildPrincipal(role), PolicyNames.RequireOperator);
    }

    // ── RequireAuditAccess (Admin OR Auditor) ─────────────────────────────────

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Auditor)]
    public async Task RequireAuditAccess_allows_Admin_and_Auditor(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldSucceed(auth, BuildPrincipal(role), PolicyNames.RequireAuditAccess);
    }

    [Theory]
    [InlineData(Roles.Vendor)]
    [InlineData(Roles.Operator)]
    public async Task RequireAuditAccess_denies_Vendor_and_Operator(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, BuildPrincipal(role), PolicyNames.RequireAuditAccess);
    }

    // ── RequireOperationAccess (Admin OR Operator OR Vendor) ──────────────────

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Vendor)]
    public async Task RequireOperationAccess_allows_Admin_Operator_Vendor(string role)
    {
        var auth = BuildAuthorizationService();
        await ShouldSucceed(auth, BuildPrincipal(role), PolicyNames.RequireOperationAccess);
    }

    [Fact]
    public async Task RequireOperationAccess_denies_Auditor()
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, BuildPrincipal(Roles.Auditor), PolicyNames.RequireOperationAccess);
    }

    [Fact]
    public async Task RequireOperationAccess_denies_unauthenticated()
    {
        var auth = BuildAuthorizationService();
        await ShouldFail(auth, AnonymousPrincipal(), PolicyNames.RequireOperationAccess);
    }
}
