using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent API configuration for the Users table.
/// Email uniqueness is scoped per-tenant — the same email address can exist
/// under different tenants without conflict.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        // ── Table ──────────────────────────────────────────────────────────────
        builder.ToTable("Users");

        // ── Primary key ────────────────────────────────────────────────────────
        builder.HasKey(u => u.UserId);

        builder.Property(u => u.UserId)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedOnAdd();

        // ── Tenant FK (critical for isolation) ────────────────────────────────
        builder.Property(u => u.TenantId)
            .IsRequired();

        // ── Core fields ────────────────────────────────────────────────────────
        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(u => u.PasswordHash)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(u => u.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
            // Default is set at the entity level, not the DB level, to avoid EF sentinel ambiguity

        builder.Property(u => u.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // ── Audit fields ───────────────────────────────────────────────────────
        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired();

        builder.Property(u => u.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.DeletedAt)
            .IsRequired(false);

        // ── Optimistic concurrency ─────────────────────────────────────────────
        builder.Property(u => u.RowVersion)
            .IsRowVersion()
            .IsConcurrencyToken();

        // ── Indexes ────────────────────────────────────────────────────────────
        // Composite unique: one email per tenant (not globally unique)
        builder.HasIndex(u => new { u.TenantId, u.Email })
            .IsUnique()
            .HasDatabaseName("UX_Users_TenantId_Email");

        // Cover index to accelerate all tenant-scoped user queries
        builder.HasIndex(u => u.TenantId)
            .HasDatabaseName("IX_Users_TenantId");

        // ── Relationships ──────────────────────────────────────────────────────
        builder.HasMany(u => u.Transactions)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
