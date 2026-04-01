namespace EnterpriseLink.Shared.Infrastructure.MultiTenancy;

/// <summary>
/// Test-only ITenantContext that returns a hard-coded TenantId.
/// Allows unit tests to simulate specific tenants without an HTTP pipeline.
///
/// Usage in tests:
///   var tenantId = Guid.NewGuid();
///   var ctx = new AppDbContext(options, new FixedTenantContext(tenantId));
/// </summary>
public sealed class FixedTenantContext : ITenantContext
{
    public FixedTenantContext(Guid tenantId)
    {
        TenantId = tenantId;
    }

    /// <inheritdoc />
    public Guid TenantId { get; }

    /// <inheritdoc />
    public bool HasTenant => TenantId != Guid.Empty;
}
