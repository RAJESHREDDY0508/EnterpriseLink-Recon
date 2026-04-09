namespace EnterpriseLink.Shared.Domain.Enums;

/// <summary>
/// Identifies which stage of the validation pipeline rejected a row.
///
/// <para><b>Pipeline stages</b>
/// Rows pass through three sequential gates — schema, business rules, and duplicate
/// detection. This enum records which gate first rejected the row and is persisted
/// on the <c>InvalidTransaction</c> record so operators can triage failures quickly.
/// </para>
/// </summary>
public enum ValidationFailureReason
{
    /// <summary>Row is valid; no failure occurred.</summary>
    None = 0,

    /// <summary>Row was rejected by schema validation (missing or unparseable required field).</summary>
    Schema = 1,

    /// <summary>Row passed schema checks but was rejected by a business rule.</summary>
    BusinessRule = 2,

    /// <summary>Row is a structural duplicate of another row already seen in this upload.</summary>
    Duplicate = 3,
}
