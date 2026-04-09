using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Shared.Infrastructure.Persistence;

/// <summary>
/// Central EF Core DbContext for the EnterpriseLink Recon platform.
///
/// TENANT ISOLATION STRATEGY:
/// ─────────────────────────────────────────────────────────────────────────────
/// 1. GLOBAL QUERY FILTERS
///    Every query against Users and Transactions is automatically scoped to the
///    current tenant via EF Core global query filters. No service-layer WHERE
///    clause is required — isolation is enforced at the ORM level.
///
/// 2. AUTO TENANT INJECTION ON INSERT
///    SaveChangesAsync intercepts all Added ITenantScoped entities and sets
///    TenantId from ITenantContext. Services NEVER set TenantId manually.
///
/// 3. SOFT DELETE
///    Deleted entities have IsDeleted = true. The global filter excludes them
///    automatically. Physical deletes are blocked at the application layer.
///
/// 4. AUDIT TRAIL
///    CreatedAt / UpdatedAt are managed exclusively by this context.
///    Services never set these directly.
///
/// 5. OPTIMISTIC CONCURRENCY
///    RowVersion (SQL Server ROWVERSION) is configured on every entity to
///    prevent lost-update race conditions.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    // ── DbSets ────────────────────────────────────────────────────────────────

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    /// <summary>
    /// Idempotency tracking table.
    /// One row per <c>FileUploadedEvent.UploadId</c>; used to prevent duplicate processing.
    /// </summary>
    public DbSet<ProcessedUpload> ProcessedUploads => Set<ProcessedUpload>();

    // ── EF Core pipeline configuration ───────────────────────────────────────

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Register the RLS interceptor for this DbContext instance.
        // It sets SESSION_CONTEXT(N'TenantId') on every new connection so that
        // the SQL Server fn_TenantAccessPredicate can enforce the security policy.
        optionsBuilder.AddInterceptors(new TenantSessionContextInterceptor(_tenantContext));
    }

    // ── Model configuration ───────────────────────────────────────────────────

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all IEntityTypeConfiguration<T> classes in this assembly automatically
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // ── Global query filters ───────────────────────────────────────────────
        //
        // CRITICAL: These filters run on EVERY query unless explicitly bypassed
        // with .IgnoreQueryFilters(). They enforce both tenant isolation AND
        // soft-delete transparency simultaneously.
        //
        // Filter evaluation happens at query execution time (not model build time)
        // because _tenantContext is a scoped service resolved per HTTP request.

        // Tenants table: only soft-delete filter (Tenants are not tenant-scoped themselves)
        modelBuilder.Entity<Tenant>()
            .HasQueryFilter(t => !t.IsDeleted);

        // Users: tenant isolation + soft delete
        modelBuilder.Entity<User>()
            .HasQueryFilter(u => u.TenantId == _tenantContext.TenantId && !u.IsDeleted);

        // Transactions: tenant isolation + soft delete
        modelBuilder.Entity<Transaction>()
            .HasQueryFilter(t => t.TenantId == _tenantContext.TenantId && !t.IsDeleted);
    }

    // ── Save interception ─────────────────────────────────────────────────────

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        ApplyTenantId();
        InterceptSoftDeletes();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditFields();
        ApplyTenantId();
        InterceptSoftDeletes();
        return base.SaveChanges();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Automatically stamps CreatedAt / UpdatedAt on every AuditableEntity.
    /// Services never need to set these fields directly.
    /// </summary>
    private void ApplyAuditFields()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    // Never allow CreatedAt to be changed after insert
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Automatically injects TenantId on new ITenantScoped entities.
    /// This prevents tenant spoofing — the client never controls TenantId.
    /// </summary>
    private void ApplyTenantId()
    {
        if (!_tenantContext.HasTenant)
            return;

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>()
                     .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = _tenantContext.TenantId;
        }
    }

    /// <summary>
    /// Converts physical DELETE operations into soft deletes.
    /// No data is ever permanently removed through normal application flow.
    /// Hard deletes require direct database access (admin / compliance).
    /// </summary>
    private void InterceptSoftDeletes()
    {
        var deletedEntries = ChangeTracker.Entries<AuditableEntity>()
            .Where(e => e.State == EntityState.Deleted)
            .ToList();

        foreach (var entry in deletedEntries)
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = DateTimeOffset.UtcNow;
        }
    }
}
