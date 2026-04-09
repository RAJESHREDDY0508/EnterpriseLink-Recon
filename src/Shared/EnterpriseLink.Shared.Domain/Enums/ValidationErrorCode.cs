namespace EnterpriseLink.Shared.Domain.Enums;

/// <summary>
/// Classifies the reason a parsed row failed validation.
///
/// <para><b>Usage</b>
/// Each <see cref="Validation.ValidationError"/> carries one of these codes so that
/// downstream consumers (dashboards, alerts, exports) can group and filter failures
/// by category without string-parsing the human-readable message.
/// </para>
/// </summary>
public enum ValidationErrorCode
{
    /// <summary>A field that must be present and non-empty is absent or blank.</summary>
    RequiredFieldMissing = 1,

    /// <summary>A field value cannot be parsed into the expected data type (e.g. decimal).</summary>
    InvalidFormat = 2,

    /// <summary>A field value is outside the permitted range (e.g. Amount &lt; 0).</summary>
    ValueOutOfRange = 3,

    /// <summary>A configured business rule rejected the row.</summary>
    BusinessRuleViolation = 4,

    /// <summary>The row is a duplicate of another row already seen in this upload session.</summary>
    DuplicateRecord = 5,
}
