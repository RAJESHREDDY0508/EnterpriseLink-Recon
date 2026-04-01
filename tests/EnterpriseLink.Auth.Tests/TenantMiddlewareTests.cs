using System.Security.Claims;
using EnterpriseLink.Shared.Infrastructure.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Auth.Tests;

/// <summary>
/// Proves that TenantMiddleware correctly resolves the TenantId from:
///   1. JWT "tenant_id" claim
///   2. X-Tenant-Id request header (fallback for internal service calls)
///   3. Claims take priority over headers
///   4. Missing/malformed values leave TenantId unset (no crash)
/// </summary>
public sealed class TenantMiddlewareTests
{
    private static TenantMiddleware BuildMiddleware(RequestDelegate? next = null)
        => new(
            next ?? (_ => Task.CompletedTask),
            NullLogger<TenantMiddleware>.Instance);

    private static DefaultHttpContext BuildContext(
        Guid? claimTenantId = null,
        string? headerTenantId = null)
    {
        var context = new DefaultHttpContext();

        if (claimTenantId.HasValue)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    new[] { new Claim(TenantMiddleware.TenantIdClaim, claimTenantId.Value.ToString()) }));
        }

        if (headerTenantId is not null)
            context.Request.Headers[TenantMiddleware.TenantIdHeader] = headerTenantId;

        return context;
    }

    // ── Test 1: Resolved from JWT claim ──────────────────────────────────────

    [Fact]
    public async Task InvokeAsync_resolves_TenantId_from_JWT_claim()
    {
        var tenantId = Guid.NewGuid();
        var middleware = BuildMiddleware();
        var context = BuildContext(claimTenantId: tenantId);

        await middleware.InvokeAsync(context);

        context.Items[TenantMiddleware.TenantIdItemKey]
            .Should().Be(tenantId, "JWT claim is the primary resolution source");
    }

    // ── Test 2: Resolved from X-Tenant-Id header ──────────────────────────────

    [Fact]
    public async Task InvokeAsync_resolves_TenantId_from_header_when_no_claim()
    {
        var tenantId = Guid.NewGuid();
        var middleware = BuildMiddleware();
        var context = BuildContext(headerTenantId: tenantId.ToString());

        await middleware.InvokeAsync(context);

        context.Items[TenantMiddleware.TenantIdItemKey]
            .Should().Be(tenantId, "header is the fallback source when no JWT claim is present");
    }

    // ── Test 3: JWT claim takes priority over header ──────────────────────────

    [Fact]
    public async Task InvokeAsync_prefers_JWT_claim_over_header()
    {
        var claimTenantId = Guid.NewGuid();
        var headerTenantId = Guid.NewGuid();
        var middleware = BuildMiddleware();
        var context = BuildContext(
            claimTenantId: claimTenantId,
            headerTenantId: headerTenantId.ToString());

        await middleware.InvokeAsync(context);

        context.Items[TenantMiddleware.TenantIdItemKey]
            .Should().Be(claimTenantId, "JWT claim must win over the header value");
    }

    // ── Test 4: No tenant — pipeline continues without crash ─────────────────

    [Fact]
    public async Task InvokeAsync_continues_pipeline_when_no_tenant_present()
    {
        var middleware = BuildMiddleware();
        var context = new DefaultHttpContext(); // no claim, no header

        var act = () => middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync("missing tenant must not crash the pipeline");
        context.Items.ContainsKey(TenantMiddleware.TenantIdItemKey)
            .Should().BeFalse("Items must not be polluted with an empty/default TenantId");
    }

    // ── Test 5: Malformed header value is silently ignored ────────────────────

    [Fact]
    public async Task InvokeAsync_ignores_malformed_header_value()
    {
        var middleware = BuildMiddleware();
        var context = BuildContext(headerTenantId: "not-a-guid");

        var act = () => middleware.InvokeAsync(context);

        await act.Should().NotThrowAsync("a bad header value must not crash the middleware");
        context.Items.ContainsKey(TenantMiddleware.TenantIdItemKey)
            .Should().BeFalse("malformed GUID must be treated as absent");
    }

    // ── Test 6: Next delegate is always called ────────────────────────────────

    [Fact]
    public async Task InvokeAsync_always_calls_next_delegate()
    {
        var nextCalled = false;
        var middleware = BuildMiddleware(next: _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(new DefaultHttpContext());

        nextCalled.Should().BeTrue("middleware must always call _next regardless of tenant presence");
    }
}
