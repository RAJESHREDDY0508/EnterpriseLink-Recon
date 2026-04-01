namespace EnterpriseLink.Auth.Authorization;

/// <summary>
/// String constants for the EnterpriseLink application roles as they appear in
/// Entra ID App Registration role definitions and in <c>ClaimTypes.Role</c> claims
/// after <c>EnterpriseLinkClaimsTransformation</c> has run.
///
/// <para>
/// These values must match the <b>role display names</b> in the Azure portal
/// (App Registration → App roles) exactly, because Entra ID emits them verbatim
/// as the value of the <c>roles</c> claim in the JWT.
/// </para>
///
/// <para><b>Role Responsibilities</b></para>
/// <list type="table">
///   <listheader>
///     <term>Role</term>
///     <description>Permitted operations</description>
///   </listheader>
///   <item>
///     <term><see cref="Admin"/></term>
///     <description>Full control: manage tenant settings, users, and all data.</description>
///   </item>
///   <item>
///     <term><see cref="Auditor"/></term>
///     <description>Read-only access to transactions and compliance reports.</description>
///   </item>
///   <item>
///     <term><see cref="Vendor"/></term>
///     <description>Upload files and submit transactions on behalf of the tenant.</description>
///   </item>
///   <item>
///     <term><see cref="Operator"/></term>
///     <description>Day-to-day processing, reconciliation, and operational views.</description>
///   </item>
/// </list>
///
/// <para><b>Usage</b></para>
/// <code>
/// [Authorize(Policy = PolicyNames.RequireAdmin)]
/// [Authorize(Roles = Roles.Admin)]
/// </code>
/// Prefer named policies (<see cref="PolicyNames"/>) over raw role strings so that
/// composite access rules (e.g. Admin OR Auditor) are defined in one place.
/// </summary>
public static class Roles
{
    /// <summary>Full administrative access to the tenant.</summary>
    public const string Admin = "Admin";

    /// <summary>Read-only access to transactions and compliance data.</summary>
    public const string Auditor = "Auditor";

    /// <summary>Can upload files and submit transactions.</summary>
    public const string Vendor = "Vendor";

    /// <summary>Day-to-day processing and reconciliation operations.</summary>
    public const string Operator = "Operator";
}
