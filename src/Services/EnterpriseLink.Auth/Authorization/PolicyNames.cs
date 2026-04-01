namespace EnterpriseLink.Auth.Authorization;

/// <summary>
/// Named authorization policy identifiers registered in <c>Program.cs</c>
/// via <c>AddAuthorization(options => { ... })</c>.
///
/// <para>
/// Using named policies rather than raw <c>[Authorize(Roles = "...")]</c> strings
/// centralises access-control rules: composite role sets (e.g. Admin OR Auditor)
/// are defined once and referenced by name everywhere they are applied.
/// </para>
///
/// <para><b>Policy → Role Matrix</b></para>
/// <list type="table">
///   <listheader>
///     <term>Policy</term>
///     <description>Allowed roles</description>
///   </listheader>
///   <item>
///     <term><see cref="RequireAdmin"/></term>
///     <description>Admin</description>
///   </item>
///   <item>
///     <term><see cref="RequireAuditor"/></term>
///     <description>Auditor</description>
///   </item>
///   <item>
///     <term><see cref="RequireVendor"/></term>
///     <description>Vendor</description>
///   </item>
///   <item>
///     <term><see cref="RequireOperator"/></term>
///     <description>Operator</description>
///   </item>
///   <item>
///     <term><see cref="RequireAuditAccess"/></term>
///     <description>Admin, Auditor — read-only reporting and compliance views</description>
///   </item>
///   <item>
///     <term><see cref="RequireOperationAccess"/></term>
///     <description>Admin, Operator, Vendor — write operations and transaction processing</description>
///   </item>
/// </list>
/// </summary>
public static class PolicyNames
{
    /// <summary>
    /// Grants access to tenant administration endpoints (user management, settings).
    /// Allowed roles: <see cref="Roles.Admin"/>.
    /// </summary>
    public const string RequireAdmin = "RequireAdmin";

    /// <summary>
    /// Grants access to compliance and read-only audit endpoints.
    /// Allowed roles: <see cref="Roles.Auditor"/>.
    /// </summary>
    public const string RequireAuditor = "RequireAuditor";

    /// <summary>
    /// Grants access to file upload and transaction submission endpoints.
    /// Allowed roles: <see cref="Roles.Vendor"/>.
    /// </summary>
    public const string RequireVendor = "RequireVendor";

    /// <summary>
    /// Grants access to day-to-day processing and reconciliation endpoints.
    /// Allowed roles: <see cref="Roles.Operator"/>.
    /// </summary>
    public const string RequireOperator = "RequireOperator";

    /// <summary>
    /// Grants access to reporting and audit views.
    /// Allowed roles: <see cref="Roles.Admin"/>, <see cref="Roles.Auditor"/>.
    /// </summary>
    public const string RequireAuditAccess = "RequireAuditAccess";

    /// <summary>
    /// Grants access to transaction processing and write operations.
    /// Allowed roles: <see cref="Roles.Admin"/>, <see cref="Roles.Operator"/>, <see cref="Roles.Vendor"/>.
    /// </summary>
    public const string RequireOperationAccess = "RequireOperationAccess";
}
