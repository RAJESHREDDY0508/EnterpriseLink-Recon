namespace EnterpriseLink.Shared.Infrastructure.MultiTenancy;

/// <summary>
/// No-op ITenantContext used in two scenarios:
/// 1. EF Core design-time (DesignTimeDbContextFactory) — migrations run outside
///    an HTTP request so no JWT claim is present.
/// 2. Unit tests that need a DbContext without a real HTTP pipeline.
///
/// When this is active, global query filters return no tenant-scoped rows
/// because TenantId is Guid.Empty — which is the safe/fail-closed behaviour.
///
/// For system-level admin operations that intentionally bypass tenant filters,
/// use DbContext.Set&lt;T&gt;().IgnoreQueryFilters().
/// </summary>
public sealed class NullTenantContext : ITenantContext
{
    public static readonly NullTenantContext Instance = new();

    /// <inheritdoc />
    public Guid TenantId => Guid.Empty;

    /// <inheritdoc />
    public bool HasTenant => false;
}
