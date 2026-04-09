using EnterpriseLink.Worker.Parsing;
using EnterpriseLink.Worker.Validation.Duplicate;
using FluentAssertions;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="FingerprintDuplicateDetector"/>.
///
/// <para>Acceptance criterion: <b>Hash-based or key-based detection</b></para>
/// </summary>
public sealed class FingerprintDuplicateDetectorTests
{
    private static ParsedRow Row(int rowNumber, params (string Key, string Value)[] fields) =>
        new(rowNumber, fields.ToDictionary(f => f.Key, f => f.Value));

    // ── First-seen rows ───────────────────────────────────────────────────────

    [Fact]
    public void First_row_is_not_a_duplicate()
    {
        var detector = new FingerprintDuplicateDetector();
        var result = detector.IsDuplicate(Row(1, ("Amount", "100"), ("Id", "REF-1")));
        result.Should().BeFalse("the first time a row is seen it is not a duplicate");
    }

    [Fact]
    public void Two_rows_with_different_reference_ids_are_not_duplicates()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Amount", "100"), ("Id", "REF-1")));
        var result = detector.IsDuplicate(Row(2, ("Amount", "100"), ("Id", "REF-2")));
        result.Should().BeFalse("different Id values produce different fingerprints");
    }

    [Fact]
    public void Two_rows_with_different_amounts_are_not_duplicates()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Amount", "100"), ("Id", "REF-1")));
        var result = detector.IsDuplicate(Row(2, ("Amount", "200"), ("Id", "REF-1")));
        result.Should().BeFalse("different Amount values produce different fingerprints");
    }

    // ── Duplicate detection ───────────────────────────────────────────────────

    [Fact]
    public void Second_identical_row_is_detected_as_duplicate()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Amount", "100"), ("Id", "REF-1")));
        var result = detector.IsDuplicate(Row(2, ("Amount", "100"), ("Id", "REF-1")));
        result.Should().BeTrue("identical key fields produce the same fingerprint");
    }

    [Fact]
    public void Duplicate_detected_regardless_of_row_number()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Amount", "50.00"), ("ExternalReferenceId", "ABC")));
        var result = detector.IsDuplicate(Row(999, ("Amount", "50.00"), ("ExternalReferenceId", "ABC")));
        result.Should().BeTrue("row number is not part of the fingerprint");
    }

    [Fact]
    public void Duplicate_detected_with_Value_alias()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Value", "75"), ("Id", "REF-X")));
        var result = detector.IsDuplicate(Row(2, ("Value", "75"), ("Id", "REF-X")));
        result.Should().BeTrue("'Value' alias is included in fingerprint");
    }

    [Fact]
    public void Duplicate_detected_with_ReferenceId_alias()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Amount", "10"), ("ReferenceId", "X-001")));
        var result = detector.IsDuplicate(Row(2, ("Amount", "10"), ("ReferenceId", "X-001")));
        result.Should().BeTrue("'ReferenceId' alias is included in fingerprint");
    }

    // ── Full-row fallback ─────────────────────────────────────────────────────

    [Fact]
    public void Rows_without_key_fields_use_full_row_fallback()
    {
        var detector = new FingerprintDuplicateDetector();
        // No Amount/Id/ExternalReferenceId — fallback hashes all values
        detector.IsDuplicate(Row(1, ("Foo", "bar"), ("Baz", "qux")));
        var result = detector.IsDuplicate(Row(2, ("Foo", "bar"), ("Baz", "qux")));
        result.Should().BeTrue("full-row fallback fingerprint detects identical rows");
    }

    [Fact]
    public void Different_values_with_full_row_fallback_are_not_duplicates()
    {
        var detector = new FingerprintDuplicateDetector();
        detector.IsDuplicate(Row(1, ("Foo", "bar")));
        var result = detector.IsDuplicate(Row(2, ("Foo", "different")));
        result.Should().BeFalse("different values produce different full-row fingerprints");
    }

    // ── Instance isolation ────────────────────────────────────────────────────

    [Fact]
    public void New_detector_instance_does_not_share_state()
    {
        var detector1 = new FingerprintDuplicateDetector();
        var detector2 = new FingerprintDuplicateDetector();

        detector1.IsDuplicate(Row(1, ("Amount", "100"), ("Id", "REF-1")));
        var result = detector2.IsDuplicate(Row(1, ("Amount", "100"), ("Id", "REF-1")));

        result.Should().BeFalse("separate instances maintain independent seen-sets");
    }

    // ── Large volume ──────────────────────────────────────────────────────────

    [Fact]
    public void One_thousand_unique_rows_are_all_non_duplicate()
    {
        var detector = new FingerprintDuplicateDetector();
        for (var i = 1; i <= 1_000; i++)
        {
            var result = detector.IsDuplicate(Row(i, ("Amount", i.ToString()), ("Id", $"REF-{i}")));
            result.Should().BeFalse($"row {i} has a unique key and must not be flagged as duplicate");
        }
    }

    [Fact]
    public void Duplicate_is_detected_among_one_thousand_rows()
    {
        var detector = new FingerprintDuplicateDetector();
        for (var i = 1; i <= 999; i++)
            detector.IsDuplicate(Row(i, ("Amount", i.ToString()), ("Id", $"REF-{i}")));

        // Row 1000 is a repeat of row 1
        var result = detector.IsDuplicate(Row(1000, ("Amount", "1"), ("Id", "REF-1")));
        result.Should().BeTrue("the duplicate must be detected after 999 unique rows");
    }
}
