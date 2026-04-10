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
/// Unit tests for <see cref="AuditLogController"/>.
///
/// <para>
/// Verifies that the controller correctly delegates to <see cref="IAuditLogService"/>,
/// passes all filter parameters, and returns properly shaped HTTP responses.
/// </para>
///
/// <para>Acceptance criterion: <b>UI displays real-time data (Audit Logs module)</b></para>
/// </summary>
public sealed class AuditLogControllerTests
{
    private readonly Mock<IAuditLogService> _serviceMock = new();
    private readonly AuditLogController _controller;

    public AuditLogControllerTests()
    {
        _controller = new AuditLogController(
            _serviceMock.Object,
            NullLogger<AuditLogController>.Instance);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AuditLogDto MakeDto(string entityType = "Transaction", string action = "Added") =>
        new(
            AuditLogId: Guid.NewGuid(),
            EntityType: entityType,
            EntityId: Guid.NewGuid().ToString(),
            TenantId: Guid.NewGuid(),
            Action: action,
            OldValues: null,
            NewValues: "{\"Amount\":\"100\"}",
            OccurredAt: DateTimeOffset.UtcNow);

    private static PagedResult<AuditLogDto> MakePage(int count = 2)
    {
        var items = Enumerable.Range(0, count).Select(_ => MakeDto()).ToList();
        return new PagedResult<AuditLogDto>(items, count, 1, 20);
    }

    // ── GET /api/audit-logs — happy path ──────────────────────────────────────

    [Fact]
    public async Task GetAuditLogs_returns_200_with_paged_result()
    {
        var page = MakePage();
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(page);

        var result = await _controller.GetAuditLogsAsync(new AuditLogQuery(), CancellationToken.None);

        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        ok.StatusCode.Should().Be(StatusCodes.Status200OK);
        ok.Value.Should().BeSameAs(page);
    }

    [Fact]
    public async Task GetAuditLogs_passes_entityType_filter_to_service()
    {
        var query = new AuditLogQuery { EntityType = "Tenant" };
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(
            It.Is<AuditLogQuery>(q => q.EntityType == "Tenant"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_passes_action_filter_to_service()
    {
        var query = new AuditLogQuery { Action = "Modified" };
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(
            It.Is<AuditLogQuery>(q => q.Action == "Modified"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_passes_tenant_filter_to_service()
    {
        var tenantId = Guid.NewGuid();
        var query = new AuditLogQuery { TenantId = tenantId };
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(
            It.Is<AuditLogQuery>(q => q.TenantId == tenantId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_passes_time_range_filters_to_service()
    {
        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;
        var query = new AuditLogQuery { From = from, To = to };
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(
            It.Is<AuditLogQuery>(q => q.From == from && q.To == to),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_passes_pagination_to_service()
    {
        var query = new AuditLogQuery { Page = 5, PageSize = 50 };
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(query, CancellationToken.None);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(
            It.Is<AuditLogQuery>(q => q.Page == 5 && q.PageSize == 50),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_returns_empty_page_when_no_entries()
    {
        var emptyPage = new PagedResult<AuditLogDto>(Array.Empty<AuditLogDto>(), 0, 1, 20);
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(emptyPage);

        var result = await _controller.GetAuditLogsAsync(new AuditLogQuery(), CancellationToken.None);

        var payload = ((OkObjectResult)result).Value
            .Should().BeOfType<PagedResult<AuditLogDto>>().Subject;
        payload.Items.Should().BeEmpty();
        payload.TotalCount.Should().Be(0);
        payload.TotalPages.Should().Be(0);
    }

    [Fact]
    public async Task GetAuditLogs_forwards_cancellation_token_to_service()
    {
        using var cts = new CancellationTokenSource();
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), cts.Token))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(new AuditLogQuery(), cts.Token);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), cts.Token), Times.Once);
    }

    [Fact]
    public async Task GetAuditLogs_calls_service_exactly_once_per_request()
    {
        _serviceMock.Setup(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(MakePage());

        await _controller.GetAuditLogsAsync(new AuditLogQuery(), CancellationToken.None);

        _serviceMock.Verify(s => s.GetAuditLogsAsync(It.IsAny<AuditLogQuery>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
