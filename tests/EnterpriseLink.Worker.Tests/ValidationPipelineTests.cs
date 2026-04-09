using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation;
using EnterpriseLink.Worker.Validation.Duplicate;
using EnterpriseLink.Worker.Validation.Rules;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="ValidationPipeline"/> — the three-stage orchestrator.
///
/// <para>Acceptance criteria covered:</para>
/// <list type="bullet">
///   <item><description>Required fields enforced (schema stage).</description></item>
///   <item><description>Rule framework extensible (business-rule stage).</description></item>
///   <item><description>Hash-based or key-based detection (duplicate stage).</description></item>
/// </list>
/// </summary>
public sealed class ValidationPipelineTests
{
    private static ParsedRow ValidRow(int rowNumber = 1, string amount = "100.00", string id = "REF-1") =>
        new(rowNumber, new Dictionary<string, string>
        {
            ["Amount"] = amount,
            ["Id"] = id,
            ["Description"] = "Test row"
        });

    private static ParsedRow RowWithFields(int rowNumber, params (string Key, string Value)[] fields) =>
        new(rowNumber, fields.ToDictionary(f => f.Key, f => f.Value));

    private static ValidationPipeline BuildPipeline() =>
        new ValidationPipeline(
            schemaValidators: new ISchemaValidator[] { new RequiredFieldsValidator() },
            businessRuleValidators: new IBusinessRuleValidator[] { new NonNegativeAmountRule() },
            duplicateDetector: new FingerprintDuplicateDetector(),
            logger: NullLogger<ValidationPipeline>.Instance);

#pragma warning disable CS1998
    private static async IAsyncEnumerable<ParsedRow> ToAsync(IEnumerable<ParsedRow> rows)
    {
        foreach (var row in rows)
            yield return row;
    }
#pragma warning restore CS1998

    // ── All valid ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task All_valid_rows_placed_in_valid_list()
    {
        var pipeline = BuildPipeline();
        var rows = new[] { ValidRow(1), ValidRow(2, id: "REF-2"), ValidRow(3, id: "REF-3") };

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync(rows));

        valid.Should().HaveCount(3, "all rows pass schema, rules, and duplicate checks");
        invalid.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_stream_returns_empty_lists()
    {
        var pipeline = BuildPipeline();

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync([]));

        valid.Should().BeEmpty();
        invalid.Should().BeEmpty();
    }

    // ── Schema stage ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Row_missing_amount_field_goes_to_invalid_with_Schema_reason()
    {
        var pipeline = BuildPipeline();
        var row = RowWithFields(1, ("Description", "No amount here"));

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync([row]));

        valid.Should().BeEmpty();
        invalid.Should().HaveCount(1);
        invalid[0].FailureReason.Should().Be("Schema");
        invalid[0].Errors[0].Code.Should().Be(ValidationErrorCode.RequiredFieldMissing);
    }

    [Fact]
    public async Task Schema_failure_skips_business_rule_check()
    {
        // A row with no Amount and also a negative amount should only produce
        // a schema error — the business rule must not add a second error.
        var pipeline = BuildPipeline();
        var row = RowWithFields(1, ("Description", "nothing")); // no Amount at all

        var (_, invalid) = await pipeline.ClassifyAsync(ToAsync([row]));

        invalid[0].Errors.Should().HaveCount(1, "only schema error, no business rule error");
        invalid[0].FailureReason.Should().Be("Schema");
    }

    // ── Business-rule stage ───────────────────────────────────────────────────

    [Fact]
    public async Task Row_with_negative_amount_goes_to_invalid_with_BusinessRule_reason()
    {
        var pipeline = BuildPipeline();
        var row = RowWithFields(1, ("Amount", "-5.00"), ("Id", "REF-1"));

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync([row]));

        valid.Should().BeEmpty();
        invalid.Should().HaveCount(1);
        invalid[0].FailureReason.Should().Be("BusinessRule");
        invalid[0].Errors[0].Code.Should().Be(ValidationErrorCode.ValueOutOfRange);
    }

    [Fact]
    public async Task Multiple_business_rule_violations_are_all_captured()
    {
        // Register two business-rule validators to verify all errors are collected.
        var pipeline = new ValidationPipeline(
            schemaValidators: new ISchemaValidator[] { new RequiredFieldsValidator() },
            businessRuleValidators: new IBusinessRuleValidator[]
            {
                new NonNegativeAmountRule(),
                new NonNegativeAmountRule()  // same rule twice — produces 2 errors
            },
            duplicateDetector: new FingerprintDuplicateDetector(),
            logger: NullLogger<ValidationPipeline>.Instance);

        var row = RowWithFields(1, ("Amount", "-1"), ("Id", "REF-1"));
        var (_, invalid) = await pipeline.ClassifyAsync(ToAsync([row]));

        invalid[0].Errors.Should().HaveCount(2,
            "both business-rule validators must run and contribute their errors");
    }

    // ── Duplicate-detection stage ─────────────────────────────────────────────

    [Fact]
    public async Task Duplicate_row_goes_to_invalid_with_Duplicate_reason()
    {
        var pipeline = BuildPipeline();
        var row1 = ValidRow(1);
        var row2 = ValidRow(2);  // same Amount+Id as row1 → duplicate

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync([row1, row2]));

        valid.Should().HaveCount(1, "only the first occurrence of the row is valid");
        invalid.Should().HaveCount(1);
        invalid[0].FailureReason.Should().Be("Duplicate");
        invalid[0].Errors[0].Code.Should().Be(ValidationErrorCode.DuplicateRecord);
    }

    [Fact]
    public async Task Three_identical_rows_produce_two_duplicate_entries()
    {
        var pipeline = BuildPipeline();
        var rows = new[] { ValidRow(1), ValidRow(2), ValidRow(3) };

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync(rows));

        valid.Should().HaveCount(1);
        invalid.Should().HaveCount(2, "rows 2 and 3 are duplicates of row 1");
        invalid.Should().AllSatisfy(e => e.FailureReason.Should().Be("Duplicate"));
    }

    // ── Mixed valid + invalid ─────────────────────────────────────────────────

    [Fact]
    public async Task Mixed_rows_are_split_correctly()
    {
        var pipeline = BuildPipeline();
        var rows = new[]
        {
            ValidRow(1, id: "REF-1"),                               // valid
            RowWithFields(2, ("Description", "no amount")),         // schema fail
            ValidRow(3, amount: "-10", id: "REF-3"),               // business-rule fail
            ValidRow(4, id: "REF-4"),                               // valid
            ValidRow(5, id: "REF-1")                                // duplicate of row 1
        };

        var (valid, invalid) = await pipeline.ClassifyAsync(ToAsync(rows));

        valid.Should().HaveCount(2, "rows 1 and 4 are the only fully passing rows");
        invalid.Should().HaveCount(3);
        invalid.Select(e => e.FailureReason)
            .Should().BeEquivalentTo(["Schema", "BusinessRule", "Duplicate"],
                "one of each failure reason");
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_respects_cancellation()
    {
        var pipeline = BuildPipeline();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Use a cancellation-aware stream so WithCancellation actually throws.
        Func<Task> act = async () =>
            await pipeline.ClassifyAsync(CancellableRows(cts.Token), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a pre-cancelled token must abort classification");
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<ParsedRow> CancellableRows(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return ValidRow(1);
    }
#pragma warning restore CS1998
}
