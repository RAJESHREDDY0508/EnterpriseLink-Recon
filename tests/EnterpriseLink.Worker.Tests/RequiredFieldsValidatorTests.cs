using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation.Rules;
using FluentAssertions;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="RequiredFieldsValidator"/>.
///
/// <para>Acceptance criterion: <b>Required fields enforced</b></para>
/// </summary>
public sealed class RequiredFieldsValidatorTests
{
    private readonly RequiredFieldsValidator _sut = new();

    private static ParsedRow Row(params (string Key, string Value)[] fields) =>
        new(1, fields.ToDictionary(f => f.Key, f => f.Value));

    // ── Passing cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_row_with_Amount_field_passes()
    {
        var result = _sut.Validate(Row(("Amount", "100.00")));

        result.IsValid.Should().BeTrue("Amount is a recognised required field");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_row_with_Value_alias_passes()
    {
        var result = _sut.Validate(Row(("Value", "50.5")));

        result.IsValid.Should().BeTrue("'Value' is an accepted alias for Amount");
    }

    [Fact]
    public void Validate_row_with_TotalAmount_alias_passes()
    {
        var result = _sut.Validate(Row(("TotalAmount", "999.99")));

        result.IsValid.Should().BeTrue("'TotalAmount' is an accepted alias for Amount");
    }

    [Fact]
    public void Validate_row_with_zero_amount_passes()
    {
        var result = _sut.Validate(Row(("Amount", "0")));

        result.IsValid.Should().BeTrue("zero is a valid decimal amount");
    }

    [Fact]
    public void Validate_row_with_large_decimal_passes()
    {
        var result = _sut.Validate(Row(("Amount", "1234567890.1234")));

        result.IsValid.Should().BeTrue("large decimals with 4 decimal places must be accepted");
    }

    // ── Failing cases ─────────────────────────────────────────────────────────

    [Fact]
    public void Validate_row_with_no_amount_field_fails()
    {
        var result = _sut.Validate(Row(("Description", "Purchase"), ("Id", "REF-001")));

        result.IsValid.Should().BeFalse("no amount column is present");
        result.Errors.Should().HaveCount(1);
        result.Errors[0].Code.Should().Be(ValidationErrorCode.RequiredFieldMissing);
        result.Errors[0].FieldName.Should().Be("Amount");
    }

    [Fact]
    public void Validate_row_with_blank_amount_fails()
    {
        var result = _sut.Validate(Row(("Amount", "   ")));

        result.IsValid.Should().BeFalse("blank Amount is treated as missing");
        result.Errors[0].Code.Should().Be(ValidationErrorCode.RequiredFieldMissing);
    }

    [Fact]
    public void Validate_row_with_empty_amount_fails()
    {
        var result = _sut.Validate(Row(("Amount", "")));

        result.IsValid.Should().BeFalse("empty Amount is treated as missing");
    }

    [Fact]
    public void Validate_row_with_non_numeric_amount_fails()
    {
        var result = _sut.Validate(Row(("Amount", "N/A")));

        result.IsValid.Should().BeFalse("non-numeric Amount cannot be parsed as decimal");
        result.Errors[0].Code.Should().Be(ValidationErrorCode.RequiredFieldMissing);
    }

    [Fact]
    public void Validate_empty_row_fails()
    {
        var result = _sut.Validate(Row());

        result.IsValid.Should().BeFalse("a row with no fields at all has no Amount");
    }
}
