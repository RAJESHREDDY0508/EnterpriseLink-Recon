using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Shared.Contracts.Schemas;
using FluentAssertions;
using System.Text.Json;

namespace EnterpriseLink.Ingestion.Tests;

/// <summary>
/// Validates that <see cref="FileUploadedEvent"/> payloads conform to the JSON Schema
/// contract defined in <c>schemas/messages/v1/file-uploaded-event.schema.json</c>.
///
/// <para>
/// These tests serve as the living proof that the C# event type and the canonical
/// JSON Schema contract remain in sync. A failing test here means either the type was
/// changed without updating the schema, or the schema was changed without updating
/// the type — both conditions must block the CI pipeline.
/// </para>
///
/// <para><b>Serialisation convention</b></para>
/// Events are serialised with <c>camelCase</c> property names
/// (<see cref="JsonNamingPolicy.CamelCase"/>) to produce idiomatic JSON.
/// The schema uses camelCase property names to match.
/// </para>
/// </summary>
public sealed class FileUploadedEventSchemaTests
{
    private static readonly IEventSchemaValidator Validator = new JsonNetSchemaValidator();

    // Serialiser options: camelCase to match the JSON Schema property names.
    private static readonly JsonSerializerOptions SerialiserOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Serialise(object @event)
        => JsonSerializer.Serialize(@event, SerialiserOptions);

    private static FileUploadedEvent ValidEvent() => new()
    {
        UploadId = Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"),
        TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        StoragePath = "11111111-1111-1111-1111-111111111111/3fa85f64-5717-4562-b3fc-2c963f66afa6/data.csv",
        FileName = "data.csv",
        FileSizeBytes = 10_485_760,
        DataRowCount = 98_432,
        SourceSystem = "Salesforce",
        UploadedAt = DateTimeOffset.Parse("2026-04-02T09:15:00Z"),
    };

    // ── Schema loading ────────────────────────────────────────────────────────

    /// <summary>
    /// The schema embedded resource must exist and load without error.
    /// A failure here indicates the csproj EmbeddedResource entry is missing
    /// or the file was moved.
    /// </summary>
    [Fact]
    public void Schema_loads_from_embedded_resource_without_error()
    {
        // If the schema cannot be loaded, Validate will return a failed result with
        // a descriptive error — we surface that message for easier diagnosis.
        var result = Validator.Validate(Serialise(ValidEvent()), KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeTrue(
            $"schema load and validation of a valid event must succeed. Errors: {string.Join("; ", result.Errors)}");
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>A fully valid event passes all schema constraints.</summary>
    [Fact]
    public void Valid_event_passes_schema_validation()
    {
        var json = Serialise(ValidEvent());
        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeTrue(
            $"a well-formed FileUploadedEvent must pass schema validation. Errors: {string.Join("; ", result.Errors)}");
        result.Errors.Should().BeEmpty();
    }

    // ── Required property enforcement ─────────────────────────────────────────

    /// <summary>Each required property is individually tested for absence.</summary>
    [Theory]
    [InlineData("uploadId")]
    [InlineData("tenantId")]
    [InlineData("storagePath")]
    [InlineData("fileName")]
    [InlineData("fileSizeBytes")]
    [InlineData("dataRowCount")]
    [InlineData("sourceSystem")]
    [InlineData("uploadedAt")]
    public void Missing_required_property_fails_schema_validation(string missingProperty)
    {
        // Serialise a valid event, parse to a mutable dictionary, remove the property.
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            Serialise(ValidEvent()))!;

        doc.Remove(missingProperty);
        var json = JsonSerializer.Serialize(doc);

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeFalse(
            $"an event missing '{missingProperty}' must fail schema validation");
        result.Errors.Should().NotBeEmpty();
    }

    // ── Type constraints ──────────────────────────────────────────────────────

    /// <summary>fileSizeBytes must be an integer ≥ 1; a string value is rejected.</summary>
    [Fact]
    public void FileSizeBytes_as_string_fails_type_validation()
    {
        var json = Serialise(ValidEvent()).Replace("\"fileSizeBytes\":10485760", "\"fileSizeBytes\":\"ten-mb\"");
        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse("fileSizeBytes must be an integer, not a string");
    }

    /// <summary>fileSizeBytes = 0 violates the minimum: 1 constraint.</summary>
    [Fact]
    public void FileSizeBytes_of_zero_fails_minimum_constraint()
    {
        var json = Serialise(ValidEvent()).Replace("\"fileSizeBytes\":10485760", "\"fileSizeBytes\":0");
        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse("fileSizeBytes must be at least 1 byte");
    }

    /// <summary>dataRowCount = -1 violates the minimum: 0 constraint.</summary>
    [Fact]
    public void DataRowCount_negative_fails_minimum_constraint()
    {
        var json = Serialise(ValidEvent()).Replace("\"dataRowCount\":98432", "\"dataRowCount\":-1");
        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse("dataRowCount must be 0 or greater");
    }

    /// <summary>dataRowCount = 0 is valid (header-only file).</summary>
    [Fact]
    public void DataRowCount_of_zero_passes_minimum_constraint()
    {
        var json = Serialise(ValidEvent()).Replace("\"dataRowCount\":98432", "\"dataRowCount\":0");
        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeTrue("a header-only file with 0 data rows is a valid upload");
    }

    // ── String constraints ────────────────────────────────────────────────────

    /// <summary>An empty storagePath string violates minLength: 1.</summary>
    [Fact]
    public void Empty_storagePath_fails_minLength_constraint()
    {
        var json = Serialise(new
        {
            uploadId = ValidEvent().UploadId,
            tenantId = ValidEvent().TenantId,
            storagePath = string.Empty,
            fileName = ValidEvent().FileName,
            fileSizeBytes = ValidEvent().FileSizeBytes,
            dataRowCount = ValidEvent().DataRowCount,
            sourceSystem = ValidEvent().SourceSystem,
            uploadedAt = ValidEvent().UploadedAt,
        });

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse("storagePath must not be empty");
    }

    /// <summary>sourceSystem with invalid characters (e.g. '!') violates the pattern constraint.</summary>
    [Fact]
    public void SourceSystem_with_invalid_characters_fails_pattern_constraint()
    {
        var json = Serialise(ValidEvent())
            .Replace("\"sourceSystem\":\"Salesforce\"", "\"sourceSystem\":\"Sales!force\"");

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse("sourceSystem may only contain letters, digits, hyphens, underscores, and spaces");
    }

    // ── Additional properties guard ───────────────────────────────────────────

    /// <summary>
    /// Unknown properties are rejected by <c>additionalProperties: false</c>.
    /// This prevents silent schema drift where producers add fields that
    /// consumers do not know about.
    /// </summary>
    [Fact]
    public void Unknown_property_fails_additionalProperties_constraint()
    {
        var doc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            Serialise(ValidEvent()))!;

        doc["unknownField"] = JsonDocument.Parse("\"surprise\"").RootElement;
        var json = JsonSerializer.Serialize(doc);

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse(
            "the schema uses additionalProperties: false to catch undocumented field additions");
    }

    // ── Invalid JSON guard ────────────────────────────────────────────────────

    /// <summary>Malformed JSON returns a failed result with a parse error, not an exception.</summary>
    [Fact]
    public void Malformed_json_returns_failed_result_without_throwing()
    {
        var act = () => Validator.Validate("{this is not json}", KnownSchemas.FileUploadedEventV1);

        act.Should().NotThrow("the validator must not throw on malformed input");
        var result = Validator.Validate("{this is not json}", KnownSchemas.FileUploadedEventV1);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("not valid JSON"));
    }

    // ── Guid.Empty exclusion ──────────────────────────────────────────────────

    /// <summary>
    /// The all-zeros UUID (<c>00000000-0000-0000-0000-000000000000</c>) is a sentinel
    /// value indicating an uninitialised C# <c>Guid</c>. Publishing it as an
    /// <c>uploadId</c> would corrupt idempotency tracking.
    /// </summary>
    [Fact]
    public void UploadId_of_Guid_Empty_fails_schema_validation()
    {
        var json = Serialise(ValidEvent())
            .Replace(
                "\"uploadId\":\"3fa85f64-5717-4562-b3fc-2c963f66afa6\"",
                "\"uploadId\":\"00000000-0000-0000-0000-000000000000\"");

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeFalse(
            "Guid.Empty is explicitly excluded from uploadId to prevent uninitialised value propagation");
    }

    /// <summary>
    /// The all-zeros UUID as <c>tenantId</c> would route all data to a phantom tenant,
    /// corrupting every tenant isolation boundary in the system.
    /// </summary>
    [Fact]
    public void TenantId_of_Guid_Empty_fails_schema_validation()
    {
        var json = Serialise(ValidEvent())
            .Replace(
                "\"tenantId\":\"11111111-1111-1111-1111-111111111111\"",
                "\"tenantId\":\"00000000-0000-0000-0000-000000000000\"");

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeFalse(
            "Guid.Empty is explicitly excluded from tenantId to prevent cross-tenant data pollution");
    }

    // ── DataRowCount maximum ──────────────────────────────────────────────────

    /// <summary>
    /// <c>dataRowCount</c> exceeding <see cref="int.MaxValue"/> (2,147,483,647) would
    /// overflow the C# <c>int</c> property during JSON deserialisation, producing a
    /// negative or incorrect count. The schema maximum guards against this.
    /// </summary>
    [Fact]
    public void DataRowCount_exceeding_int_max_fails_maximum_constraint()
    {
        // 2,147,483,648 = int.MaxValue + 1
        var json = Serialise(ValidEvent())
            .Replace("\"dataRowCount\":98432", "\"dataRowCount\":2147483648");

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeFalse(
            "dataRowCount must not exceed Int32.MaxValue (2,147,483,647) to prevent C# int overflow");
    }

    /// <summary>
    /// <c>dataRowCount</c> exactly at <see cref="int.MaxValue"/> is the boundary value
    /// — must be accepted.
    /// </summary>
    [Fact]
    public void DataRowCount_at_int_max_passes_maximum_constraint()
    {
        var json = Serialise(ValidEvent())
            .Replace("\"dataRowCount\":98432", "\"dataRowCount\":2147483647");

        var result = Validator.Validate(json, KnownSchemas.FileUploadedEventV1);

        result.IsValid.Should().BeTrue(
            $"dataRowCount of Int32.MaxValue is the allowed boundary. Errors: {string.Join("; ", result.Errors)}");
    }
}
