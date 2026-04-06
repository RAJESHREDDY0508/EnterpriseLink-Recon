namespace EnterpriseLink.Worker.Parsing;

/// <summary>
/// Streaming CSV parser that reads arbitrarily large files without loading them
/// into memory.
///
/// <para><b>Memory guarantee</b></para>
/// Implementations must yield one <see cref="ParsedRow"/> at a time via
/// <c>IAsyncEnumerable</c>. At no point may the full file content, or the complete
/// set of rows, be buffered in memory. This guarantee enables the system to process
/// 5 GB+ CSV files on hosts with as little as 512 MB of usable heap.
///
/// <para><b>Cancellation</b></para>
/// Callers cancel by cancelling the token passed to <see cref="ParseAsync"/>.
/// Implementations must honour the token on each loop iteration using the
/// <c>[EnumeratorCancellation]</c> attribute pattern so that long-running parses
/// are interrupted promptly.
///
/// <para><b>Error handling</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       If the file does not exist, <see cref="ParseAsync"/> throws
///       <see cref="FileNotFoundException"/> before yielding any rows.
///     </description>
///   </item>
///   <item>
///     <description>
///       Malformed rows (bad quoting, extra delimiters) are skipped and logged
///       rather than aborting the entire file — the validation layer (Sprint 9)
///       will capture field-level errors.
///     </description>
///   </item>
/// </list>
/// </summary>
public interface ICsvStreamingParser
{
    /// <summary>
    /// Asynchronously streams all data rows in the CSV file at <paramref name="fullPath"/>,
    /// yielding one <see cref="ParsedRow"/> per iteration without buffering the file.
    /// </summary>
    /// <param name="fullPath">
    /// Absolute filesystem path to the CSV file.
    /// Must be a file that exists and is readable by the current process.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the streaming enumeration. Cancellation is checked
    /// on every row so large files do not block shutdown or request cancellation.
    /// </param>
    /// <returns>
    /// An <see cref="IAsyncEnumerable{T}"/> of <see cref="ParsedRow"/> instances,
    /// one per CSV data row (header excluded). The enumerable is lazy — I/O only
    /// occurs when the caller awaits <c>MoveNextAsync()</c>.
    /// </returns>
    /// <exception cref="FileNotFoundException">
    /// Thrown before yielding any rows when <paramref name="fullPath"/> does not exist.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// Thrown before yielding any rows when the file contains no header row.
    /// </exception>
    IAsyncEnumerable<ParsedRow> ParseAsync(
        string fullPath,
        CancellationToken cancellationToken = default);
}
