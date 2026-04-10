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
/// Unit tests for <see cref="BatchMonitorController"/>.
///
/// <para>
/// The service layer is mocked via <see cref="IBatchMonitorService"/> so tests
/// focus exclusively on controller routing, response shaping, and HTTP status codes.
/// </para>
///
/// <para>Acceptance criterion: <b>Batch status exposed</b></para>
/// </summary>
public sealed class BatchMonitorControllerTests
{
    private readonly Mock<IBatchMonitorService> _serviceMock = new();
    private readonly BatchMonitorController _controller;

    public BatchMonitorControllerTests()
    {
        _controller = new BatchMonitorController(
            _serviceMock.Object,
            NullLogger<BatchMonitorController>.Instance);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ProcessedUploadSummaryDto MakeDto(string status = "Completed") =>
        new(
            UploadId: Guid.NewGuid(),
            TenantId: Guid.NewGuid(),
            Status: status,
            RowsInserted: 1000,
            SourceSystem: "TestSystem",
            CreatedAt: DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt: DateTimeOffset.UtcNow);

    private static PagedResult<ProcessedUploadSummaryDto> MakePage(
        int count = 2, int total = 2, int page = 1, int pageSize = 20)
    {
        var items = Enumerable.Range(0, count).Select(_ => MakeDto()).ToList();
        return new PagedResult<ProcessedUploadSummaryDto>(items, total, page, pageSize);
    }

    // ── GET /api/uploads — happy path ─────────────────────────────────────────

    [Fact]
    public async Task GetUploads_returns_200_with_paged_result()
    {
        var page = MakePage();
        _serviceMock.Setup(s => s.GetUploadsAsync(It.IsAny<BatchMonitorQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(page);

        var result = await _controller.GetUploadsAsync(new BatchMonitorQuery(), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetUploads_passes_query_to_service()
    {
        var query = new BatchMonitorQuery { Page = 2, PageSize = 10, Status = "Completed" };
        _serviceMock.Setup(s => s.GetUploadsAsync(It.IsAny<BatchMonitorQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetUploadsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetUploadsAsync(
            It.Is<BatchMonitorQuery>(q => q.Page == 2 && q.PageSize == 10 && q.Status == "Completed"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetUploads_returns_empty_page_when_no_records()
    {
        var emptyPage = new PagedResult<ProcessedUploadSummaryDto>(
            Array.Empty<ProcessedUploadSummaryDto>(), 0, 1, 20);
        _serviceMock.Setup(s => s.GetUploadsAsync(It.IsAny<BatchMonitorQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(emptyPage);

        var result = await _controller.GetUploadsAsync(new BatchMonitorQuery(), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<PagedResult<ProcessedUploadSummaryDto>>().Subject;
        payload.Items.Should().BeEmpty();
        payload.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUploads_forwards_cancellation_token_to_service()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _serviceMock.Setup(s => s.GetUploadsAsync(It.IsAny<BatchMonitorQuery>(), token))
                    .ReturnsAsync(MakePage());

        await _controller.GetUploadsAsync(new BatchMonitorQuery(), token);

        _serviceMock.Verify(s => s.GetUploadsAsync(It.IsAny<BatchMonitorQuery>(), token), Times.Once);
    }

    // ── GET /api/uploads/{uploadId} — found ───────────────────────────────────

    [Fact]
    public async Task GetUploadById_returns_200_with_dto_when_found()
    {
        var dto = MakeDto();
        _serviceMock.Setup(s => s.GetUploadByIdAsync(dto.UploadId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(dto);

        var result = await _controller.GetUploadByIdAsync(dto.UploadId, CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeSameAs(dto);
    }

    [Fact]
    public async Task GetUploadById_passes_correct_uploadId_to_service()
    {
        var uploadId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetUploadByIdAsync(uploadId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakeDto());

        await _controller.GetUploadByIdAsync(uploadId, CancellationToken.None);

        _serviceMock.Verify(s => s.GetUploadByIdAsync(uploadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GET /api/uploads/{uploadId} — not found ───────────────────────────────

    [Fact]
    public async Task GetUploadById_returns_404_when_not_found()
    {
        var uploadId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetUploadByIdAsync(uploadId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((ProcessedUploadSummaryDto?)null);

        var result = await _controller.GetUploadByIdAsync(uploadId, CancellationToken.None);

        result.Should().BeOfType<NotFoundObjectResult>()
              .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task GetUploadById_404_body_contains_upload_id()
    {
        var uploadId = Guid.NewGuid();
        _serviceMock.Setup(s => s.GetUploadByIdAsync(uploadId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync((ProcessedUploadSummaryDto?)null);

        var result = await _controller.GetUploadByIdAsync(uploadId, CancellationToken.None);

        var body = result.Should().BeOfType<NotFoundObjectResult>().Subject.Value?.ToString();
        body.Should().Contain(uploadId.ToString(),
            "the 404 body must include the missing upload ID to aid client diagnostics");
    }

    // ── Pagination metadata ───────────────────────────────────────────────────

    [Fact]
    public async Task GetUploads_paged_result_computes_total_pages_correctly()
    {
        var page = new PagedResult<ProcessedUploadSummaryDto>(
            Enumerable.Range(0, 20).Select(_ => MakeDto()).ToList(),
            totalCount: 45,
            page: 1,
            pageSize: 20);
        _serviceMock.Setup(s => s.GetUploadsAsync(It.IsAny<BatchMonitorQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(page);

        var result = await _controller.GetUploadsAsync(new BatchMonitorQuery(), CancellationToken.None);

        var payload = ((OkObjectResult)result).Value
            .Should().BeOfType<PagedResult<ProcessedUploadSummaryDto>>().Subject;
        payload.TotalPages.Should().Be(3, "ceil(45/20) = 3");
        payload.HasNextPage.Should().BeTrue();
        payload.HasPreviousPage.Should().BeFalse();
    }
}
