using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation.Rules;
using FluentAssertions;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="NonNegativeAmountRule"/>.
///
/// <para>Acceptance criterion: <b>Rule framework extensible</b>
/// This rule class demonstrates that a new business rule can be added by
/// implementing <c>IBusinessRuleValidator</c> alone — zero changes to the pipeline.
/// </para>
/// </summary>
public sealed class NonNegativeAmountRuleTests
{
    private readonly NonNegativeAmountRule _sut = new();

    private static ParsedRow Row(params (string Key, string Value)[] fields) =>
        new(1, fields.ToDictionary(f => f.Key, f => f.Value));

    // ── Passing cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_positive_amount_passes()
    {
        var result = _sut.Validate(Row(("Amount", "100.00")));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_zero_amount_passes()
    {
        var result = _sut.Validate(Row(("Amount", "0")));
        result.IsValid.Should().BeTrue("zero is not negative");
    }

    [Fact]
    public void Validate_positive_value_alias_passes()
    {
        var result = _sut.Validate(Row(("Value", "9.99")));
        result.IsValid.Should().BeTrue("'Value' alias is checked");
    }

    [Fact]
    public void Validate_positive_TotalAmount_alias_passes()
    {
        var result = _sut.Validate(Row(("TotalAmount", "1000")));
        result.IsValid.Should().BeTrue("'TotalAmount' alias is checked");
    }

    [Fact]
    public void Validate_row_without_amount_field_passes()
    {
        // RequiredFieldsValidator handles the missing-field case;
        // this rule must not add a spurious error on top.
        var result = _sut.Validate(Row(("Description", "test")));
        result.IsValid.Should().BeTrue("missing amount is not this rule's responsibility");
    }

    // ── Failing cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_negative_amount_fails()
    {
        var result = _sut.Validate(Row(("Amount", "-1.00")));

        result.IsValid.Should().BeFalse("negative amounts are not permitted in batch uploads");
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Code.Should().Be(ValidationErrorCode.ValueOutOfRange);
        result.Errors[0].FieldName.Should().Be("Amount");
    }

    [Fact]
    public void Validate_negative_value_alias_fails()
    {
        var result = _sut.Validate(Row(("Value", "-0.01")));

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be(ValidationErrorCode.ValueOutOfRange);
    }

    [Fact]
    public void Validate_large_negative_amount_fails()
    {
        var result = _sut.Validate(Row(("Amount", "-999999.99")));

        result.IsValid.Should().BeFalse();
        result.Errors[0].Code.Should().Be(ValidationErrorCode.ValueOutOfRange);
    }
}
