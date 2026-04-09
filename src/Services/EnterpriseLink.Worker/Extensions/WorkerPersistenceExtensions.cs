using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Batch;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.Idempotency;
using EnterpriseLink.Worker.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Worker.Extensions;

/// <summary>
/// Extension methods that register database persistence, batch insert, and
/// idempotency services for the Worker service.
/// </summary>
public static class WorkerPersistenceExtensions
{
    /// <summary>
    /// Registers the following services:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <see cref="WorkerTenantContext"/> as both its concrete type and as
    ///       <see cref="ITenantContext"/> (scoped — set per message by the consumer).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="AppDbContext"/> with SQL Server transport and automatic retry
    ///       on transient failures (max 3 retries, 5-second delay).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="BatchInsertOptions"/> bound from <c>BatchInsert</c> section
    ///       with startup validation.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IBatchRowInserter"/> → <see cref="TransactionBatchInserter"/> (scoped).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IUploadIdempotencyGuard"/> → <see cref="EfUploadIdempotencyGuard"/> (scoped).
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <param name="configuration">Application configuration (used for connection string and batch options).</param>
    /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown at startup if the <c>DefaultConnection</c> connection string is absent.
    /// </exception>
    public static IServiceCollection AddWorkerPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ── Tenant context ─────────────────────────────────────────────────────
        // Scoped: each MassTransit message scope creates its own mutable instance.
        // The consumer sets TenantId before any DB operation, so EF Core query
        // filters and ApplyTenantId both see the correct tenant for the message.
        services.AddScoped<WorkerTenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>());

        // ── Database ───────────────────────────────────────────────────────────
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is required for Worker persistence. " +
                "Set it in appsettings.json or via the environment variable " +
                "ConnectionStrings__DefaultConnection.");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(
                connectionString,
                sql => sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null)));

        // ── Batch insert options ───────────────────────────────────────────────
        services.AddOptions<BatchInsertOptions>()
            .Bind(configuration.GetSection(BatchInsertOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Batch inserter + idempotency guard ────────────────────────────────
        // Scoped: each message scope gets a fresh instance tied to the same
        // AppDbContext and WorkerTenantContext within that scope.
        services.AddScoped<IBatchRowInserter, TransactionBatchInserter>();
        services.AddScoped<IUploadIdempotencyGuard, EfUploadIdempotencyGuard>();

        return services;
    }
}
