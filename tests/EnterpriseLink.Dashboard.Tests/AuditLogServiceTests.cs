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
/// Integration-style unit tests for <see cref="AuditLogService"/> using an
/// EF Core InMemory database.
///
/// <para>
/// Because <see cref="DashboardTenantContext"/> has <c>HasTenant = false</c>,
/// the <c>AppDbContext</c> AuditLog query filter evaluates to <c>true</c> for all
/// rows — the service sees cross-tenant data without <c>IgnoreQueryFilters()</c>.
/// Tests verify that optional filters correctly narrow that unfiltered result set.
/// </para>
///
/// <para>Acceptance criterion: <b>UI displays real-time data (Audit Logs module)</b></para>
/// </summary>
public sealed class AuditLogServiceTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly AuditLogService _service;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, new DashboardTenantContext());
        _context.Database.EnsureCreated();

        _service = new AuditLogService(_context, NullLogger<AuditLogService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = new Tenant
        {
            TenantId = Guid.NewGuid(),
            Name = "ALS Test Tenant",
            IndustryType = IndustryType.Financial,
        };
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return tenant;
    }

    private async Task<AuditLog> SeedAuditLogAsync(
        string entityType = "Transaction",
        string action = "Added",
        Guid? tenantId = null,
        DateTimeOffset? occurredAt = null)
    {
        var log = new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            EntityType = entityType,
            EntityId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Action = action,
            OldValues = action == "Modified" ? "{\"Amount\":\"100\"}" : null,
            NewValues = "{\"Amount\":\"200\"}",
            OccurredAt = occurredAt ?? DateTimeOffset.UtcNow,
        };

        // AuditLog does NOT extend AuditableEntity, so no audit trigger fires.
        // AppDbContext.BuildAuditEntries filters on AuditableEntity — AuditLog is immune.
        _context.AuditLogs.Add(log);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return log;
    }

    // ── Basic retrieval ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_returns_all_entries_when_no_filter()
    {
        await SeedAuditLogAsync();
        await SeedAuditLogAsync();
        await SeedAuditLogAsync();

        var result = await _service.GetAuditLogsAsync(new AuditLogQuery());

        result.Items.Should().HaveCount(3);
        result.TotalCount.Should().Be(3);
    }

    [Fact]
    public async Task GetAuditLogsAsync_returns_empty_when_no_entries()
    {
        var result = await _service.GetAuditLogsAsync(new AuditLogQuery());

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAuditLogsAsync_dto_maps_all_fields_correctly()
    {
        var tenantId = Guid.NewGuid();
        var log = await SeedAuditLogAsync("Tenant", "Added", tenantId);

        var result = await _service.GetAuditLogsAsync(new AuditLogQuery());

        var dto = result.Items.Should().ContainSingle().Subject;
        dto.AuditLogId.Should().Be(log.AuditLogId);
        dto.EntityType.Should().Be("Tenant");
        dto.Action.Should().Be("Added");
        dto.TenantId.Should().Be(tenantId);
        dto.OldValues.Should().BeNull();
        dto.NewValues.Should().Be("{\"Amount\":\"200\"}");
    }

    // ── EntityType filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_filters_by_entityType()
    {
        await SeedAuditLogAsync("Transaction");
        await SeedAuditLogAsync("Transaction");
        await SeedAuditLogAsync("Tenant");

        var result = await _service.GetAuditLogsAsync(
            new AuditLogQuery { EntityType = "Transaction" });

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(a => a.EntityType == "Transaction");
    }

    // ── Action filter ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_filters_by_action()
    {
        await SeedAuditLogAsync(action: "Added");
        await SeedAuditLogAsync(action: "Modified");
        await SeedAuditLogAsync(action: "Added");

        var result = await _service.GetAuditLogsAsync(
            new AuditLogQuery { Action = "Added" });

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(a => a.Action == "Added");
    }

    // ── TenantId filter ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_filters_by_tenantId()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await SeedAuditLogAsync(tenantId: tenantA);
        await SeedAuditLogAsync(tenantId: tenantA);
        await SeedAuditLogAsync(tenantId: tenantB);

        var result = await _service.GetAuditLogsAsync(
            new AuditLogQuery { TenantId = tenantA });

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(a => a.TenantId == tenantA);
    }

    // ── Time range filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_filters_by_from()
    {
        var cutoff = DateTimeOffset.UtcNow;
        await SeedAuditLogAsync(occurredAt: cutoff.AddHours(-2));  // before — excluded
        await SeedAuditLogAsync(occurredAt: cutoff.AddHours(1));   // after — included

        var result = await _service.GetAuditLogsAsync(
            new AuditLogQuery { From = cutoff });

        result.Items.Should().ContainSingle();
        result.Items[0].OccurredAt.Should().BeOnOrAfter(cutoff);
    }

    [Fact]
    public async Task GetAuditLogsAsync_filters_by_to()
    {
        var cutoff = DateTimeOffset.UtcNow;
        await SeedAuditLogAsync(occurredAt: cutoff.AddHours(-1));  // before — included
        await SeedAuditLogAsync(occurredAt: cutoff.AddHours(2));   // after — excluded

        var result = await _service.GetAuditLogsAsync(
            new AuditLogQuery { To = cutoff });

        result.Items.Should().ContainSingle();
        result.Items[0].OccurredAt.Should().BeOnOrBefore(cutoff);
    }

    [Fact]
    public async Task GetAuditLogsAsync_filters_by_from_and_to_range()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAuditLogAsync(occurredAt: now.AddDays(-10));  // too old
        await SeedAuditLogAsync(occurredAt: now.AddDays(-3));   // in range
        await SeedAuditLogAsync(occurredAt: now.AddDays(5));    // future

        var result = await _service.GetAuditLogsAsync(new AuditLogQuery
        {
            From = now.AddDays(-7),
            To = now,
        });

        result.Items.Should().ContainSingle();
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_orders_by_occurredAt_descending()
    {
        var now = DateTimeOffset.UtcNow;
        await SeedAuditLogAsync(occurredAt: now.AddSeconds(-30));
        await SeedAuditLogAsync(occurredAt: now);
        await SeedAuditLogAsync(occurredAt: now.AddSeconds(-15));

        var result = await _service.GetAuditLogsAsync(new AuditLogQuery());

        result.Items.Should().BeInDescendingOrder(a => a.OccurredAt);
    }

    // ── Pagination ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAuditLogsAsync_paginates_correctly()
    {
        for (var i = 0; i < 7; i++)
            await SeedAuditLogAsync();

        var page1 = await _service.GetAuditLogsAsync(new AuditLogQuery { Page = 1, PageSize = 3 });
        var page2 = await _service.GetAuditLogsAsync(new AuditLogQuery { Page = 2, PageSize = 3 });
        var page3 = await _service.GetAuditLogsAsync(new AuditLogQuery { Page = 3, PageSize = 3 });

        page1.Items.Should().HaveCount(3);
        page2.Items.Should().HaveCount(3);
        page3.Items.Should().HaveCount(1);
        page1.TotalCount.Should().Be(7);
        page1.TotalPages.Should().Be(3);
        page1.HasPreviousPage.Should().BeFalse();
        page2.HasPreviousPage.Should().BeTrue();
        page3.HasNextPage.Should().BeFalse();
    }
}
