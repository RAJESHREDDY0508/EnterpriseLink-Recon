using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent API configuration for the Transactions table.
/// This is the highest-volume table in the system (10M+ rows/day target).
/// Index strategy is designed to avoid table scans on the most common
/// query patterns: by tenant, by status, by date range, and combinations.
/// </summary>
public sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        // ── Table ──────────────────────────────────────────────────────────────
        builder.ToTable("Transactions");

        // ── Primary key ────────────────────────────────────────────────────────
        builder.HasKey(t => t.TransactionId);

        builder.Property(t => t.TransactionId)
            .HasDefaultValueSql("NEWSEQUENTIALID()")   // Sequential avoids page splits on clustered index
            .ValueGeneratedOnAdd();

        // ── Tenant FK (critical for isolation) ────────────────────────────────
        builder.Property(t => t.TenantId)
            .IsRequired();

        builder.Property(t => t.UserId)
            .IsRequired(false);

        // ── Core fields ────────────────────────────────────────────────────────
        builder.Property(t => t.Amount)
            .IsRequired()
            .HasColumnType("decimal(18,4)");    // 4 decimal places for financial precision

        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);
            // Default (Pending) is set at the entity level to avoid EF sentinel ambiguity

        builder.Property(t => t.ExternalReferenceId)
            .IsRequired(false)
            .HasMaxLength(256);

        builder.Property(t => t.Description)
            .IsRequired(false)
            .HasMaxLength(500);

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
            .IsRowVersion()
            .IsConcurrencyToken();

        // ── Indexes ────────────────────────────────────────────────────────────
        // Single column — fast lookup per tenant
        builder.HasIndex(t => t.TenantId)
            .HasDatabaseName("IX_Transactions_TenantId");

        // Single column — global status monitoring (admin dashboard)
        builder.HasIndex(t => t.Status)
            .HasDatabaseName("IX_Transactions_Status");

        // Single column — time-range queries on audit/reporting
        builder.HasIndex(t => t.CreatedAt)
            .HasDatabaseName("IX_Transactions_CreatedAt");

        // Composite — most common dashboard query: "show me Pending for TenantX"
        builder.HasIndex(t => new { t.TenantId, t.Status })
            .HasDatabaseName("IX_Transactions_TenantId_Status");

        // Composite — date range within a tenant (reconciliation reports)
        builder.HasIndex(t => new { t.TenantId, t.CreatedAt })
            .HasDatabaseName("IX_Transactions_TenantId_CreatedAt");

        // Partial-style: ExternalReferenceId for reconciliation deduplication lookups
        builder.HasIndex(t => new { t.TenantId, t.ExternalReferenceId })
            .HasDatabaseName("IX_Transactions_TenantId_ExternalRef")
            .HasFilter("[ExternalReferenceId] IS NOT NULL");  // Filtered index — only index non-null rows
    }
}
