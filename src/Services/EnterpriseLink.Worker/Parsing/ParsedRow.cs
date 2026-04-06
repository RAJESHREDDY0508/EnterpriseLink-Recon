namespace EnterpriseLink.Worker.Parsing;

/// <summary>
/// Represents a single data row parsed from a CSV file.
///
/// <para>
/// Each <see cref="ParsedRow"/> is created for one CSV line and is intended to be
/// consumed immediately — it is never accumulated into a <c>List</c> or array over
/// the full file lifetime. This ensures memory use stays bounded regardless of file
/// size (including 5 GB+ files).
/// </para>
///
/// <para><b>Field access</b></para>
/// Fields are keyed by the original CSV header name (case-insensitive). If a column
/// is missing for a particular row the value is stored as <see cref="string.Empty"/>
/// rather than throwing, giving downstream validation rules (Sprint 9) a consistent
/// surface to report errors on.
///
/// <para><b>Thread safety</b>
/// Instances are effectively immutable after construction — the <see cref="Fields"/>
/// dictionary is wrapped in <see cref="IReadOnlyDictionary{TKey,TValue}"/> and the
/// record itself is sealed, so instances are safe to pass across async continuations.
/// </para>
/// </summary>
/// <param name="RowNumber">
/// 1-based row number within the CSV file, excluding the header row.
/// Row 1 is the first data row; used for error reporting and progress logging.
/// </param>
/// <param name="Fields">
/// Header-keyed column values for this row.
/// Keys match the CSV header names exactly as they appear in the file.
/// Values are raw strings — no trimming or type conversion is applied here;
/// that is the responsibility of the validation and mapping layer (Sprint 9).
/// </param>
public sealed record ParsedRow(
    int RowNumber,
    IReadOnlyDictionary<string, string> Fields);
