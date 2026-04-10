using EnterpriseLink.Dashboard.MultiTenancy;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Dashboard.Services;
using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Dashboard.Tests;

/// <summary>
/// Integration-style unit tests for <see cref="ErrorViewerService"/> using an
/// EF Core InMemory database.
///
/// <para>
/// Verifies cross-tenant visibility (IgnoreQueryFilters), optional filtering by
/// upload, tenant and failure reason, and correct row-number ordering.
/// </para>
///
/// <para>Acceptance criterion: <b>Validation errors queryable</b></para>
/// </summary>
public sealed class ErrorViewerServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly ErrorViewerService _service;

    public ErrorViewerServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, new DashboardTenantContext());
        _context.Database.EnsureCreated();

        _service = new ErrorViewerService(_context, NullLogger<ErrorViewerService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = "EV Test Tenant",
            IndustryType = IndustryType.Financial,
        };
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return tenant;
    }

    private async Task<InvalidTransaction> SeedErrorAsync(
        Guid tenantId,
        Guid? uploadId = null,
        int rowNumber = 1,
        string failureReason = "Schema")
    {
        var error = new InvalidTransaction
        {
            InvalidTransactionId = Guid.NewGuid(),
            UploadId = uploadId ?? Guid.NewGuid(),
            TenantId = tenantId,
            RowNumber = rowNumber,
            RawData = "{\"Amount\":\"bad\"}",
            ValidationErrors = "[\"[RequiredFieldMissing] Amount: invalid\"]",
            FailureReason = failureReason,
        };

        // InvalidTransaction has a tenant query filter; bypass by using the context
        // directly with IgnoreQueryFilters not available at save time — add the entity
        // and save without the tenant check via direct Add (context tenant is Guid.Empty).
        // Since DashboardTenantContext has HasTenant=false, ApplyTenantId is skipped,
        // so we must set TenantId manually (which we did above).
        _context.InvalidTransactions.Add(error);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return error;
    }

    // ── Basic retrieval ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorsAsync_returns_all_errors_when_no_filter()
    {
        var tenant = await SeedTenantAsync();
        await SeedErrorAsync(tenant.TenantId, rowNumber: 1);
        await SeedErrorAsync(tenant.TenantId, rowNumber: 2);

        var result = await _service.GetErrorsAsync(new ErrorViewerQuery());

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetErrorsAsync_returns_empty_when_no_errors()
    {
        var result = await _service.GetErrorsAsync(new ErrorViewerQuery());

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetErrorsAsync_dto_maps_all_fields_correctly()
    {
        var tenant = await SeedTenantAsync();
        var uploadId = Guid.NewGuid();
        var error = await SeedErrorAsync(
            tenant.TenantId, uploadId, rowNumber: 7, failureReason: "BusinessRule");

        var result = await _service.GetErrorsAsync(new ErrorViewerQuery());

        var dto = result.Items.Should().ContainSingle().Subject;
        dto.InvalidTransactionId.Should().Be(error.InvalidTransactionId);
        dto.UploadId.Should().Be(uploadId);
        dto.TenantId.Should().Be(tenant.TenantId);
        dto.RowNumber.Should().Be(7);
        dto.FailureReason.Should().Be("BusinessRule");
    }

    // ── UploadId filter ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorsAsync_filters_by_uploadId()
    {
        var tenant = await SeedTenantAsync();
        var targetUpload = Guid.NewGuid();
        await SeedErrorAsync(tenant.TenantId, targetUpload, rowNumber: 1);
        await SeedErrorAsync(tenant.TenantId, Guid.NewGuid(), rowNumber: 2);

        var result = await _service.GetErrorsAsync(
            new ErrorViewerQuery { UploadId = targetUpload });

        result.Items.Should().ContainSingle(e => e.UploadId == targetUpload);
        result.TotalCount.Should().Be(1);
    }

    // ── TenantId filter ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorsAsync_filters_by_tenantId()
    {
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        await SeedErrorAsync(tenantA.TenantId, rowNumber: 1);
        await SeedErrorAsync(tenantB.TenantId, rowNumber: 1);

        var result = await _service.GetErrorsAsync(
            new ErrorViewerQuery { TenantId = tenantA.TenantId });

        result.Items.Should().ContainSingle(e => e.TenantId == tenantA.TenantId);
    }

    // ── FailureReason filter ──────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorsAsync_filters_by_failureReason()
    {
        var tenant = await SeedTenantAsync();
        await SeedErrorAsync(tenant.TenantId, failureReason: "Schema");
        await SeedErrorAsync(tenant.TenantId, failureReason: "BusinessRule");
        await SeedErrorAsync(tenant.TenantId, failureReason: "Duplicate");

        var result = await _service.GetErrorsAsync(
            new ErrorViewerQuery { FailureReason = "BusinessRule" });

        result.Items.Should().ContainSingle(e => e.FailureReason == "BusinessRule");
        result.TotalCount.Should().Be(1);
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetErrorsAsync_paginates_correctly()
    {
        var tenant = await SeedTenantAsync();
        var uploadId = Guid.NewGuid();
        for (var i = 1; i <= 5; i++)
            await SeedErrorAsync(tenant.TenantId, uploadId, rowNumber: i);

        var page1 = await _service.GetErrorsAsync(new ErrorViewerQuery { Page = 1, PageSize = 2 });
        var page2 = await _service.GetErrorsAsync(new ErrorViewerQuery { Page = 2, PageSize = 2 });

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetErrorsAsync_orders_by_rowNumber_ascending_within_upload()
    {
        var tenant = await SeedTenantAsync();
        var uploadId = Guid.NewGuid();
        await SeedErrorAsync(tenant.TenantId, uploadId, rowNumber: 5);
        await SeedErrorAsync(tenant.TenantId, uploadId, rowNumber: 1);
        await SeedErrorAsync(tenant.TenantId, uploadId, rowNumber: 3);

        var result = await _service.GetErrorsAsync(
            new ErrorViewerQuery { UploadId = uploadId });

        result.Items.Select(e => e.RowNumber).Should().BeInAscendingOrder();
    }

    // ── Cross-tenant visibility ───────────────────────────────────────────────

    [Fact]
    public async Task GetErrorsAsync_returns_errors_from_multiple_tenants()
    {
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        await SeedErrorAsync(tenantA.TenantId);
        await SeedErrorAsync(tenantB.TenantId);

        var result = await _service.GetErrorsAsync(new ErrorViewerQuery());

        result.Items.Should().HaveCount(2, "Dashboard must show errors from all tenants");
        result.Items.Select(e => e.TenantId).Distinct().Should().HaveCount(2);
    }
}
