using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace EnterpriseLink.Worker.Parsing;

/// <summary>
/// <see cref="ICsvStreamingParser"/> implementation backed by
/// <a href="https://joshclose.github.io/CsvHelper/">CsvHelper</a>.
///
/// <para><b>Memory model — 5 GB file guarantee</b></para>
/// The file is opened with a 4 KB <see cref="FileStream"/> buffer and
/// <see cref="FileOptions.SequentialScan"/> (OS hint to prefetch sequentially).
/// <see cref="CsvReader.ReadAsync"/> advances one row at a time — no row accumulation.
/// Each <see cref="ParsedRow"/> is yielded to the caller immediately and is eligible
/// for garbage collection as soon as the caller's <c>await foreach</c> loop body
/// completes. Heap use is therefore O(1) relative to the number of rows — only one
/// <see cref="ParsedRow"/> and its <see cref="ParsedRow.Fields"/> dictionary exist at
/// any point in time.
///
/// <para><b>Encoding detection</b></para>
/// <see cref="StreamReader"/> is constructed with
/// <c>detectEncodingFromByteOrderMarks: true</c>. UTF-8 BOM, UTF-16 LE, UTF-16 BE,
/// and UTF-32 BOM are all detected automatically. Files without a BOM are assumed
/// to be UTF-8 (the default for modern CSV exports from Salesforce, SAP, Oracle ERP,
/// etc.).
///
/// <para><b>Robustness</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       Bad data (unbalanced quotes, illegal characters) is skipped silently via
///       <c>BadDataFound = null</c> — the malformed row is logged and the parse
///       continues. Sprint 9 validation will record the gap as a missing-row error.
///     </description>
///   </item>
///   <item>
///     <description>
///       Missing fields (row has fewer columns than the header) are stored as
///       <see cref="string.Empty"/> via <c>MissingFieldFound = null</c>.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Cancellation</b>
/// The <c>[EnumeratorCancellation]</c> attribute on the <c>cancellationToken</c>
/// parameter propagates cancellation into every <c>await csv.ReadAsync()</c> call.
/// Callers that cancel the token during a 5 GB parse will see the enumeration stop
/// within one row boundary — no additional latency.
/// </para>
/// </summary>
public sealed class CsvStreamingParser : ICsvStreamingParser
{
    /// <summary>
    /// Number of rows between progress log entries.
    /// Logs one line per 10,000 rows — sufficient for Seq / ELK dashboards without
    /// flooding the log stream even on 100 M-row files.
    /// </summary>
    private const int ProgressLogIntervalRows = 10_000;

    private readonly ILogger<CsvStreamingParser> _logger;

    /// <summary>
    /// Initialises the parser with its structured logger.
    /// </summary>
    /// <param name="logger">Structured logger for progress and error events.</param>
    public CsvStreamingParser(ILogger<CsvStreamingParser> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>FileStream settings</b></para>
    /// <c>bufferSize = 4096</c> limits the OS read-ahead to 4 KB per I/O operation.
    /// <c>FileOptions.SequentialScan</c> tells the OS to optimise the page cache for
    /// sequential reading, reducing memory pressure when the OS would otherwise keep
    /// random-access cache pages.
    /// <c>FileOptions.Asynchronous</c> enables true async I/O on Windows (IOCP).
    ///
    /// <para><b>CsvHelper configuration</b></para>
    /// <c>HasHeaderRecord = true</c> — first row is always treated as the column header.
    /// <c>BadDataFound = null</c> — bad rows are skipped (logged separately).
    /// <c>MissingFieldFound = null</c> — missing columns yield empty strings.
    /// <c>TrimOptions.Trim</c> — leading/trailing spaces stripped from each field value.
    /// </remarks>
    public async IAsyncEnumerable<ParsedRow> ParseAsync(
        string fullPath,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // ── Guard: file must exist before opening ────────────────────────────
        // Throwing before yielding gives the consumer a clear FileNotFoundException
        // that triggers retry → dead-letter, instead of a generic I/O error mid-stream.
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"CSV file was not found at '{fullPath}'. " +
                "Verify the storage path in the FileUploadedEvent is correct and " +
                "that the file has not been deleted since ingestion.",
                fullPath);
        }

        _logger.LogInformation(
            "Starting CSV streaming parse. FullPath={FullPath}", fullPath);

        var badRowCount = 0;

        // ── CsvHelper configuration ───────────────────────────────────────────
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,

            // Do not throw on malformed rows — log and continue. Sprint 9 will
            // record the gap as a data-quality error rather than aborting the batch.
            BadDataFound = args =>
            {
                badRowCount++;
                _logger.LogWarning(
                    "Bad CSV data skipped at field {Field}. FullPath={FullPath}",
                    args.Field, fullPath);
            },

            // Missing columns yield empty string rather than throwing.
            MissingFieldFound = null,

            // Strip leading/trailing whitespace from each field value.
            // Downstream validation rules (Sprint 9) receive trimmed values.
            TrimOptions = TrimOptions.Trim,
        };

        // ── Open the file with streaming-optimised settings ───────────────────
        // 4 KB buffer: small enough to avoid large heap allocations, large enough
        // for efficient sequential I/O on any modern OS.
        // SequentialScan: informs the OS page cache this is a forward-only scan.
        // Asynchronous: enables IOCP on Windows for non-blocking reads.
        await using var fileStream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4_096,
            options: FileOptions.SequentialScan | FileOptions.Asynchronous);

        // BOM detection: handles UTF-8 BOM, UTF-16 LE/BE, UTF-32.
        // Files without a BOM default to UTF-8.
        using var streamReader = new StreamReader(
            fileStream,
            encoding: Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);

        using var csv = new CsvReader(streamReader, config);

        // ── Read and validate header ──────────────────────────────────────────
        var hasHeader = await csv.ReadAsync();
        if (!hasHeader)
        {
            // Empty file — log and exit cleanly (0 rows yielded).
            _logger.LogWarning(
                "CSV file is empty (no header row). FullPath={FullPath}", fullPath);
            yield break;
        }

        csv.ReadHeader();

        if (csv.HeaderRecord is null || csv.HeaderRecord.Length == 0)
        {
            throw new InvalidDataException(
                $"CSV file at '{fullPath}' has no readable header row. " +
                "A header row is required to map column names to row fields.");
        }

        _logger.LogDebug(
            "CSV header read. ColumnCount={ColumnCount} Columns={Columns} FullPath={FullPath}",
            csv.HeaderRecord.Length,
            string.Join(", ", csv.HeaderRecord),
            fullPath);

        // ── Stream data rows one at a time ────────────────────────────────────
        var rowNumber = 0;

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            rowNumber++;

            // Build the field dictionary for this row.
            // Dictionary capacity is pre-set to the header column count to avoid
            // internal resizing allocations on every row.
            var fields = new Dictionary<string, string>(
                csv.HeaderRecord.Length,
                StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < csv.HeaderRecord.Length; i++)
            {
                // GetField returns null when the column is missing from this row;
                // store empty string so callers always get a non-null value.
                fields[csv.HeaderRecord[i]] = csv.GetField(i) ?? string.Empty;
            }

            yield return new ParsedRow(rowNumber, fields.AsReadOnly());

            // ── Progress logging ──────────────────────────────────────────────
            // One log line per 10,000 rows: visible in dashboards, not flooding.
            if (rowNumber % ProgressLogIntervalRows == 0)
            {
                _logger.LogInformation(
                    "CSV parse progress. RowsRead={RowsRead} BadRows={BadRows} FullPath={FullPath}",
                    rowNumber, badRowCount, fullPath);
            }
        }

        _logger.LogInformation(
            "CSV streaming parse complete. " +
            "TotalRows={TotalRows} BadRows={BadRows} FullPath={FullPath}",
            rowNumber, badRowCount, fullPath);
    }
}
