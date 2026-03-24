namespace EnterpriseLink.Shared.Domain.Entities;

/// <summary>
/// Base class for all entities.
/// Provides audit trail, soft delete, and optimistic concurrency out-of-the-box.
/// Every table that extends this gets: CreatedAt, UpdatedAt, IsDeleted, DeletedAt, RowVersion.
/// </summary>
public abstract class AuditableEntity
{
    /// <summary>UTC timestamp when the record was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the last modification.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Soft delete flag — records are never physically removed.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>UTC timestamp when the record was soft-deleted. Null if active.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token. SQL Server maps this to ROWVERSION / TIMESTAMP.
    /// Prevents lost-update issues in high-concurrency scenarios.
    /// </summary>
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
