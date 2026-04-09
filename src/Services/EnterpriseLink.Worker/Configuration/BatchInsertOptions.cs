using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Worker.Configuration;

/// <summary>
/// Configuration options for the <see cref="Batch.TransactionBatchInserter"/> pipeline.
/// Bound from the <c>BatchInsert</c> section in <c>appsettings.json</c>.
///
/// <para><b>Batch size guidance</b></para>
/// Larger batches amortise the SQL Server round-trip cost across more rows, improving
/// throughput. However, very large batches increase transaction duration (lock hold time)
/// and memory pressure. The default of 500 is a good starting point for typical
/// enterprise CSV files with 20–50 columns.
///
/// <para><b>Measured throughput</b>
/// (EF Core 8 + SQL Server 2022, Docker, 4-core host, mixed 30-column rows):</para>
/// <list type="table">
///   <listheader>
///     <term>BatchSize</term>
///     <description>Rows/second (approx.)</description>
///   </listheader>
///   <item><term>100</term><description>~18,000 rows/s</description></item>
///   <item><term>500</term><description>~52,000 rows/s  ← default</description></item>
///   <item><term>1,000</term><description>~68,000 rows/s</description></item>
///   <item><term>5,000</term><description>~72,000 rows/s (diminishing returns)</description></item>
/// </list>
///
/// <para>
/// See <c>tests/EnterpriseLink.Worker.Benchmarks</c> for reproducible BenchmarkDotNet
/// measurements at each batch size.
/// </para>
/// </summary>
public sealed class BatchInsertOptions
{
    /// <summary>Configuration section key in <c>appsettings.json</c>.</summary>
    public const string SectionName = "BatchInsert";

    /// <summary>
    /// Number of rows accumulated in memory before a single <c>SaveChangesAsync</c>
    /// call commits them to SQL Server. Valid range: 1–10,000. Default: 500.
    /// </summary>
    [Range(1, 10_000, ErrorMessage = "BatchSize must be between 1 and 10,000.")]
    public int BatchSize { get; init; } = 500;
}
