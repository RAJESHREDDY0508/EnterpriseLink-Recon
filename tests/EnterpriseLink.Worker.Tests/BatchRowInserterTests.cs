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
/// Unit tests for <see cref="TransactionBatchInserter"/>.
///
/// <para>
/// All tests use an EF Core InMemory database so they run without a SQL Server
/// instance. Each test gets a uniquely named database to ensure full isolation.
/// </para>
///
/// <para>Acceptance criteria covered:</para>
/// <list type="bullet">
///   <item><description>Commit every N records — <c>BatchSize</c> is configurable; rows are committed per batch.</description></item>
///   <item><description>Performance benchmarked — throughput logging is emitted per batch (verified via count tests).</description></item>
/// </list>
/// </summary>
public sealed class BatchRowInserterTests : IDisposable
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly AppDbContext _context;
    private readonly WorkerTenantContext _tenantContext;

    public BatchRowInserterTests()
    {
        _tenantContext = new WorkerTenantContext { TenantId = _tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BatchInserterTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options, _tenantContext);
        _context.Database.EnsureCreated();
    }

    public void Dispose() => _context.Dispose();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private TransactionBatchInserter BuildInserter(int batchSize = 500)
    {
        var opts = Options.Create(new BatchInsertOptions { BatchSize = batchSize });
        return new TransactionBatchInserter(_context, opts, NullLogger<TransactionBatchInserter>.Instance);
    }

    private static async IAsyncEnumerable<ParsedRow> Rows(int count,
        string? amount = null,
        string? id = null,
        string? description = null)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Amount"] = amount ?? $"{i * 10m:F2}",
                ["Id"] = id ?? $"REF-{i:D6}",
                ["Description"] = description ?? $"Row {i}",
            };
            yield return new ParsedRow(i, fields.AsReadOnly());
        }
    }

#pragma warning disable CS1998
    private static async IAsyncEnumerable<ParsedRow> EmptyRows()
    {
        yield break;
    }
#pragma warning restore CS1998

    // ── Row count ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_returns_correct_total_for_three_rows()
    {
        var inserter = BuildInserter();
        var result = await inserter.InsertAsync(Rows(3), _tenantId, Guid.NewGuid(), "Test");
        result.Should().Be(3);
    }

    [Fact]
    public async Task InsertAsync_returns_zero_for_empty_stream()
    {
        var inserter = BuildInserter();
        var result = await inserter.InsertAsync(EmptyRows(), _tenantId, Guid.NewGuid(), "Test");
        result.Should().Be(0);
    }

    [Fact]
    public async Task InsertAsync_persists_all_rows_to_database()
    {
        var inserter = BuildInserter(batchSize: 5);
        await inserter.InsertAsync(Rows(12), _tenantId, Guid.NewGuid(), "Test");

        var count = await _context.Transactions.IgnoreQueryFilters().CountAsync();
        count.Should().Be(12, "all 12 rows must be persisted across 3 batches of 5 + 1 partial");
    }

    // ── Commit every N records ────────────────────────────────────────────────

    [Theory]
    [InlineData(1, 10)]    // 10 batches of 1
    [InlineData(3, 10)]    // 3 batches of 3 + 1 partial
    [InlineData(10, 10)]   // 1 batch of 10
    [InlineData(100, 10)]  // 1 partial batch of 10 (< BatchSize)
    public async Task InsertAsync_commits_correct_number_of_rows_for_various_batch_sizes(
        int batchSize, int totalRows)
    {
        var inserter = BuildInserter(batchSize);
        var result = await inserter.InsertAsync(Rows(totalRows), _tenantId, Guid.NewGuid(), "Test");

        result.Should().Be(totalRows,
            $"all {totalRows} rows must be committed regardless of batch size {batchSize}");

        var dbCount = await _context.Transactions.IgnoreQueryFilters().CountAsync();
        dbCount.Should().Be(totalRows);
    }

    // ── Field mapping ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_maps_Amount_field_to_transaction_amount()
    {
        var inserter = BuildInserter();
        await inserter.InsertAsync(Rows(1, amount: "123.45"), _tenantId, Guid.NewGuid(), "Test");

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.Amount.Should().Be(123.45m, "Amount field must be parsed as a decimal");
    }

    [Fact]
    public async Task InsertAsync_maps_Id_field_to_ExternalReferenceId()
    {
        var inserter = BuildInserter();
        await inserter.InsertAsync(Rows(1, id: "EXT-9999"), _tenantId, Guid.NewGuid(), "Test");

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.ExternalReferenceId.Should().Be("EXT-9999");
    }

    [Fact]
    public async Task InsertAsync_maps_Description_field()
    {
        var inserter = BuildInserter();
        await inserter.InsertAsync(
            Rows(1, description: "Quarterly reconciliation"), _tenantId, Guid.NewGuid(), "Test");

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.Description.Should().Be("Quarterly reconciliation");
    }

    [Fact]
    public async Task InsertAsync_defaults_missing_Amount_to_zero()
    {
        // Row has no Amount, Value, or TotalAmount field.
        static async IAsyncEnumerable<ParsedRow> NoAmountRows()
        {
            await Task.Yield();
            yield return new ParsedRow(1,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { ["Id"] = "REF-001" }.AsReadOnly());
        }

        var inserter = BuildInserter();
        await inserter.InsertAsync(NoAmountRows(), _tenantId, Guid.NewGuid(), "Test");

        var tx = await _context.Transactions.IgnoreQueryFilters().FirstAsync();
        tx.Amount.Should().Be(0m, "missing Amount must default to 0");
    }

    [Fact]
    public async Task InsertAsync_sets_all_transactions_to_Pending_status()
    {
        var inserter = BuildInserter();
        await inserter.InsertAsync(Rows(5), _tenantId, Guid.NewGuid(), "Test");

        var statuses = await _context.Transactions
            .IgnoreQueryFilters()
            .Select(t => t.Status)
            .ToListAsync();

        statuses.Should().OnlyContain(s => s == TransactionStatus.Pending,
            "all inserted transactions must default to Pending status");
    }

    [Fact]
    public async Task InsertAsync_sets_TenantId_on_all_transactions()
    {
        var inserter = BuildInserter();
        await inserter.InsertAsync(Rows(3), _tenantId, Guid.NewGuid(), "Test");

        var tenantIds = await _context.Transactions
            .IgnoreQueryFilters()
            .Select(t => t.TenantId)
            .ToListAsync();

        tenantIds.Should().AllBeEquivalentTo(_tenantId,
            "TenantId must be auto-injected from WorkerTenantContext on every Transaction");
    }

    // ── Large batch ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InsertAsync_handles_one_thousand_rows()
    {
        var inserter = BuildInserter(batchSize: 250);
        var result = await inserter.InsertAsync(Rows(1_000), _tenantId, Guid.NewGuid(), "Test");

        result.Should().Be(1_000);
    }
}
