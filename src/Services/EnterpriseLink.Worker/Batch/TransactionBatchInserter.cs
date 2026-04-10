using System.Diagnostics;
using System.Globalization;
using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.Parsing;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Batch;

/// <summary>
/// EF Core implementation of <see cref="IBatchRowInserter"/> that maps
/// <see cref="ParsedRow"/> objects to <see cref="Transaction"/> entities and
/// commits them in configurable-size batches.
///
/// <para><b>Memory model</b></para>
/// A fixed-capacity <c>List&lt;Transaction&gt;</c> is pre-allocated once per
/// <see cref="InsertAsync"/> call. After each <c>SaveChangesAsync</c> the list
/// is cleared (<c>List.Clear</c>) and the EF Core change tracker is reset
/// (<c>ChangeTracker.Clear</c>). Memory use is therefore bounded by
/// <c>BatchSize × sizeof(Transaction)</c> regardless of total file size.
///
/// <para><b>Field mapping</b></para>
/// The CSV column names are not standardised across source systems. The mapper
/// attempts several candidate column names (case-insensitive) in priority order:
/// <list type="bullet">
///   <item><description><c>Amount</c>: "Amount", "Value", "TotalAmount"</description></item>
///   <item><description><c>ExternalReferenceId</c>: "Id", "ExternalReferenceId", "ReferenceId", "TransactionId"</description></item>
///   <item><description><c>Description</c>: "Description", "Notes", "Memo", "Comment"</description></item>
/// </list>
/// Missing or un-parseable fields are silently defaulted (Amount→0, others→null).
/// Sprint 9 validation will flag field-level errors.
///
/// <para><b>Performance logging</b>
/// Each batch emits a <c>Debug</c> log with batch number, row count, and elapsed ms.
/// On completion a single <c>Information</c> log records total rows inserted,
/// total elapsed ms, and rows/second throughput.
/// </para>
/// </summary>
public sealed class TransactionBatchInserter : IBatchRowInserter
{
    private readonly AppDbContext _context;
    private readonly int _batchSize;
    private readonly ILogger<TransactionBatchInserter> _logger;

    /// <summary>Initialises the inserter with its required dependencies.</summary>
    /// <param name="context">Scoped EF Core context; change tracker is cleared between batches.</param>
    /// <param name="options">Batch insert options — controls <c>BatchSize</c>.</param>
    /// <param name="logger">Structured logger for throughput metrics.</param>
    public TransactionBatchInserter(
        AppDbContext context,
        IOptions<BatchInsertOptions> options,
        ILogger<TransactionBatchInserter> logger)
    {
        _context = context;
        _batchSize = options.Value.BatchSize;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> InsertAsync(
        IAsyncEnumerable<ParsedRow> rows,
        Guid tenantId,
        Guid uploadId,
        string sourceSystem,
        CancellationToken cancellationToken = default)
    {
        var totalInserted = 0;
        var batchNumber = 0;
        var overallSw = Stopwatch.StartNew();

        // Pre-allocate with known capacity to avoid List resizing.
        var batch = new List<Transaction>(_batchSize);

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            batch.Add(MapRowToTransaction(row, uploadId, sourceSystem));

            if (batch.Count >= _batchSize)
            {
                batchNumber++;
                totalInserted += await CommitBatchAsync(batch, uploadId, batchNumber, cancellationToken);
                batch.Clear();
            }
        }

        // Commit any remaining rows in the final (partial) batch.
        if (batch.Count > 0)
        {
            batchNumber++;
            totalInserted += await CommitBatchAsync(batch, uploadId, batchNumber, cancellationToken);
        }

        overallSw.Stop();
        var elapsedSeconds = Math.Max(overallSw.Elapsed.TotalSeconds, 0.001);
        var rowsPerSecond = totalInserted / elapsedSeconds;

        _logger.LogInformation(
            "Batch insert complete. UploadId={UploadId} SourceSystem={SourceSystem} " +
            "TotalInserted={TotalInserted} Batches={Batches} " +
            "ElapsedMs={ElapsedMs} RowsPerSecond={RowsPerSecond:F0}",
            uploadId, sourceSystem, totalInserted, batchNumber,
            overallSw.ElapsedMilliseconds, rowsPerSecond);

        return totalInserted;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<int> CommitBatchAsync(
        List<Transaction> batch,
        Guid uploadId,
        int batchNumber,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        await _context.Transactions.AddRangeAsync(batch, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        // Clear the change tracker after each commit so tracked entities are
        // released for GC. Without this, the tracker would grow with every batch.
        _context.ChangeTracker.Clear();

        sw.Stop();

        _logger.LogDebug(
            "Batch committed. UploadId={UploadId} BatchNumber={BatchNumber} " +
            "Rows={Rows} ElapsedMs={ElapsedMs}",
            uploadId, batchNumber, batch.Count, sw.ElapsedMilliseconds);

        return batch.Count;
    }

    /// <summary>
    /// Maps a <see cref="ParsedRow"/> to a new <see cref="Transaction"/>.
    /// <c>TenantId</c> is intentionally omitted — it is auto-injected by
    /// <c>AppDbContext.ApplyTenantId</c> from the scoped <c>WorkerTenantContext</c>.
    /// <c>UploadId</c> and <c>SourceSystem</c> carry data-lineage provenance
    /// (Story 3) so every transaction can be traced back to its originating file.
    /// </summary>
    private static Transaction MapRowToTransaction(
        ParsedRow row, Guid uploadId, string sourceSystem)
    {
        var amount = ParseDecimal(row.Fields, "Amount", "Value", "TotalAmount");
        var externalRef = GetFirstNonEmpty(row.Fields, "Id", "ExternalReferenceId", "ReferenceId", "TransactionId");
        var description = GetFirstNonEmpty(row.Fields, "Description", "Notes", "Memo", "Comment");

        return new Transaction
        {
            Amount = amount,
            Status = TransactionStatus.Pending,
            ExternalReferenceId = externalRef,
            Description = description,
            UploadId = uploadId,
            SourceSystem = sourceSystem,
        };
    }

    private static decimal ParseDecimal(
        IReadOnlyDictionary<string, string> fields,
        params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (fields.TryGetValue(key, out var raw) &&
                decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return 0m;
    }

    private static string? GetFirstNonEmpty(
        IReadOnlyDictionary<string, string> fields,
        params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            if (fields.TryGetValue(key, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
