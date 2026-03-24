namespace EnterpriseLink.Shared.Infrastructure.MultiTenancy;

/// <summary>
/// Provides the current tenant identity to the infrastructure layer.
/// Abstracted so it can be resolved from HTTP JWT claims in production,
/// injected directly in tests, or set to Null for migrations.
///
/// The AppDbContext depends on this interface — NOT on HttpContext directly —
/// keeping the persistence layer decoupled from the web layer.
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// The TenantId of the currently authenticated tenant.
    /// Returns Guid.Empty when no tenant is resolved (design-time / system tasks).
    /// </summary>
    Guid TenantId { get; }

    /// <summary>True when a valid tenant is resolved. False for system/design-time contexts.</summary>
    bool HasTenant { get; }
}
