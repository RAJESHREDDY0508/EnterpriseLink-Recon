namespace EnterpriseLink.Shared.Domain.Enums;

/// <summary>
/// Classifies the industry vertical a tenant operates in.
/// Drives compliance rules (e.g., HIPAA for Healthcare, PCI for Financial).
/// </summary>
public enum IndustryType
{
    Financial = 1,
    Healthcare = 2,
    Insurance = 3,
    Government = 4,
    Retail = 5,
    Other = 99
}
