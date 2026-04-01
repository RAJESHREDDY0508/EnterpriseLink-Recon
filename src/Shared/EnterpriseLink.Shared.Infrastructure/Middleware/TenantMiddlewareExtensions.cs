using Microsoft.AspNetCore.Builder;

namespace EnterpriseLink.Shared.Infrastructure.Middleware;

/// <summary>
/// Extension methods to register <see cref="TenantMiddleware"/> in the
/// ASP.NET Core pipeline.
///
/// Usage in each service's Program.cs:
///   app.UseTenantResolution();
///
/// Must be placed AFTER app.UseAuthentication() so the JWT principal is
/// already populated when the middleware runs claim-based resolution.
/// </summary>
public static class TenantMiddlewareExtensions
{
    public static IApplicationBuilder UseTenantResolution(this IApplicationBuilder app)
        => app.UseMiddleware<TenantMiddleware>();
}
