using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace EnterpriseLink.Shared.Infrastructure.MultiTenancy;

/// <summary>
/// Production implementation of ITenantContext.
/// Resolves the current TenantId from the JWT "tenant_id" claim
/// that is issued by the Auth Service and validated at the API Gateway.
///
/// Registration:
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
            var claim = _httpContextAccessor.HttpContext?
                .User
                .FindFirst("tenant_id");

            return claim is not null && Guid.TryParse(claim.Value, out var tenantId)
                ? tenantId
                : Guid.Empty;
        }
    }

    /// <inheritdoc />
    public bool HasTenant => TenantId != Guid.Empty;
}
