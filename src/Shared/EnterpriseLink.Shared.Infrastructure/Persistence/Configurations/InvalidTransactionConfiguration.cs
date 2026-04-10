using EnterpriseLink.Shared.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="InvalidTransaction"/>.
///
/// <para><b>Purpose</b>
/// <see cref="InvalidTransaction"/> stores every CSV row that fails schema
/// validation, business-rule checks, or duplicate detection. Persisting rejected
/// rows separately allows operators to review, correct, and re-submit data without
/// touching the <c>Transactions</c> table.
/// </para>
///
/// <para><b>Tenant isolation</b>
/// The global query filter (tenant + soft-delete) is configured in
/// <c>AppDbContext.OnModelCreating</c> because it requires a closure over the
/// scoped <c>ITenantContext</c>. This configuration class handles schema only.
/// </para>
///
/// <para><b>Storage</b>
/// <c>RawData</c> and <c>ValidationErrors</c> use <c>nvarchar(max)</c> because
/// the number of CSV columns and the number of errors per row are unbounded.
/// </para>
/// </summary>
internal sealed class InvalidTransactionConfiguration
    : IEntityTypeConfiguration<InvalidTransaction>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InvalidTransaction> builder)
    {
        // ── Table + temporal history (Story 1) ────────────────────────────────
        builder.ToTable("InvalidTransactions", t => t.IsTemporal(tb =>
        {
            tb.UseHistoryTable("InvalidTransactionsHistory");
            tb.HasPeriodStart("SysStartTime").HasColumnName("SysStartTime");
            tb.HasPeriodEnd("SysEndTime").HasColumnName("SysEndTime");
        }));

        // ── Primary key ────────────────────────────────────────────────────────
        // Database-generated sequential GUID avoids page splits on high-volume inserts.
        builder.HasKey(t => t.InvalidTransactionId);
        builder.Property(t => t.InvalidTransactionId)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        // ── Properties ────────────────────────────────────────────────────────
        builder.Property(t => t.UploadId)
            .IsRequired();

        builder.Property(t => t.RowNumber)
            .IsRequired();

        // Entire ParsedRow.Fields dictionary serialised as JSON.
        builder.Property(t => t.RawData)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        // JSON array of formatted error strings.
        builder.Property(t => t.ValidationErrors)
            .IsRequired()
            .HasColumnType("nvarchar(max)");

        // "Schema" | "BusinessRule" | "Duplicate"
        builder.Property(t => t.FailureReason)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(t => t.IsDeleted)
            .HasDefaultValue(false);

        builder.Property(t => t.RowVersion)
            .IsRowVersion();

        // ── Indexes ────────────────────────────────────────────────────────────
        // TenantId alone — used by "all invalid rows for this tenant" queries.
        builder.HasIndex(t => t.TenantId)
            .HasDatabaseName("IX_InvalidTransactions_TenantId");

        // UploadId alone — used by "all errors for this upload" queries.
        builder.HasIndex(t => t.UploadId)
            .HasDatabaseName("IX_InvalidTransactions_UploadId");

        // (TenantId, UploadId) — used by tenant-scoped upload error queries.
        builder.HasIndex(t => new { t.TenantId, t.UploadId })
            .HasDatabaseName("IX_InvalidTransactions_TenantId_UploadId");

        // ── Relationships ──────────────────────────────────────────────────────
        // RESTRICT: invalid records must not be orphaned when a tenant is deleted.
        builder.HasOne(t => t.Tenant)
            .WithMany()
            .HasForeignKey(t => t.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
