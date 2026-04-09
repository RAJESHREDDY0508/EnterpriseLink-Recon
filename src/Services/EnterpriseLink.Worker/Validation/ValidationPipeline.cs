using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation.Duplicate;
using EnterpriseLink.Worker.Validation.Rules;

namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// Default implementation of <see cref="IValidationPipeline"/> that runs rows
/// through three sequential stages: schema, business rules, and duplicate detection.
///
/// <para><b>Stage order and short-circuit</b>
/// <list type="number">
///   <item>
///     <description>
///       <b>Schema</b> — all <see cref="IRowValidator"/> implementations tagged as
///       schema validators (<see cref="RequiredFieldsValidator"/>) run first.
///       A failure here immediately marks the row invalid with
///       <c>FailureReason = Schema</c>; business-rule and duplicate checks are
///       skipped for that row.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Business rules</b> — all remaining <see cref="IRowValidator"/>
///       implementations run for rows that passed schema. A failure marks the row
///       invalid with <c>FailureReason = BusinessRule</c>; duplicate check is
///       skipped.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Duplicate detection</b> — rows that cleared both prior stages are
///       fingerprinted. A seen fingerprint marks the row invalid with
///       <c>FailureReason = Duplicate</c>.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para><b>Memory</b>
/// The entire row stream is materialised into two lists before returning. This is
/// intentional: the caller (<c>FileUploadedEventConsumer</c>) needs to pass the
/// valid list to the batch inserter and the invalid list to the error persister
/// sequentially, and an <c>IAsyncEnumerable</c> can only be iterated once. For
/// files up to the expected enterprise batch size (≤1 million rows) the lists fit
/// comfortably in memory.
/// </para>
/// </summary>
public sealed class ValidationPipeline : IValidationPipeline
{
    private readonly IReadOnlyList<IRowValidator> _schemaValidators;
    private readonly IReadOnlyList<IRowValidator> _businessRuleValidators;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly ILogger<ValidationPipeline> _logger;

    /// <summary>
    /// Constructs the pipeline with explicit lists of schema validators, business
    /// rule validators, and the duplicate detector.
    /// </summary>
    /// <param name="schemaValidators">
    /// Validators that enforce structural requirements (e.g. required fields, type
    /// parseability). Registered as <c>ISchemaValidator</c> in DI to distinguish
    /// them from business-rule validators.
    /// </param>
    /// <param name="businessRuleValidators">
    /// Validators that enforce domain rules (e.g. non-negative amount). Registered
    /// as <c>IBusinessRuleValidator</c> in DI.
    /// </param>
    /// <param name="duplicateDetector">
    /// Hash-based or key-based detector. Scoped per message so state resets between
    /// uploads.
    /// </param>
    /// <param name="logger">Structured logger for per-upload summary metrics.</param>
    public ValidationPipeline(
        IEnumerable<ISchemaValidator> schemaValidators,
        IEnumerable<IBusinessRuleValidator> businessRuleValidators,
        IDuplicateDetector duplicateDetector,
        ILogger<ValidationPipeline> logger)
    {
        _schemaValidators = schemaValidators.Cast<IRowValidator>().ToList().AsReadOnly();
        _businessRuleValidators = businessRuleValidators.Cast<IRowValidator>().ToList().AsReadOnly();
        _duplicateDetector = duplicateDetector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<ParsedRow> Valid,
        IReadOnlyList<(ParsedRow Row, IReadOnlyList<ValidationError> Errors, string FailureReason)> Invalid)>
        ClassifyAsync(
            IAsyncEnumerable<ParsedRow> rows,
            CancellationToken cancellationToken = default)
    {
        var valid = new List<ParsedRow>();
        var invalid = new List<(ParsedRow, IReadOnlyList<ValidationError>, string)>();

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            // ── Stage 1: Schema ────────────────────────────────────────────────
            var schemaErrors = RunValidators(_schemaValidators, row);
            if (schemaErrors.Count > 0)
            {
                invalid.Add((row, schemaErrors, ValidationFailureReason.Schema.ToString()));
                continue;
            }

            // ── Stage 2: Business rules ────────────────────────────────────────
            var ruleErrors = RunValidators(_businessRuleValidators, row);
            if (ruleErrors.Count > 0)
            {
                invalid.Add((row, ruleErrors, ValidationFailureReason.BusinessRule.ToString()));
                continue;
            }

            // ── Stage 3: Duplicate detection ───────────────────────────────────
            if (_duplicateDetector.IsDuplicate(row))
            {
                var dupError = new ValidationError(
                    FieldName: "Row",
                    Message: $"Row {row.RowNumber} is a duplicate of a previously seen row in this upload.",
                    Code: ValidationErrorCode.DuplicateRecord);

                invalid.Add((row, new[] { dupError }.AsReadOnly(), ValidationFailureReason.Duplicate.ToString()));
                continue;
            }

            valid.Add(row);
        }

        _logger.LogInformation(
            "Validation complete. Valid={Valid} Invalid={Invalid}",
            valid.Count, invalid.Count);

        return (valid.AsReadOnly(), invalid.AsReadOnly());
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static IReadOnlyList<ValidationError> RunValidators(
        IReadOnlyList<IRowValidator> validators, ParsedRow row)
    {
        List<ValidationError>? errors = null;

        foreach (var validator in validators)
        {
            var result = validator.Validate(row);
            if (!result.IsValid)
            {
                errors ??= new List<ValidationError>();
                errors.AddRange(result.Errors);
            }
        }

        return errors is null
            ? Array.Empty<ValidationError>()
            : errors.AsReadOnly();
    }
}
