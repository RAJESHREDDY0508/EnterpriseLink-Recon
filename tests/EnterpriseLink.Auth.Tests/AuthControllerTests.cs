using System.Security.Claims;
using EnterpriseLink.Auth.Controllers;
using EnterpriseLink.Auth.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EnterpriseLink.Auth.Tests;

/// <summary>
/// Unit tests for <see cref="AuthController"/>.
///
/// <para>
/// These tests verify the token exchange and identity resolution logic in isolation.
/// Microsoft.Identity.Web token validation is NOT tested here — that is handled
/// by the library's own test suite. We test what happens after the JWT is already
/// validated and <c>HttpContext.User</c> is populated with decoded claims.
/// </para>
///
/// <para><b>Test strategy</b></para>
/// <list type="bullet">
///   <item><description>
///     Inject <see cref="DefaultHttpContext"/> with a pre-populated
///     <see cref="ClaimsPrincipal"/> to simulate a validated Entra ID token.
///   </description></item>
///   <item><description>
///     Mock <see cref="ITenantMappingService"/> to control tenant resolution
///     without a live database or configuration file.
///   </description></item>
///   <item><description>
///     Assert HTTP status codes and response payload structure.
///   </description></item>
/// </list>
/// </summary>
public sealed class AuthControllerTests
{
    private static readonly Guid EntraTenantId =
        new("22222222-2222-2222-2222-222222222222");

    private static readonly Guid InternalTenantId =
        new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly string ObjectId =
        "11111111-1111-1111-1111-111111111111";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuthController BuildController(
        Guid? mappedTenantId = null,
        string? overrideEntraTid = null,
        string? overrideOid = null,
        string[]? roles = null)
    {
        var mapper = new Mock<ITenantMappingService>();
        mapper
            .Setup(m => m.MapEntraTenant(It.IsAny<string>()))
            .Returns(mappedTenantId);

        var controller = new AuthController(
            mapper.Object,
            NullLogger<AuthController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(overrideEntraTid, overrideOid, roles),
        };

        return controller;
    }

    private static DefaultHttpContext BuildHttpContext(
        string? tid = null,
        string? oid = null,
        string[]? roles = null)
    {
        var claims = new List<Claim>
        {
            new("tid", tid ?? EntraTenantId.ToString()),
            new("oid", oid ?? ObjectId),
            new("preferred_username", "john.doe@contoso.com"),
            new("name", "John Doe"),
            new("exp", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString()),
        };

        if (roles is not null)
            claims.AddRange(roles.Select(r => new Claim("roles", r)));

        var identity = new ClaimsIdentity(claims, authenticationType: "Bearer");
        var principal = new ClaimsPrincipal(identity);

        return new DefaultHttpContext { User = principal };
    }

    // ── ExchangeToken — Happy path ─────────────────────────────────────────────

    /// <summary>
    /// A validated Entra token with registered tenant yields 200 with TenantId.
    /// </summary>
    [Fact]
    public void ExchangeToken_returns_200_with_internal_TenantId_when_tenant_is_registered()
    {
        var controller = BuildController(mappedTenantId: InternalTenantId);

        var result = controller.ExchangeToken();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<TokenExchangeResponse>().Subject;
        response.TenantId.Should().Be(InternalTenantId);
        response.UserId.Should().Be(ObjectId);
        response.Email.Should().Be("john.doe@contoso.com");
    }

    /// <summary>
    /// Roles assigned in Entra App Registration are forwarded in the response.
    /// </summary>
    [Fact]
    public void ExchangeToken_maps_entra_roles_to_response()
    {
        var controller = BuildController(
            mappedTenantId: InternalTenantId,
            roles: ["Operator", "Auditor"]);

        var result = controller.ExchangeToken();

        var response = ((OkObjectResult)result).Value.Should()
            .BeOfType<TokenExchangeResponse>().Subject;

        response.Roles.Should().BeEquivalentTo(["Operator", "Auditor"]);
    }

    // ── ExchangeToken — Unregistered tenant ───────────────────────────────────

    /// <summary>
    /// An Entra token from an unregistered directory returns 401 (not 403)
    /// to prevent leaking whether the tenant is known to EnterpriseLink.
    /// </summary>
    [Fact]
    public void ExchangeToken_returns_401_when_entra_tenant_is_not_registered()
    {
        var controller = BuildController(mappedTenantId: null);

        var result = controller.ExchangeToken();

        result.Should().BeOfType<UnauthorizedObjectResult>(
            "unregistered tenants must return 401 to avoid leaking registration state");
    }

    // ── ExchangeToken — Missing claims ────────────────────────────────────────

    /// <summary>
    /// A token missing the "tid" claim (malformed or spoofed) returns 401.
    /// </summary>
    [Fact]
    public void ExchangeToken_returns_401_when_tid_claim_is_missing()
    {
        var controller = BuildController(
            mappedTenantId: InternalTenantId,
            overrideEntraTid: ""); // empty tid → resolved as null by FindFirst

        // Override the context with a user that has no tid claim
        var claims = new List<Claim>
        {
            new("oid", ObjectId),
        };
        controller.ControllerContext.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var result = controller.ExchangeToken();

        result.Should().BeOfType<UnauthorizedObjectResult>(
            "missing tid claim must be treated as an invalid token");
    }

    /// <summary>
    /// A token missing the "oid" claim returns 401.
    /// </summary>
    [Fact]
    public void ExchangeToken_returns_401_when_oid_claim_is_missing()
    {
        var controller = BuildController(mappedTenantId: InternalTenantId);

        var claims = new List<Claim>
        {
            new("tid", EntraTenantId.ToString()),
        };
        controller.ControllerContext.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));

        var result = controller.ExchangeToken();

        result.Should().BeOfType<UnauthorizedObjectResult>(
            "missing oid claim must be treated as an invalid token");
    }

    // ── Me — Happy path ───────────────────────────────────────────────────────

    /// <summary>
    /// /me returns decoded identity from the validated Entra token.
    /// </summary>
    [Fact]
    public void Me_returns_200_with_identity_claims_from_validated_token()
    {
        var controller = BuildController(mappedTenantId: InternalTenantId);

        var result = controller.Me();

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<MeResponse>().Subject;
        response.ObjectId.Should().Be(ObjectId);
        response.Email.Should().Be("john.doe@contoso.com");
        response.EntraTenantId.Should().Be(EntraTenantId.ToString());
        response.DisplayName.Should().Be("John Doe");
    }

    /// <summary>
    /// /me token expiry is decoded from the "exp" Unix timestamp claim.
    /// </summary>
    [Fact]
    public void Me_decodes_token_expiry_from_exp_claim()
    {
        var controller = BuildController(mappedTenantId: InternalTenantId);

        var result = controller.Me();

        var response = ((OkObjectResult)result).Value.Should()
            .BeOfType<MeResponse>().Subject;

        response.TokenExpiry.Should().NotBeNull(
            "exp claim is present and should be decoded to DateTimeOffset");
        response.TokenExpiry!.Value.Should().BeAfter(DateTimeOffset.UtcNow,
            "token expiry must be in the future");
    }
}
