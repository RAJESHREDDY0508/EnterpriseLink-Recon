namespace EnterpriseLink.Shared.Domain.MultiTenancy;

/// <summary>
/// Marker interface for all entities that belong to a specific tenant.
/// The AppDbContext uses this to automatically apply global query filters
/// and inject TenantId on insert — no manual assignment needed in services.
/// </summary>
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
