using System.Text;
using EnterpriseLink.Ingestion.Configuration;
using EnterpriseLink.Ingestion.Models;
using EnterpriseLink.Ingestion.Validation;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Moq;

namespace EnterpriseLink.Ingestion.Tests;

/// <summary>
/// Unit tests for <see cref="FileUploadRequestValidator"/>.
///
/// <para>
/// Tests validate all file constraints (presence, size, extension, content-type)
/// and all metadata constraints (SourceSystem, Description) in isolation from the
/// HTTP pipeline. No controller, no database, no RabbitMQ.
/// </para>
/// </summary>
public sealed class FileUploadValidatorTests
{
    // ── Default valid options ─────────────────────────────────────────────────
    private static readonly IngestionOptions DefaultOptions = new()
    {
        MaxFileSizeBytes = 524_288_000L,
        MemoryBufferThresholdBytes = 1_048_576,
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IValidator<FileUploadRequest> BuildValidator(IngestionOptions? options = null)
    {
        var opts = Options.Create(options ?? DefaultOptions);
        return new FileUploadRequestValidator(opts);
    }

    private static Mock<IFormFile> BuildFileMock(
        string fileName = "data.csv",
        string contentType = "text/csv",
        long length = 1024,
        string? content = null)
    {
        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.Length).Returns(length);

        var bytes = Encoding.UTF8.GetBytes(content ?? "col1,col2\nval1,val2");
        mock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(bytes));

        return mock;
    }

    private static FileUploadRequest ValidRequest(IFormFile? file = null) => new()
    {
        File = file ?? BuildFileMock().Object,
        SourceSystem = "Salesforce",
        Description = "Q1 batch",
    };

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>A fully valid request passes all rules.</summary>
    [Fact]
    public async Task ValidRequest_passes_all_rules()
    {
        var validator = BuildValidator();
        var result = await validator.ValidateAsync(ValidRequest());
        result.IsValid.Should().BeTrue("a well-formed .csv upload with valid metadata should pass");
    }

    // ── File presence ─────────────────────────────────────────────────────────

    /// <summary>Null file (missing multipart part) is rejected.</summary>
    [Fact]
    public async Task Null_file_fails_with_required_message()
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.File = null!;

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "File" &&
            e.ErrorMessage.Contains("must be provided"));
    }

    // ── File size ─────────────────────────────────────────────────────────────

    /// <summary>Empty file (0 bytes) is rejected.</summary>
    [Fact]
    public async Task Empty_file_fails_validation()
    {
        var validator = BuildValidator();
        var file = BuildFileMock(length: 0);
        var result = await validator.ValidateAsync(ValidRequest(file.Object));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.ErrorMessage.Contains("must not be empty"));
    }

    /// <summary>File exactly at the size limit is accepted.</summary>
    [Fact]
    public async Task File_at_exact_size_limit_passes()
    {
        var options = new IngestionOptions { MaxFileSizeBytes = 1000L };
        var validator = BuildValidator(options);
        var file = BuildFileMock(length: 1000L);

        var result = await validator.ValidateAsync(ValidRequest(file.Object));

        result.IsValid.Should().BeTrue("a file exactly at the limit should be accepted");
    }

    /// <summary>File one byte over the size limit is rejected.</summary>
    [Fact]
    public async Task File_exceeding_size_limit_fails_validation()
    {
        var options = new IngestionOptions { MaxFileSizeBytes = 1000L };
        var validator = BuildValidator(options);
        var file = BuildFileMock(length: 1001L);

        var result = await validator.ValidateAsync(ValidRequest(file.Object));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.ErrorMessage.Contains("exceeds the maximum allowed size"));
    }

    // ── File extension ────────────────────────────────────────────────────────

    /// <summary>A .csv extension (lower-case) is accepted.</summary>
    [Fact]
    public async Task Lowercase_csv_extension_passes()
    {
        var validator = BuildValidator();
        var file = BuildFileMock(fileName: "transactions.csv");
        var result = await validator.ValidateAsync(ValidRequest(file.Object));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>A .CSV extension (upper-case) is accepted — comparison is case-insensitive.</summary>
    [Fact]
    public async Task Uppercase_CSV_extension_passes()
    {
        var validator = BuildValidator();
        var file = BuildFileMock(fileName: "transactions.CSV");
        var result = await validator.ValidateAsync(ValidRequest(file.Object));
        result.IsValid.Should().BeTrue("extension check must be case-insensitive");
    }

    /// <summary>Non-CSV extensions are rejected.</summary>
    [Theory]
    [InlineData("transactions.xlsx")]
    [InlineData("transactions.json")]
    [InlineData("transactions.txt")]
    [InlineData("transactions")]
    public async Task Non_csv_extension_fails_validation(string fileName)
    {
        var validator = BuildValidator();
        var file = BuildFileMock(fileName: fileName);

        var result = await validator.ValidateAsync(ValidRequest(file.Object));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.ErrorMessage.Contains("Only .csv files are accepted"));
    }

    // ── Content-Type ──────────────────────────────────────────────────────────

    /// <summary>All accepted Content-Type values pass.</summary>
    [Theory]
    [InlineData("text/csv")]
    [InlineData("text/plain")]
    [InlineData("application/octet-stream")]
    [InlineData("application/vnd.ms-excel")]
    public async Task Accepted_content_types_pass(string contentType)
    {
        var validator = BuildValidator();
        var file = BuildFileMock(contentType: contentType);
        var result = await validator.ValidateAsync(ValidRequest(file.Object));
        result.IsValid.Should().BeTrue($"Content-Type '{contentType}' should be accepted");
    }

    /// <summary>Unrecognised Content-Type values are rejected.</summary>
    [Theory]
    [InlineData("application/json")]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    public async Task Unrecognised_content_type_fails_validation(string contentType)
    {
        var validator = BuildValidator();
        var file = BuildFileMock(contentType: contentType);

        var result = await validator.ValidateAsync(ValidRequest(file.Object));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.ErrorMessage.Contains("is not accepted"));
    }

    // ── SourceSystem ──────────────────────────────────────────────────────────

    /// <summary>Missing SourceSystem is rejected.</summary>
    [Fact]
    public async Task Missing_SourceSystem_fails_validation()
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.SourceSystem = string.Empty;

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "SourceSystem" &&
            e.ErrorMessage.Contains("required"));
    }

    /// <summary>SourceSystem longer than 100 characters is rejected.</summary>
    [Fact]
    public async Task SourceSystem_exceeding_100_chars_fails()
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.SourceSystem = new string('A', 101);

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "SourceSystem" &&
            e.ErrorMessage.Contains("100 characters"));
    }

    /// <summary>SourceSystem with special characters not in the allowed set is rejected.</summary>
    [Theory]
    [InlineData("Sales!force")]
    [InlineData("SAP@ERP")]
    [InlineData("oracle.erp")]
    public async Task SourceSystem_with_invalid_characters_fails(string sourceSystem)
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.SourceSystem = sourceSystem;

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "SourceSystem");
    }

    /// <summary>SourceSystem with allowed characters (letters, digits, hyphens, underscores, spaces) passes.</summary>
    [Theory]
    [InlineData("Salesforce")]
    [InlineData("SAP-ERP")]
    [InlineData("Oracle_ERP")]
    [InlineData("My Source System 123")]
    public async Task SourceSystem_with_valid_characters_passes(string sourceSystem)
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.SourceSystem = sourceSystem;

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue($"SourceSystem '{sourceSystem}' should be valid");
    }

    /// <summary>SourceSystem that is only whitespace (e.g. "   ") is rejected.</summary>
    [Theory]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("  \n  ")]
    public async Task Whitespace_only_SourceSystem_fails_validation(string sourceSystem)
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.SourceSystem = sourceSystem;

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse(
            "a whitespace-only SourceSystem is semantically empty and must be rejected");
        result.Errors.Should().Contain(e =>
            e.PropertyName == "SourceSystem",
            "the SourceSystem field must carry the validation failure");
    }

    // ── FileName null guard ───────────────────────────────────────────────────

    /// <summary>
    /// A null FileName must produce a clear validation error rather than a
    /// NullReferenceException from the extension check.
    /// </summary>
    [Fact]
    public async Task Null_FileName_produces_validation_error_not_exception()
    {
        var validator = BuildValidator();
        var fileMock = BuildFileMock(fileName: null!);

        // Set FileName to null explicitly — some HTTP clients omit the filename header.
        fileMock.Setup(f => f.FileName).Returns((string)null!);

        var request = ValidRequest(fileMock.Object);
        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse("a null FileName must fail validation, not throw");
        result.Errors.Should().Contain(e =>
            e.PropertyName == "File.FileName",
            "the FileName field must carry the error message");
    }

    /// <summary>An empty string FileName is rejected.</summary>
    [Fact]
    public async Task Empty_FileName_fails_validation()
    {
        var validator = BuildValidator();
        var fileMock = BuildFileMock(fileName: string.Empty);
        var result = await validator.ValidateAsync(ValidRequest(fileMock.Object));

        result.IsValid.Should().BeFalse("an empty FileName must fail validation");
        result.Errors.Should().Contain(e => e.PropertyName == "File.FileName");
    }

    // ── Description ───────────────────────────────────────────────────────────

    /// <summary>Null Description is accepted (optional field).</summary>
    [Fact]
    public async Task Null_Description_passes_validation()
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.Description = null;

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue("Description is optional");
    }

    /// <summary>Description exactly at 500 characters passes.</summary>
    [Fact]
    public async Task Description_at_500_chars_passes()
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.Description = new string('A', 500);

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeTrue();
    }

    /// <summary>Description exceeding 500 characters is rejected.</summary>
    [Fact]
    public async Task Description_exceeding_500_chars_fails()
    {
        var validator = BuildValidator();
        var request = ValidRequest();
        request.Description = new string('A', 501);

        var result = await validator.ValidateAsync(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "Description" &&
            e.ErrorMessage.Contains("500 characters"));
    }
}
