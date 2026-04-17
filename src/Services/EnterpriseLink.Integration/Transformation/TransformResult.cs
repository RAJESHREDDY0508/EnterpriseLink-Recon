namespace EnterpriseLink.Integration.Transformation;

/// <summary>
/// Output of a data transformation operation: a CSV string ready for storage
/// plus metadata about how many rows were produced.
/// </summary>
public sealed class TransformResult
{
    /// <summary>
    /// Full CSV content including the header row.
    /// Columns: <c>ExternalReferenceId,Amount,Description,SourceSystem</c>.
    /// </summary>
    public required string CsvContent { get; init; }

    /// <summary>Number of data rows (header excluded).</summary>
    public required int RowCount { get; init; }

    /// <summary>Suggested file name for storage (e.g. <c>legacyerp_20260417_143022.csv</c>).</summary>
    public required string SuggestedFileName { get; init; }
}
