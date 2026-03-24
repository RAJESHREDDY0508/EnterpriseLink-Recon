using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnterpriseLink.Shared.Infrastructure.Persistence;

/// <summary>
/// Provides a DbContext instance for EF Core design-time tools (migrations, scaffolding).
/// This factory is ONLY used by `dotnet ef` — never at runtime.
///
/// At design time there is no HTTP request, so no JWT claim exists.
/// NullTenantContext is used — global query filters will return empty results
/// but the schema is still generated correctly from the model.
///
/// Usage:
///   dotnet ef migrations add {MigrationName} \
///     --project src/Shared/EnterpriseLink.Shared.Infrastructure \
///     --output-dir Persistence/Migrations
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Design-time connection string — points to the local Docker SQL Server.
        // This value is ONLY used when running `dotnet ef` CLI commands.
        // Production connection strings are managed via environment variables / Key Vault.
        const string designTimeConnection =
            "Server=localhost,1433;" +
            "Database=EnterpriseLink_Core;" +
            "User Id=sa;" +
            "Password=Dev@Password123!;" +
            "TrustServerCertificate=True;" +
            "MultipleActiveResultSets=True";

        optionsBuilder.UseSqlServer(
            designTimeConnection,
            sql =>
            {
                sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable("__EFMigrationsHistory", "dbo");
                sql.EnableRetryOnFailure(maxRetryCount: 3);
            });

        return new AppDbContext(optionsBuilder.Options, NullTenantContext.Instance);
    }
}
