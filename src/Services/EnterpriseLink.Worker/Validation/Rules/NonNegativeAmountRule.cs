using System.Globalization;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation;

namespace EnterpriseLink.Worker.Validation.Rules;

/// <summary>
/// Business-rule validator that rejects rows whose parsed <c>Amount</c> is
/// strictly negative.
///
/// <para><b>Rule rationale</b>
/// In the EnterpriseLink reconciliation domain, negative amounts represent credits
/// or reversals that must be submitted through a dedicated workflow rather than the
/// normal CSV upload path. Allowing raw negative amounts through batch insert would
/// corrupt reconciliation ledger totals.
/// </para>
///
/// <para><b>Extensibility note</b>
/// This class demonstrates the extensible rule framework — adding a new business
/// rule requires only implementing <see cref="IRowValidator"/> and registering it
/// with the DI container. No changes to <see cref="ValidationPipeline"/> or any
/// existing rule are needed.
/// </para>
///
/// <para><b>Thread safety</b>
/// This class is stateless and safe for concurrent use. It is registered as a
/// singleton in the DI container.
/// </para>
/// </summary>
public sealed class NonNegativeAmountRule : IBusinessRuleValidator
{
    private static readonly string[] AmountCandidates =
    [
        "Amount", "Value", "TotalAmount"
    ];

    /// <inheritdoc />
    public ValidationResult Validate(ParsedRow row)
    {
        foreach (var candidate in AmountCandidates)
        {
            if (!row.Fields.TryGetValue(candidate, out var raw) ||
                string.IsNullOrWhiteSpace(raw))
                continue;

            if (!decimal.TryParse(raw, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var amount))
                continue;   // RequiredFieldsValidator already caught un-parseable values

            if (amount < 0m)
            {
                return ValidationResult.Failure(
                [
                    new ValidationError(
                        FieldName: candidate,
                        Message: $"Row {row.RowNumber}: {candidate} value '{raw}' is negative " +
                                 "({amount:F4}). Negative amounts are not permitted in batch " +
                                 "CSV uploads; use the reversal workflow instead.",
                        Code: ValidationErrorCode.ValueOutOfRange)
                ]);
            }

            return ValidationResult.Success();
        }

        // No amount field found — RequiredFieldsValidator handles this case.
        return ValidationResult.Success();
    }
}
