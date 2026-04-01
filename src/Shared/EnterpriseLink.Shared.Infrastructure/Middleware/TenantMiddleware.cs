using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace EnterpriseLink.Shared.Infrastructure.Middleware;

/// <summary>
/// Resolves the current tenant early in the pipeline and stores the TenantId
/// in <see cref="HttpContext.Items"/> so that downstream services and
/// <see cref="EnterpriseLink.Shared.Infrastructure.MultiTenancy.HttpTenantContext"/>
/// can read it without re-parsing on every access.
///
/// Resolution order (first match wins):
///   1. JWT claim  "tenant_id"   — standard flow through Auth Service / API Gateway
///   2. Header     "X-Tenant-Id" — internal service-to-service calls without a full JWT
///
/// If neither source yields a valid GUID the request continues without a tenant
/// (TenantId stays absent from Items). Endpoints that require a tenant should
/// be protected by authorization policies that will reject the unauthenticated
/// request before it reaches application code.
/// </summary>
public sealed class TenantMiddleware
{
    public const string TenantIdClaim = "tenant_id";
    public const string TenantIdHeader = "X-Tenant-Id";
    public const string TenantIdItemKey = "TenantId";

    private readonly RequestDelegate _next;
    private readonly ILogger<TenantMiddleware> _logger;

    public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = ResolveFromClaim(context) ?? ResolveFromHeader(context);

        if (tenantId.HasValue)
        {
            context.Items[TenantIdItemKey] = tenantId.Value;
            _logger.LogDebug(
                "Tenant resolved for {Method} {Path}: {TenantId}",
                context.Request.Method,
                context.Request.Path,
                tenantId.Value);
        }
        else
        {
            _logger.LogDebug(
                "No tenant resolved for {Method} {Path}",
                context.Request.Method,
                context.Request.Path);
        }

        await _next(context);
    }

    // ── Resolution strategies ─────────────────────────────────────────────────

    private static Guid? ResolveFromClaim(HttpContext context)
    {
        var claim = context.User.FindFirst(TenantIdClaim);
        return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }

    private static Guid? ResolveFromHeader(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(TenantIdHeader, out var value))
            return null;

        return Guid.TryParse(value, out var id) ? id : null;
    }
}
