using EnterpriseLink.Dashboard.MultiTenancy;
using EnterpriseLink.Dashboard.Services;
using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Dashboard.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods that wire up all Dashboard
/// service registrations in a single call from <c>Program.cs</c>.
///
/// <para>
/// Keeping registrations in an extension method rather than directly in
/// <c>Program.cs</c> mirrors the pattern used by other services in this solution
/// (e.g. <c>WorkerBatchExtensions</c>, <c>WorkerValidationExtensions</c>) and keeps
/// <c>Program.cs</c> readable regardless of how many services are registered.
/// </para>
/// </summary>
public static class DashboardServiceExtensions
{
    /// <summary>
    /// Registers the EF Core <see cref="AppDbContext"/>, the cross-tenant
    /// <see cref="DashboardTenantContext"/>, and all Dashboard query services.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configuration">Application configuration (provides the connection string).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddDashboardServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Tenant context ────────────────────────────────────────────────────
        // DashboardTenantContext has HasTenant=false, giving cross-tenant read access.
        // Scoped lifetime matches AppDbContext so the same instance is used within a request.
        services.AddScoped<ITenantContext, DashboardTenantContext>();

        // ── EF Core ───────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is not configured. " +
                "Add it to appsettings.json or the environment.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                connectionString,
                sqlOptions => sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null)));

        // ── Dashboard query services (scoped — one per request) ───────────────
        services.AddScoped<IBatchMonitorService, BatchMonitorService>();
        services.AddScoped<IErrorViewerService, ErrorViewerService>();
        services.AddScoped<IAuditLogService, AuditLogService>();

        return services;
    }
}
