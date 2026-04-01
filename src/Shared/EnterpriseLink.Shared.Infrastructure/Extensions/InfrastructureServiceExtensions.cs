using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EnterpriseLink.Shared.Infrastructure.Extensions;

/// <summary>
/// DI registration helper for the shared infrastructure layer.
/// Every microservice that uses AppDbContext calls this one method —
/// tenant isolation, connection string, and EF Core pipeline are all wired here.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers the full tenant-aware EF Core stack:
    ///   • IHttpContextAccessor  (reads HTTP request for claim resolution)
    ///   • ITenantContext        (resolves TenantId from JWT "tenant_id" claim)
    ///   • AppDbContext          (scoped DbContext with global filters + RLS interceptor)
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">SQL Server connection string from service configuration.</param>
    public static IServiceCollection AddTenantInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // Required for HttpTenantContext to read the current request's claims.
        services.AddHttpContextAccessor();

        // Resolves TenantId from the "tenant_id" JWT claim on every request.
        // Scoped lifetime matches the DbContext — one resolution per HTTP request.
        services.AddScoped<ITenantContext, HttpTenantContext>();

        // Register AppDbContext scoped to the HTTP request.
        // The global query filters inside the context capture the scoped
        // ITenantContext, ensuring every query is automatically tenant-scoped.
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(
                    typeof(AppDbContext).Assembly.FullName)));

        return services;
    }
}
