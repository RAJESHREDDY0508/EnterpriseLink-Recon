using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent API configuration for the Tenants table.
/// Tenants is the root of the multi-tenant hierarchy — every other record
/// traces back to a row in this table.
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        // ── Table ──────────────────────────────────────────────────────────────
        builder.ToTable("Tenants");

        // ── Primary key ────────────────────────────────────────────────────────
        builder.HasKey(t => t.TenantId);

        builder.Property(t => t.TenantId)
            .HasDefaultValueSql("NEWSEQUENTIALID()")  // Sequential GUIDs reduce index fragmentation
            .ValueGeneratedOnAdd();

        // ── Core fields ────────────────────────────────────────────────────────
        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.IndustryType)
            .IsRequired()
            .HasConversion<string>()      // Store as "Financial", "Healthcare" — human-readable
            .HasMaxLength(50);
            // Default is set at the entity level, not the DB level, to avoid EF sentinel ambiguity

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // ── Audit fields ───────────────────────────────────────────────────────
        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired();

        builder.Property(t => t.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(t => t.DeletedAt)
            .IsRequired(false);

        // ── Optimistic concurrency ─────────────────────────────────────────────
        builder.Property(t => t.RowVersion)
            .IsRowVersion()           // Maps to SQL Server ROWVERSION / TIMESTAMP
            .IsConcurrencyToken();

        // ── Indexes ────────────────────────────────────────────────────────────
        // Unique constraint on Name — prevents duplicate tenant registration
        builder.HasIndex(t => t.Name)
            .IsUnique()
            .HasDatabaseName("UX_Tenants_Name");

        // ── Relationships ──────────────────────────────────────────────────────
        // Restrict delete: cannot delete a Tenant while Users exist
        builder.HasMany(t => t.Users)
            .WithOne(u => u.Tenant)
            .HasForeignKey(u => u.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict delete: cannot delete a Tenant while Transactions exist
        builder.HasMany(t => t.Transactions)
            .WithOne(tr => tr.Tenant)
            .HasForeignKey(tr => tr.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
