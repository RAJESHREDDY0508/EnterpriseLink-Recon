using System.Text.Json;
using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

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

    /// <summary>
    /// Stores every CSV row rejected by schema validation, business-rule checks,
    /// or duplicate detection. Scoped per-tenant via the global query filter.
    /// </summary>
    public DbSet<InvalidTransaction> InvalidTransactions => Set<InvalidTransaction>();

    /// <summary>
    /// Append-only audit trail. One row per entity change (insert / update / soft-delete),
    /// written atomically in the same transaction as the change itself.
    /// </summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

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

        // InvalidTransactions: tenant isolation + soft delete
        modelBuilder.Entity<InvalidTransaction>()
            .HasQueryFilter(t => t.TenantId == _tenantContext.TenantId && !t.IsDeleted);

        // AuditLogs: tenant-scoped reads (cross-tenant admin queries use IgnoreQueryFilters).
        // TenantId is nullable on AuditLog — rows for non-tenant-scoped entities (e.g. Tenant
        // itself) have TenantId = null and are only visible to admin queries.
        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(a => !_tenantContext.HasTenant ||
                                 a.TenantId == null ||
                                 a.TenantId == _tenantContext.TenantId);
    }

    // ── Save interception ─────────────────────────────────────────────────────

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        ApplyTenantId();
        InterceptSoftDeletes();

        // Capture before/after state AFTER soft-delete conversion so the audit
        // log reflects the exact rows that will be written to the database.
        var auditEntries = BuildAuditEntries();
        Set<AuditLog>().AddRange(auditEntries);

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ApplyAuditFields();
        ApplyTenantId();
        InterceptSoftDeletes();

        var auditEntries = BuildAuditEntries();
        Set<AuditLog>().AddRange(auditEntries);

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

        var tenantId = _tenantContext.TenantId;

        if (tenantId == Guid.Empty)
            throw new InvalidOperationException(
                "A tenant-scoped save was attempted but TenantId is Guid.Empty. " +
                "Ensure the tenant context is initialised before any write operation.");

        foreach (var entry in ChangeTracker.Entries<ITenantScoped>()
                     .Where(e => e.State == EntityState.Added))
        {
            entry.Entity.TenantId = tenantId;
        }
    }

    /// <summary>
    /// Captures before/after state for every <see cref="AuditableEntity"/> change
    /// currently tracked by the change tracker. Called after
    /// <see cref="InterceptSoftDeletes"/> so that soft-delete conversions
    /// (Deleted → Modified+IsDeleted=true) are reflected in the audit entries.
    /// </summary>
    private IEnumerable<AuditLog> BuildAuditEntries()
    {
        var now = DateTimeOffset.UtcNow;
        var tenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : (Guid?)null;

        var entries = ChangeTracker
            .Entries<AuditableEntity>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();   // Materialise before adding new AuditLog entries to the tracker

        foreach (var entry in entries)
        {
            string? oldValues = null;
            string? newValues = null;

            if (entry.State is EntityState.Modified or EntityState.Deleted)
                oldValues = SerialiseValues(entry.OriginalValues);

            if (entry.State is EntityState.Added or EntityState.Modified)
                newValues = SerialiseValues(entry.CurrentValues);

            yield return new AuditLog
            {
                EntityType = entry.Entity.GetType().Name,
                EntityId = BuildEntityId(entry),
                TenantId = tenantId,
                Action = entry.State.ToString(),
                OldValues = oldValues,
                NewValues = newValues,
                OccurredAt = now,
            };
        }
    }

    private static string BuildEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null) return string.Empty;

        return string.Join(",", key.Properties
            .Select(p => entry.Property(p.Name).CurrentValue?.ToString() ?? "null"));
    }

    private static string SerialiseValues(PropertyValues values)
    {
        var dict = values.Properties
            .Where(p => !p.IsShadowProperty())   // Exclude SysStartTime/SysEndTime etc.
            .ToDictionary(p => p.Name, p => values[p]?.ToString());

        return JsonSerializer.Serialize(dict);
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
