namespace EnterpriseLink.Shared.Domain.Enums;

/// <summary>
/// Roles assigned to users within a tenant.
/// Controls what actions a user can perform inside the platform.
/// </summary>
public enum UserRole
{
    /// <summary>Full access to tenant settings, users, and data.</summary>
    Admin = 1,

    /// <summary>Read-only access to transactions and reports.</summary>
    Auditor = 2,

    /// <summary>Can upload files and submit transactions.</summary>
    Vendor = 3,

    /// <summary>Day-to-day processing and reconciliation operations.</summary>
    Operator = 4
}
