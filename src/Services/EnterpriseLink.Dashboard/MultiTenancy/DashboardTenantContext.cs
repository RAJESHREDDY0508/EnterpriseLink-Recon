using EnterpriseLink.Shared.Infrastructure.MultiTenancy;

namespace EnterpriseLink.Dashboard.MultiTenancy;

/// <summary>
/// Cross-tenant <see cref="ITenantContext"/> implementation for the Dashboard service.
///
/// <para>
/// The Dashboard is an operational admin tool intended to provide visibility across
/// all tenants simultaneously. Setting <see cref="HasTenant"/> to <c>false</c>
/// causes the <c>AppDbContext</c> global query filters to evaluate the "no tenant"
/// branch:
/// </para>
/// <list type="bullet">
///   <item>
///     <description>
///       <c>AuditLog</c>: filter passes all rows (<c>!HasTenant</c> is true).
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>Transaction</c>, <c>InvalidTransaction</c>: services explicitly call
///       <c>.IgnoreQueryFilters()</c> before applying optional per-request tenant
///       parameters supplied by the query objects.
///     </description>
///   </item>
///   <item>
///     <description>
///       <c>ProcessedUpload</c>: only a soft-delete filter exists; services bypass
///       it via <c>.IgnoreQueryFilters()</c> to include all lifecycle states.
///     </description>
///   </item>
/// </list>
///
/// <para>
/// This design keeps the <c>AppDbContext</c> construction path identical to other
/// services while giving the Dashboard intentional cross-tenant read access.
/// </para>
/// </summary>
public sealed class DashboardTenantContext : ITenantContext
{
    /// <inheritdoc />
    /// <remarks>
    /// Always <c>Guid.Empty</c>. The Dashboard does not operate in a single-tenant
    /// scope; services use <c>IgnoreQueryFilters()</c> and filter by explicit
    /// <c>TenantId</c> query parameters when tenant isolation is required.
    /// </remarks>
    public Guid TenantId => Guid.Empty;

    /// <inheritdoc />
    /// <remarks>
    /// Always <c>false</c>. This signals the <c>AppDbContext</c> query filters to
    /// use the cross-tenant code path, and suppresses <c>ApplyTenantId</c> on any
    /// write (the Dashboard is read-only by design).
    /// </remarks>
    public bool HasTenant => false;
}
