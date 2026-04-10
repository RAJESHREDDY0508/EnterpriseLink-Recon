using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Batch;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.MultiTenancy;
using EnterpriseLink.Worker.Parsing;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Tests;

/// <summary>
/// Unit tests for data lineage tracking on <see cref="Transaction"/> rows
/// persisted by <see cref="TransactionBatchInserter"/>.
///
/// <para>Acceptance criterion: <b>Data lineage tracked</b></para>
///
/// <para>
/// Each test verifies that <c>TransactionBatchInserter</c> correctly stamps
/// <c>UploadId</c> and <c>SourceSystem</c> on every <c>Transaction</c> row,
/// enabling a full trace from any row back to its originating file upload and
/// source system.
/// </para>
/// </summary>
public sealed class TransactionLineageTests : IDisposable
{
    private readonly AppDbContext _context;
    private readonly WorkerTenantContext _tenantContext;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _uploadId = Guid.NewGuid();
    private const string SourceSystem = "BankSystemA";

    public TransactionLineageTests()
    {
        _tenantContext = new WorkerTenantContext { TenantId = _tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AppDbContext(options, _tenantContext);
        _context.Database.EnsureCreated();
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TransactionBatchInserter BuildInserter(int batchSize = 100) =>
        new(_context,
            Options.Create(new BatchInsertOptions { BatchSize = batchSize }),
            NullLogger<TransactionBatchInserter>.Instance);

    private static async IAsyncEnumerable<ParsedRow> MakeRowsAsync(int count,
        string amount = "250.00",
        string? referenceId = null)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new ParsedRow(
                RowNumber: i + 1,
                Fields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Amount"] = amount,
                    ["ExternalReferenceId"] = referenceId ?? $"REF-LINEAGE-{i:D4}",
                });
        }

        await Task.CompletedTask;
    }

    private async Task SeedTenantAsync()
    {
        _context.Tenants.Add(new Tenant
        {
            TenantId = _tenantId,
            Name = "Lineage-Test Tenant",
            IndustryType = IndustryType.Financial,
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    // ── UploadId is stamped on every row ─────────────────────────────────────

    [Fact]
    public async Task InsertAsync_stamps_UploadId_on_every_transaction()
    {
        await SeedTenantAsync();
        var inserter = BuildInserter();

        await inserter.InsertAsync(MakeRowsAsync(5), _tenantId, _uploadId, SourceSystem);

        var transactions = await _context.Transactions
            .IgnoreQueryFilters()
            .ToListAsync();

        transactions.Should().HaveCount(5);
        transactions.Should().OnlyContain(t => t.UploadId == _uploadId,
            "every Transaction row must carry the UploadId of the file that produced it");
    }

    [Fact]
    public async Task InsertAsync_stamps_UploadId_across_multiple_batches()
    {
        await SeedTenantAsync();
        var inserter = BuildInserter(batchSize: 3);   // force 2 batches for 5 rows

        await inserter.InsertAsync(MakeRowsAsync(5), _tenantId, _uploadId, SourceSystem);

        var transactions = await _context.Transactions
            .IgnoreQueryFilters()
            .ToListAsync();

        transactions.Should().HaveCount(5, "all rows across batches must be persisted");
        transactions.Should().OnlyContain(t => t.UploadId == _uploadId,
            "UploadId must be consistent across all committed batches");
    }

    // ── SourceSystem is stamped on every row ─────────────────────────────────

    [Fact]
    public async Task InsertAsync_stamps_SourceSystem_on_every_transaction()
    {
        await SeedTenantAsync();
        var inserter = BuildInserter();

        await inserter.InsertAsync(MakeRowsAsync(5), _tenantId, _uploadId, SourceSystem);

        var transactions = await _context.Transactions
            .IgnoreQueryFilters()
            .ToListAsync();

        transactions.Should().OnlyContain(t => t.SourceSystem == SourceSystem,
            "every Transaction row must carry the SourceSystem identifier for traceability");
    }

    [Fact]
    public async Task InsertAsync_stamps_SourceSystem_across_multiple_batches()
    {
        await SeedTenantAsync();
        var inserter = BuildInserter(batchSize: 2);   // force 3 batches for 5 rows

        await inserter.InsertAsync(MakeRowsAsync(5), _tenantId, _uploadId, SourceSystem);

        var transactions = await _context.Transactions
            .IgnoreQueryFilters()
            .ToListAsync();

        transactions.Should().OnlyContain(t => t.SourceSystem == SourceSystem,
            "SourceSystem must be consistent across all committed batches");
    }

    // ── Different uploads produce distinct lineage ────────────────────────────

    [Fact]
    public async Task Transactions_from_different_uploads_have_distinct_UploadIds()
    {
        await SeedTenantAsync();

        var uploaderA = BuildInserter();
        var uploadIdA = Guid.NewGuid();
        var uploadIdB = Guid.NewGuid();

        // First upload
        await uploaderA.InsertAsync(MakeRowsAsync(2, referenceId: "REF-A-{0:D4}"), _tenantId, uploadIdA, "SystemA");
        _context.ChangeTracker.Clear();

        // Second upload (different uploadId and sourceSystem)
        await uploaderA.InsertAsync(MakeRowsAsync(2, referenceId: "REF-B-{0:D4}"), _tenantId, uploadIdB, "SystemB");

        var txA = await _context.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.UploadId == uploadIdA)
            .ToListAsync();

        var txB = await _context.Transactions
            .IgnoreQueryFilters()
            .Where(t => t.UploadId == uploadIdB)
            .ToListAsync();

        txA.Should().HaveCount(2, "first upload must produce exactly 2 rows");
        txB.Should().HaveCount(2, "second upload must produce exactly 2 rows");
        txA.Should().OnlyContain(t => t.SourceSystem == "SystemA");
        txB.Should().OnlyContain(t => t.SourceSystem == "SystemB");
    }

    // ── UploadId / SourceSystem are independent of TenantId injection ─────────

    [Fact]
    public async Task InsertAsync_lineage_fields_do_not_override_TenantId_injection()
    {
        await SeedTenantAsync();
        var inserter = BuildInserter();

        await inserter.InsertAsync(MakeRowsAsync(3), _tenantId, _uploadId, SourceSystem);

        var transactions = await _context.Transactions
            .IgnoreQueryFilters()
            .ToListAsync();

        transactions.Should().OnlyContain(t => t.TenantId == _tenantId,
            "TenantId must still be auto-injected by AppDbContext regardless of lineage fields");

        transactions.Should().OnlyContain(t => t.UploadId == _uploadId && t.SourceSystem == SourceSystem,
            "lineage fields must coexist with TenantId without overwriting each other");
    }

    // ── Single-row edge case ──────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_single_row_has_correct_lineage()
    {
        await SeedTenantAsync();
        var inserter = BuildInserter();

        var inserted = await inserter.InsertAsync(MakeRowsAsync(1), _tenantId, _uploadId, SourceSystem);

        inserted.Should().Be(1);

        var tx = await _context.Transactions
            .IgnoreQueryFilters()
            .SingleAsync();

        tx.UploadId.Should().Be(_uploadId, "single-row insert must stamp UploadId");
        tx.SourceSystem.Should().Be(SourceSystem, "single-row insert must stamp SourceSystem");
    }
}
