using EnterpriseLink.Shared.Domain.Enums;

namespace EnterpriseLink.Shared.Domain.Validation;

/// <summary>
/// Describes a single field-level or row-level validation failure.
///
/// <para><b>Immutability</b>
/// <c>ValidationError</c> is a positional record — it is immutable after
/// construction and safe for concurrent reads. Instances are never mutated; a new
/// instance is created for each distinct error.
/// </para>
/// </summary>
/// <param name="FieldName">
/// The name of the CSV column (or <c>"Row"</c> for row-level errors such as
/// duplicate detection) that caused the failure.
/// </param>
/// <param name="Message">
/// A human-readable description of the failure, suitable for display in an
/// operator dashboard or an error-export report.
/// </param>
/// <param name="Code">
/// A structured <see cref="ValidationErrorCode"/> that classifies the failure
/// category, enabling programmatic filtering without string-parsing.
/// </param>
public sealed record ValidationError(
    string FieldName,
    string Message,
    ValidationErrorCode Code);
