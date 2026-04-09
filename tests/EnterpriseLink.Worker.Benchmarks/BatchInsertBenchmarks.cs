using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using EnterpriseLink.Shared.Infrastructure.MultiTenancy;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Batch;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.MultiTenancy;
using EnterpriseLink.Worker.Parsing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Benchmarks;

/// <summary>
/// BenchmarkDotNet microbenchmarks for <see cref="TransactionBatchInserter"/>.
///
/// <para><b>What is measured</b></para>
/// The EF Core pipeline overhead (change tracking, SaveChangesAsync, object allocation)
/// at various <see cref="BatchSize"/> values. InMemory database is used so the numbers
/// reflect pure EF Core cost — not SQL Server I/O latency.
///
/// <para><b>What is NOT measured</b></para>
/// SQL Server I/O throughput. To measure production throughput, run the benchmark
/// against a real SQL Server instance by replacing <c>UseInMemoryDatabase</c> with
/// <c>UseSqlServer</c> and providing a <c>ConnectionStrings__DefaultConnection</c>
/// environment variable.
///
/// <para><b>How to run</b></para>
/// <code>
/// dotnet run --project tests/EnterpriseLink.Worker.Benchmarks -c Release
/// </code>
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class BatchInsertBenchmarks
{
    // ── Parameters ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Number of rows accumulated before a single <c>SaveChangesAsync</c> call.
    /// BenchmarkDotNet runs the benchmark once per value.
    /// </summary>
    [Params(100, 500, 1_000, 5_000)]
    public int BatchSize { get; set; }

    /// <summary>Total rows streamed per benchmark invocation.</summary>
    [Params(10_000)]
    public int TotalRows { get; set; }

    // ── State ───────────────────────────────────────────────────────────────────

    private TransactionBatchInserter _inserter = null!;
    private Guid _tenantId;
    private AppDbContext _context = null!;

    // ── Setup ───────────────────────────────────────────────────────────────────

    [GlobalSetup]
    public void Setup()
    {
        _tenantId = Guid.NewGuid();

        var tenantContext = new WorkerTenantContext { TenantId = _tenantId };

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"BenchmarkDb_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options, tenantContext);
        _context.Database.EnsureCreated();

        var batchOptions = Options.Create(new BatchInsertOptions { BatchSize = BatchSize });

        _inserter = new TransactionBatchInserter(
            _context,
            batchOptions,
            NullLogger<TransactionBatchInserter>.Instance);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _context.Dispose();
    }

    // ── Benchmarks ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Measures the time and allocations required to stream and insert
    /// <see cref="TotalRows"/> rows at the current <see cref="BatchSize"/>.
    /// </summary>
    [Benchmark]
    public async Task<int> BatchInsert_Transactions()
    {
        var rows = GenerateRows(TotalRows);
        return await _inserter.InsertAsync(
            rows,
            tenantId: _tenantId,
            uploadId: Guid.NewGuid(),
            sourceSystem: "Benchmark",
            cancellationToken: default);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<ParsedRow> GenerateRows(int count)
    {
        for (var i = 1; i <= count; i++)
        {
            await Task.Yield(); // Simulate async streaming (CSV file read).
            yield return new ParsedRow(
                RowNumber: i,
                Fields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Amount"] = (i * 1.25m).ToString("F2"),
                    ["Id"] = $"REF-{i:D8}",
                    ["Description"] = $"Benchmark row {i}",
                }.AsReadOnly());
        }
    }
}
