using EnterpriseLink.Dashboard.Controllers;
using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Dashboard.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EnterpriseLink.Dashboard.Tests;

/// <summary>
/// Unit tests for <see cref="ErrorViewerController"/>.
///
/// <para>
/// Covers both the upload-scoped endpoint (<c>GET /api/uploads/{uploadId}/errors</c>)
/// and the global cross-upload endpoint (<c>GET /api/errors</c>).
/// </para>
///
/// <para>Acceptance criterion: <b>Validation errors queryable</b></para>
/// </summary>
public sealed class ErrorViewerControllerTests
{
    private readonly Mock<IErrorViewerService> _serviceMock = new();
    private readonly ErrorViewerController _controller;

    public ErrorViewerControllerTests()
    {
        _controller = new ErrorViewerController(
            _serviceMock.Object,
            NullLogger<ErrorViewerController>.Instance);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static InvalidTransactionDto MakeDto(
        Guid? uploadId = null,
        string failureReason = "Schema") =>
        new(
            InvalidTransactionId: Guid.NewGuid(),
            UploadId: uploadId ?? Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            RowNumber: 42,
            RawData: "{\"Amount\":\"abc\"}",
            ValidationErrors: "[\"[RequiredFieldMissing] Amount: No valid amount found\"]",
            FailureReason: failureReason,
            CreatedAt: DateTimeOffset.UtcNow);

    private static PagedResult<InvalidTransactionDto> MakePage(
        IReadOnlyList<InvalidTransactionDto>? items = null)
    {
        var list = items ?? new[] { MakeDto() };
        return new PagedResult<InvalidTransactionDto>(list, list.Count, 1, 20);
    }

    // ── GET /api/uploads/{uploadId}/errors ────────────────────────────────────

    [Fact]
    public async Task GetUploadErrors_returns_200_with_paged_result()
    {
        var uploadId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        var result = await _controller.GetUploadErrorsAsync(
            uploadId, new ErrorViewerQuery(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
              .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GetUploadErrors_overrides_UploadId_from_route()
    {
        // Even if the query has a different UploadId, the route value must win.
        var routeUploadId = Guid.NewGuid();
        var queryUploadId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetUploadErrorsAsync(
            routeUploadId,
            new ErrorViewerQuery { UploadId = queryUploadId },
            CancellationToken.None);

        _serviceMock.Verify(s => s.GetErrorsAsync(
            It.Is<ErrorViewerQuery>(q => q.UploadId == routeUploadId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUploadErrors_preserves_failureReason_from_query()
    {
        var uploadId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetUploadErrorsAsync(
            uploadId,
            new ErrorViewerQuery { FailureReason = "BusinessRule" },
            CancellationToken.None);

        _serviceMock.Verify(s => s.GetErrorsAsync(
            It.Is<ErrorViewerQuery>(q => q.FailureReason == "BusinessRule"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUploadErrors_returns_empty_page_when_no_errors()
    {
        var uploadId = Guid.NewGuid();
        var emptyPage = new PagedResult<InvalidTransactionDto>(
            Array.Empty<InvalidTransactionDto>(), 0, 1, 20);
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(emptyPage);

        var result = await _controller.GetUploadErrorsAsync(
            uploadId, new ErrorViewerQuery(), CancellationToken.None);

        var payload = ((OkObjectResult)result).Value
            .Should().BeOfType<PagedResult<InvalidTransactionDto>>().Subject;
        payload.Items.Should().BeEmpty();
        payload.TotalCount.Should().Be(0);
    }

    // ── GET /api/errors ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllErrors_returns_200_with_paged_result()
    {
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        var result = await _controller.GetAllErrorsAsync(new ErrorViewerQuery(), CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>()
              .Which.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task GetAllErrors_passes_full_query_to_service()
    {
        var query = new ErrorViewerQuery
        {
            Page = 3,
            PageSize = 10,
            FailureReason = "Duplicate",
            TenantId = Guid.NewGuid(),
        };
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAllErrorsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetErrorsAsync(
            It.Is<ErrorViewerQuery>(q =>
                q.Page == 3 &&
                q.PageSize == 10 &&
                q.FailureReason == "Duplicate" &&
                q.TenantId == query.TenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllErrors_forwards_cancellation_token_to_service()
    {
        using var cts = new CancellationTokenSource();
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), cts.Token))
                    .ReturnsAsync(MakePage());

        await _controller.GetAllErrorsAsync(new ErrorViewerQuery(), cts.Token);

        _serviceMock.Verify(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), cts.Token), Times.Once);
    }

    // ── Service called exactly once per request ───────────────────────────────

    [Fact]
    public async Task GetUploadErrors_calls_service_exactly_once()
    {
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetUploadErrorsAsync(Guid.NewGuid(), new ErrorViewerQuery(), CancellationToken.None);

        _serviceMock.Verify(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAllErrors_calls_service_exactly_once()
    {
        _serviceMock.Setup(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAllErrorsAsync(new ErrorViewerQuery(), CancellationToken.None);

        _serviceMock.Verify(s => s.GetErrorsAsync(It.IsAny<ErrorViewerQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
