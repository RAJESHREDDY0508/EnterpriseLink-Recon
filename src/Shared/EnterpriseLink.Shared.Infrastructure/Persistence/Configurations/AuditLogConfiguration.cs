using EnterpriseLink.Shared.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnterpriseLink.Shared.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity type configuration for <see cref="AuditLog"/>.
///
/// <para><b>Immutability</b>
/// <see cref="AuditLog"/> rows are never updated or deleted by the application.
/// The table therefore has no <c>RowVersion</c>, <c>IsDeleted</c>, or
/// <c>UpdatedAt</c> columns — only the fields needed to reconstruct the full
/// change history.
/// </para>
///
/// <para><b>No temporal table</b>
/// <see cref="AuditLog"/> itself is not system-versioned. Auditing the audit log
/// would be circular and serves no compliance purpose.
/// </para>
///
/// <para><b>Indexing strategy</b>
/// Three independent indexes cover the three dominant query patterns:
/// <list type="bullet">
///   <item><description>(<c>EntityType</c>, <c>EntityId</c>) — point lookup: all changes to entity X.</description></item>
///   <item><description><c>TenantId</c> — tenant-scoped compliance report.</description></item>
///   <item><description><c>OccurredAt</c> — time-range query: what changed in period P?</description></item>
/// </list>
/// </para>
/// </summary>
internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        // ── Primary key ────────────────────────────────────────────────────────
        builder.HasKey(a => a.AuditLogId);
        builder.Property(a => a.AuditLogId)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("NEWSEQUENTIALID()");

        // ── Properties ────────────────────────────────────────────────────────
        builder.Property(a => a.EntityType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.TenantId)
            .IsRequired(false);

        builder.Property(a => a.Action)
            .IsRequired()
            .HasMaxLength(50);

        // JSON before/after snapshots — unbounded length.
        builder.Property(a => a.OldValues)
            .IsRequired(false)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.NewValues)
            .IsRequired(false)
            .HasColumnType("nvarchar(max)");

        builder.Property(a => a.OccurredAt)
            .IsRequired();

        // ── Indexes ────────────────────────────────────────────────────────────
        // Point lookup: "show all changes to Transaction {id}"
        builder.HasIndex(a => new { a.EntityType, a.EntityId })
            .HasDatabaseName("IX_AuditLogs_EntityType_EntityId");

        // Tenant-scoped compliance report
        builder.HasIndex(a => a.TenantId)
            .HasDatabaseName("IX_AuditLogs_TenantId");

        // Time-range compliance query
        builder.HasIndex(a => a.OccurredAt)
            .HasDatabaseName("IX_AuditLogs_OccurredAt");
    }
}
