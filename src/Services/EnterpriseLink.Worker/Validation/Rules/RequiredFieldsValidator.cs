using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation;

namespace EnterpriseLink.Worker.Validation.Rules;

/// <summary>
/// Schema-stage validator that enforces presence of the <c>Amount</c> field (or its
/// accepted aliases) in every parsed CSV row.
///
/// <para><b>Acceptance criterion: Required fields enforced</b>
/// Every row in an imported CSV must supply a value for <c>Amount</c>. Rows that
/// lack a recognised amount column — or where the field is present but blank — are
/// rejected immediately at the schema stage, before any business-rule checks run.
/// </para>
///
/// <para><b>Candidate columns (case-insensitive, priority order)</b>
/// <list type="bullet">
///   <item><description><c>Amount</c></description></item>
///   <item><description><c>Value</c></description></item>
///   <item><description><c>TotalAmount</c></description></item>
/// </list>
/// A row passes this validator when at least one candidate column is present,
/// non-empty, and parseable as a <c>decimal</c>.
/// </para>
///
/// <para><b>Thread safety</b>
/// This class is stateless and safe for concurrent use. It is registered as a
/// singleton in the DI container.
/// </para>
/// </summary>
public sealed class RequiredFieldsValidator : ISchemaValidator
{
    private static readonly string[] AmountCandidates =
    [
        "Amount", "Value", "TotalAmount"
    ];

    /// <inheritdoc />
    public ValidationResult Validate(ParsedRow row)
    {
        // Check that at least one amount column is present and parseable.
        foreach (var candidate in AmountCandidates)
        {
            if (row.Fields.TryGetValue(candidate, out var raw) &&
                !string.IsNullOrWhiteSpace(raw) &&
                decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            {
                return ValidationResult.Success();
            }
        }

        // No valid amount field found.
        var message = $"Row {row.RowNumber}: no recognised amount field " +
                      $"(tried: {string.Join(", ", AmountCandidates)}). " +
                      "The field must be present, non-empty, and parseable as a decimal.";

        return ValidationResult.Failure(
        [
            new ValidationError(
                FieldName: "Amount",
                Message: message,
                Code: ValidationErrorCode.RequiredFieldMissing)
        ]);
    }
}
