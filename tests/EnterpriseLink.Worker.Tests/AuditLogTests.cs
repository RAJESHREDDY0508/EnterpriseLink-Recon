using System.Text.Json;
using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.MultiTenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for <c>AppDbContext.BuildAuditEntries</c>.
///
/// <para>Acceptance criterion: <b>Before/after state stored</b></para>
///
/// <para>
/// Each test verifies that saving a change to an <c>AuditableEntity</c> atomically
/// writes an <c>AuditLog</c> row capturing the entity type, action, and the
/// property values before and after the change.
/// </para>
/// </summary>
public sealed class AuditLogTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly WorkerTenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();

    public AuditLogTests()
    {
        _tenantContext = new WorkerTenantContext { TenantId = _tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, _tenantContext);
    }

    public void Dispose() => _context.Dispose();

    // ── Helper: insert a Tenant first (needed as FK for Transaction) ──────────

    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = new Tenant
        {
            TenantId = _tenantId,
            Name = "Audit-Test Tenant",
            IndustryType = IndustryType.Financial,
        };

        // Bypass global query filter for the FK
        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return tenant;
    }

    private Transaction MakeTransaction() => new()
    {
        Amount = 100m,
        Status = TransactionStatus.Pending,
        ExternalReferenceId = "REF-AUDIT-001",
    };

    // ── Insert produces an AuditLog entry ────────────────────────────────────

    [Fact]
    public async Task Insert_entity_creates_audit_log_with_Added_action()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();

        var logs = await _context.AuditLogs
            .IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Transaction))
            .ToListAsync();

        logs.Should().Contain(a => a.Action == "Added",
            "inserting a Transaction must produce an AuditLog with Action=Added");
    }

    [Fact]
    public async Task Insert_audit_log_has_null_OldValues()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();

        var log = await _context.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EntityType == nameof(Transaction) && a.Action == "Added");

        log.OldValues.Should().BeNull("there is no 'before' state for an insert");
    }

    [Fact]
    public async Task Insert_audit_log_NewValues_contains_Amount()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();

        var log = await _context.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EntityType == nameof(Transaction) && a.Action == "Added");

        log.NewValues.Should().NotBeNull();
        var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(log.NewValues!);
        dict.Should().ContainKey("Amount");
        dict!["Amount"].Should().Be("100");
    }

    // ── Update produces an AuditLog entry ────────────────────────────────────

    [Fact]
    public async Task Update_entity_creates_audit_log_with_Modified_action()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.Amount = 999m;
        await _context.SaveChangesAsync();

        var modifiedLog = await _context.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Transaction) && a.Action == "Modified")
            .FirstOrDefaultAsync();

        modifiedLog.Should().NotBeNull("updating a Transaction must produce an AuditLog with Action=Modified");
    }

    [Fact]
    public async Task Update_audit_log_OldValues_contains_previous_Amount()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.Amount = 999m;
        await _context.SaveChangesAsync();

        var modifiedLog = await _context.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Transaction) && a.Action == "Modified")
            .FirstAsync();

        var oldDict = JsonSerializer.Deserialize<Dictionary<string, string?>>(modifiedLog.OldValues!);
        oldDict!["Amount"].Should().Be("100", "OldValues must capture the value before the update");
    }

    [Fact]
    public async Task Update_audit_log_NewValues_contains_updated_Amount()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.Amount = 999m;
        await _context.SaveChangesAsync();

        var modifiedLog = await _context.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Transaction) && a.Action == "Modified")
            .FirstAsync();

        var newDict = JsonSerializer.Deserialize<Dictionary<string, string?>>(modifiedLog.NewValues!);
        newDict!["Amount"].Should().Be("999", "NewValues must capture the value after the update");
    }

    // ── Soft-delete appears as Modified ──────────────────────────────────────

    [Fact]
    public async Task Soft_delete_creates_audit_log_with_Modified_action()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        _context.Transactions.Remove(tx);   // InterceptSoftDeletes converts to Modified
        await _context.SaveChangesAsync();

        var softDeleteLog = await _context.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Transaction) && a.Action == "Modified")
            .FirstOrDefaultAsync();

        softDeleteLog.Should().NotBeNull(
            "soft deletes appear as Modified — the audit log must capture this");
    }

    [Fact]
    public async Task Soft_delete_NewValues_shows_IsDeleted_true()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        _context.Transactions.Remove(tx);
        await _context.SaveChangesAsync();

        var softDeleteLog = await _context.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.EntityType == nameof(Transaction) && a.Action == "Modified")
            .FirstAsync();

        var newDict = JsonSerializer.Deserialize<Dictionary<string, string?>>(softDeleteLog.NewValues!);
        newDict!["IsDeleted"].Should().Be("True",
            "soft delete sets IsDeleted=true — NewValues must reflect this");
    }

    // ── TenantId and EntityType ───────────────────────────────────────────────

    [Fact]
    public async Task Audit_log_EntityType_matches_entity_class_name()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();

        var log = await _context.AuditLogs.IgnoreQueryFilters()
            .FirstAsync(a => a.Action == "Added" && a.EntityType == "Transaction");

        log.EntityType.Should().Be("Transaction");
    }

    [Fact]
    public async Task Audit_log_TenantId_is_set_from_tenant_context()
    {
        await SeedTenantAsync();

        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();

        var log = await _context.AuditLogs.IgnoreQueryFilters()
            .FirstAsync(a => a.Action == "Added" && a.EntityType == "Transaction");

        log.TenantId.Should().Be(_tenantId,
            "audit log TenantId must be set from the scoped ITenantContext");
    }

    // ── OccurredAt ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Audit_log_OccurredAt_is_close_to_now()
    {
        await SeedTenantAsync();

        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        _context.Transactions.Add(MakeTransaction());
        await _context.SaveChangesAsync();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var log = await _context.AuditLogs.IgnoreQueryFilters()
            .FirstAsync(a => a.Action == "Added" && a.EntityType == "Transaction");

        log.OccurredAt.Should().BeOnOrAfter(before)
            .And.BeOnOrBefore(after,
                "OccurredAt must be stamped with the UTC time of the SaveChanges call");
    }
}
