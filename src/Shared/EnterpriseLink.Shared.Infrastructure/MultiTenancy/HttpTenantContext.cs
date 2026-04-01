using EnterpriseLink.Shared.Infrastructure.Middleware;
using Microsoft.AspNetCore.Http;

namespace EnterpriseLink.Shared.Infrastructure.MultiTenancy;

/// <summary>
/// Production implementation of ITenantContext.
///
/// Resolution order:
///   1. HttpContext.Items["TenantId"] — pre-parsed by TenantMiddleware (fastest path)
///   2. JWT claim "tenant_id"         — fallback when middleware is not in the pipeline
///
/// Registration (handled by InfrastructureServiceExtensions.AddTenantInfrastructure):
///   builder.Services.AddHttpContextAccessor();
///   builder.Services.AddScoped&lt;ITenantContext, HttpTenantContext&gt;();
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpTenantContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid TenantId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext is null)
                return Guid.Empty;

            // Fast path: TenantMiddleware already parsed and stored the value.
            if (httpContext.Items.TryGetValue(TenantMiddleware.TenantIdItemKey, out var cached)
                && cached is Guid cachedId)
            {
                return cachedId;
            }

            // Fallback: read directly from the JWT claim.
            var claim = httpContext.User.FindFirst(TenantMiddleware.TenantIdClaim);
            return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : Guid.Empty;
        }
    }

    /// <inheritdoc />
    public bool HasTenant => TenantId != Guid.Empty;
}
