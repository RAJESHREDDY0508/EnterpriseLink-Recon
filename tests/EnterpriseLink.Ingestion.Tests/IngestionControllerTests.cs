using System.Security.Claims;
using System.Text;
using EnterpriseLink.Ingestion.Controllers;
using EnterpriseLink.Ingestion.Models;
using EnterpriseLink.Shared.Infrastructure.Middleware;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EnterpriseLink.Ingestion.Tests;

/// <summary>
/// Unit tests for <see cref="IngestionController"/>.
///
/// <para>
/// Each test configures only what it needs: a mock validator,
/// a mock form file, and a pre-built <see cref="ClaimsPrincipal"/>.
/// No HTTP pipeline, no database, no RabbitMQ.
/// </para>
/// </summary>
public sealed class IngestionControllerTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IngestionController BuildController(
        IValidator<FileUploadRequest>? validator = null,
        Guid? tenantId = null)
    {
        var v = validator ?? PassingValidator();
        var controller = new IngestionController(v, NullLogger<IngestionController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = BuildHttpContext(tenantId ?? TenantId),
        };

        return controller;
    }

    private static IValidator<FileUploadRequest> PassingValidator()
    {
        var mock = new Mock<IValidator<FileUploadRequest>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<FileUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult());
        return mock.Object;
    }

    private static IValidator<FileUploadRequest> FailingValidator(params string[] errorMessages)
    {
        var errors = errorMessages
            .Select(m => new ValidationFailure("File", m))
            .ToList();

        var mock = new Mock<IValidator<FileUploadRequest>>();
        mock.Setup(v => v.ValidateAsync(It.IsAny<FileUploadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult(errors));
        return mock.Object;
    }

    private static DefaultHttpContext BuildHttpContext(Guid tenantId)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(TenantMiddleware.TenantIdClaim, tenantId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, "user-oid-123"),
        ],
        "TestAuth");

        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
        };
    }

    private static Mock<IFormFile> BuildFileMock(
        string fileName = "data.csv",
        string contentType = "text/csv",
        long length = 2048,
        string? csvContent = null)
    {
        var content = csvContent ?? "col1,col2\nrow1a,row1b\nrow2a,row2b";
        var bytes = Encoding.UTF8.GetBytes(content);

        var mock = new Mock<IFormFile>();
        mock.Setup(f => f.FileName).Returns(fileName);
        mock.Setup(f => f.ContentType).Returns(contentType);
        mock.Setup(f => f.Length).Returns(length);
        // Factory overload: returns a fresh MemoryStream on every call.
        // Required when the same mock is used across multiple UploadAsync invocations
        // because StreamReader disposes the stream after reading.
        mock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(bytes));

        return mock;
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    /// <summary>Valid upload returns 200 OK with a populated UploadResult.</summary>
    [Fact]
    public async Task Valid_upload_returns_200_with_UploadResult()
    {
        var controller = BuildController();
        var request = new FileUploadRequest
        {
            File = BuildFileMock(csvContent: "col1,col2\nrow1,row2\nrow3,row4").Object,
            SourceSystem = "Salesforce",
            Description = "Q1 batch",
        };

        var result = await controller.UploadAsync(request, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var body = ok.Value.Should().BeOfType<UploadResult>().Subject;

        body.UploadId.Should().NotBeEmpty("a new GUID should be generated per upload");
        body.TenantId.Should().Be(TenantId);
        body.FileName.Should().Be("data.csv");
        body.DataRowCount.Should().Be(2, "two data rows excluding the header");
        body.SourceSystem.Should().Be("Salesforce");
        body.AcceptedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    /// <summary>Each upload generates a unique UploadId.</summary>
    [Fact]
    public async Task Each_upload_generates_a_unique_UploadId()
    {
        var controller = BuildController();
        var request = new FileUploadRequest
        {
            File = BuildFileMock().Object,
            SourceSystem = "SAP",
        };

        var result1 = await controller.UploadAsync(request, CancellationToken.None);
        var result2 = await controller.UploadAsync(request, CancellationToken.None);

        var id1 = ((result1 as OkObjectResult)!.Value as UploadResult)!.UploadId;
        var id2 = ((result2 as OkObjectResult)!.Value as UploadResult)!.UploadId;

        id1.Should().NotBe(id2, "every upload session must have a unique identifier");
    }

    // ── Row counting ──────────────────────────────────────────────────────────

    /// <summary>Header-only CSV (no data rows) returns DataRowCount = 0.</summary>
    [Fact]
    public async Task Header_only_csv_returns_zero_data_rows()
    {
        var controller = BuildController();
        var request = new FileUploadRequest
        {
            File = BuildFileMock(csvContent: "col1,col2").Object,
            SourceSystem = "Oracle",
        };

        var result = await controller.UploadAsync(request, CancellationToken.None);

        var body = ((result as OkObjectResult)!.Value as UploadResult)!;
        body.DataRowCount.Should().Be(0, "only a header row is present");
    }

    /// <summary>Row count accurately reflects the number of data lines.</summary>
    [Theory]
    [InlineData("h1,h2\nr1a,r1b", 1)]
    [InlineData("h1,h2\nr1a,r1b\nr2a,r2b\nr3a,r3b", 3)]
    [InlineData("h1\nr1\nr2\nr3\nr4\nr5", 5)]
    public async Task Row_count_matches_data_lines(string csvContent, int expectedRows)
    {
        var controller = BuildController();
        var request = new FileUploadRequest
        {
            File = BuildFileMock(csvContent: csvContent).Object,
            SourceSystem = "TestSystem",
        };

        var result = await controller.UploadAsync(request, CancellationToken.None);

        var body = ((result as OkObjectResult)!.Value as UploadResult)!;
        body.DataRowCount.Should().Be(expectedRows);
    }

    // ── Validation failures ───────────────────────────────────────────────────

    /// <summary>When validation fails the controller returns 400 with error details.</summary>
    [Fact]
    public async Task Validation_failure_returns_400_with_error_messages()
    {
        var validator = FailingValidator(
            "Only .csv files are accepted.",
            "SourceSystem is required.");

        var controller = BuildController(validator);
        var request = new FileUploadRequest
        {
            File = BuildFileMock(fileName: "bad.xlsx").Object,
            SourceSystem = string.Empty,
        };

        var result = await controller.UploadAsync(request, CancellationToken.None);

        result.Should().BeOfType<BadRequestObjectResult>(
            "invalid requests must be rejected with 400");
    }

    /// <summary>A single validation error is surfaced in the 400 response body.</summary>
    [Fact]
    public async Task Validation_error_body_contains_field_and_message()
    {
        var validator = FailingValidator("Only .csv files are accepted.");
        var controller = BuildController(validator);

        var result = await controller.UploadAsync(
            new FileUploadRequest { File = BuildFileMock().Object, SourceSystem = "X" },
            CancellationToken.None);

        var bad = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        bad.Value.Should().NotBeNull("error body must be present");
    }

    // ── Tenant resolution ─────────────────────────────────────────────────────

    /// <summary>
    /// A principal without the <c>tenant_id</c> claim is rejected with 401
    /// rather than 403, avoiding leaking whether a tenant exists.
    /// </summary>
    [Fact]
    public async Task Missing_tenant_id_claim_returns_401()
    {
        var controller = new IngestionController(
            PassingValidator(),
            NullLogger<IngestionController>.Instance);

        // Principal with no tenant_id claim
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "user-without-tenant"),
        ],
        "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };

        var result = await controller.UploadAsync(
            new FileUploadRequest { File = BuildFileMock().Object, SourceSystem = "X" },
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>(
            "callers must exchange their token to obtain a tenant_id claim before uploading");
    }

    /// <summary>A malformed (non-GUID) tenant_id claim is rejected with 401.</summary>
    [Fact]
    public async Task Malformed_tenant_id_claim_returns_401()
    {
        var controller = new IngestionController(
            PassingValidator(),
            NullLogger<IngestionController>.Instance);

        var identity = new ClaimsIdentity(
        [
            new Claim(TenantMiddleware.TenantIdClaim, "not-a-guid"),
        ],
        "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
            },
        };

        var result = await controller.UploadAsync(
            new FileUploadRequest { File = BuildFileMock().Object, SourceSystem = "X" },
            CancellationToken.None);

        result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    /// <summary>TenantId from the JWT claim is reflected in the UploadResult.</summary>
    [Fact]
    public async Task TenantId_from_claim_is_present_in_result()
    {
        var expectedTenant = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        var controller = BuildController(tenantId: expectedTenant);

        var request = new FileUploadRequest
        {
            File = BuildFileMock().Object,
            SourceSystem = "SAP",
        };

        var result = await controller.UploadAsync(request, CancellationToken.None);

        var body = ((result as OkObjectResult)!.Value as UploadResult)!;
        body.TenantId.Should().Be(expectedTenant);
    }
}
