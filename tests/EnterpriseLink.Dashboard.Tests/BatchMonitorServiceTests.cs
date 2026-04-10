using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Dashboard.Services;
using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Dashboard.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EnterpriseLink.Dashboard.Tests;

/// <summary>
/// Integration-style unit tests for <see cref="BatchMonitorService"/> using an
/// EF Core InMemory database.
///
/// <para>
/// Each test creates a uniquely named InMemory database to guarantee full isolation.
/// The <see cref="DashboardTenantContext"/> (HasTenant=false) is used exactly as it
/// would be in production, verifying that <c>IgnoreQueryFilters()</c> correctly
/// bypasses the soft-delete filter on <c>ProcessedUploads</c>.
/// </para>
/// </summary>
public sealed class BatchMonitorServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly BatchMonitorService _service;

    public BatchMonitorServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, new DashboardTenantContext());
        _context.Database.EnsureCreated();

        _service = new BatchMonitorService(_context, NullLogger<BatchMonitorService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = "Test Tenant",
            IndustryType = IndustryType.Financial,
        };
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return tenant;
    }

    private async Task<ProcessedUpload> SeedUploadAsync(
        Guid tenantId,
        UploadProcessingStatus status = UploadProcessingStatus.Completed,
        int rowsInserted = 500,
        string sourceSystem = "TestSystem")
    {
        var upload = new ProcessedUpload
        {
            UploadId = Guid.NewGuid(),
            TenantId = tenantId,
            Status = status,
            RowsInserted = rowsInserted,
            SourceSystem = sourceSystem,
        };
        _context.ProcessedUploads.Add(upload);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return upload;
    }

    // ── GetUploadsAsync — basic retrieval ─────────────────────────────────────

    [Fact]
    public async Task GetUploadsAsync_returns_all_uploads_when_no_filter()
    {
        var tenant = await SeedTenantAsync();
        await SeedUploadAsync(tenant.TenantId);
        await SeedUploadAsync(tenant.TenantId);

        var result = await _service.GetUploadsAsync(new BatchMonitorQuery());

        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetUploadsAsync_returns_empty_when_no_uploads()
    {
        var result = await _service.GetUploadsAsync(new BatchMonitorQuery());

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetUploadsAsync_dto_maps_all_fields_correctly()
    {
        var tenant = await SeedTenantAsync();
        var upload = await SeedUploadAsync(
            tenant.TenantId, UploadProcessingStatus.Completed, 999, "BankSystem");

        var result = await _service.GetUploadsAsync(new BatchMonitorQuery());

        var dto = result.Items.Should().ContainSingle().Subject;
        dto.UploadId.Should().Be(upload.UploadId);
        dto.TenantId.Should().Be(upload.TenantId);
        dto.Status.Should().Be("Completed");
        dto.RowsInserted.Should().Be(999);
        dto.SourceSystem.Should().Be("BankSystem");
    }

    // ── GetUploadsAsync — status filter ───────────────────────────────────────

    [Fact]
    public async Task GetUploadsAsync_filters_by_status()
    {
        var tenant = await SeedTenantAsync();
        await SeedUploadAsync(tenant.TenantId, UploadProcessingStatus.Completed);
        await SeedUploadAsync(tenant.TenantId, UploadProcessingStatus.Failed);
        await SeedUploadAsync(tenant.TenantId, UploadProcessingStatus.Processing);

        var result = await _service.GetUploadsAsync(new BatchMonitorQuery { Status = "Completed" });

        result.Items.Should().ContainSingle(u => u.Status == "Completed");
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetUploadsAsync_ignores_invalid_status_string_returns_all()
    {
        var tenant = await SeedTenantAsync();
        await SeedUploadAsync(tenant.TenantId, UploadProcessingStatus.Completed);

        // An unrecognised status value cannot be parsed → no filter applied → returns all.
        var result = await _service.GetUploadsAsync(new BatchMonitorQuery { Status = "NotAStatus" });

        result.Items.Should().HaveCount(1);
    }

    // ── GetUploadsAsync — tenant filter ───────────────────────────────────────

    [Fact]
    public async Task GetUploadsAsync_filters_by_tenantId()
    {
        var tenantA = await SeedTenantAsync();
        var tenantB = await SeedTenantAsync();
        await SeedUploadAsync(tenantA.TenantId);
        await SeedUploadAsync(tenantA.TenantId);
        await SeedUploadAsync(tenantB.TenantId);

        var result = await _service.GetUploadsAsync(
            new BatchMonitorQuery { TenantId = tenantA.TenantId });

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(u => u.TenantId == tenantA.TenantId);
    }

    // ── GetUploadsAsync — pagination ──────────────────────────────────────────

    [Fact]
    public async Task GetUploadsAsync_paginates_correctly()
    {
        var tenant = await SeedTenantAsync();
        for (var i = 0; i < 5; i++)
            await SeedUploadAsync(tenant.TenantId);

        var page1 = await _service.GetUploadsAsync(new BatchMonitorQuery { Page = 1, PageSize = 2 });
        var page2 = await _service.GetUploadsAsync(new BatchMonitorQuery { Page = 2, PageSize = 2 });
        var page3 = await _service.GetUploadsAsync(new BatchMonitorQuery { Page = 3, PageSize = 2 });

        page1.Items.Should().HaveCount(2);
        page2.Items.Should().HaveCount(2);
        page3.Items.Should().HaveCount(1);
        page1.TotalCount.Should().Be(5);
        page1.TotalPages.Should().Be(3);
    }

    [Fact]
    public async Task GetUploadsAsync_orders_by_createdAt_descending()
    {
        var tenant = await SeedTenantAsync();
        await SeedUploadAsync(tenant.TenantId);
        await SeedUploadAsync(tenant.TenantId);

        var result = await _service.GetUploadsAsync(new BatchMonitorQuery());

        result.Items.Should().BeInDescendingOrder(u => u.CreatedAt);
    }

    // ── GetUploadByIdAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task GetUploadByIdAsync_returns_dto_when_found()
    {
        var tenant = await SeedTenantAsync();
        var upload = await SeedUploadAsync(tenant.TenantId);

        var dto = await _service.GetUploadByIdAsync(upload.UploadId);

        dto.Should().NotBeNull();
        dto!.UploadId.Should().Be(upload.UploadId);
    }

    [Fact]
    public async Task GetUploadByIdAsync_returns_null_when_not_found()
    {
        var dto = await _service.GetUploadByIdAsync(Guid.NewGuid());

        dto.Should().BeNull();
    }
}
