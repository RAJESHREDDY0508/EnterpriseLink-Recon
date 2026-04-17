using System.Text;

namespace EnterpriseLink.Integration.Transformation;

/// <summary>
/// Pass-through transformer for data that is already in CSV format (e.g. files
/// downloaded from an SFTP server). Counts data rows and returns the content
/// unchanged, appending a <c>SourceSystem</c> column if not already present.
/// </summary>
public sealed class CsvPassThroughTransformer : IDataTransformer
{
    private readonly ILogger<CsvPassThroughTransformer> _logger;

    public CsvPassThroughTransformer(ILogger<CsvPassThroughTransformer> logger)
    {
        _logger = logger;
    }

    public TransformResult Transform(
        string rawData,
        Dictionary<string, string> fieldMappings,
        string sourceSystem,
        string adapterName)
    {
        if (string.IsNullOrWhiteSpace(rawData))
            return Empty(adapterName);

        var lines = rawData.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return Empty(adapterName);

        var header = lines[0].TrimEnd('\r');
        var hasSourceSystem = header.Contains("SourceSystem", StringComparison.OrdinalIgnoreCase);

        var csv = new StringBuilder();
        if (hasSourceSystem)
        {
            // Pass through as-is
            csv.AppendLine(header);
            for (int i = 1; i < lines.Length; i++)
                csv.AppendLine(lines[i].TrimEnd('\r'));
        }
        else
        {
            // Append SourceSystem column
            csv.AppendLine(header + ",SourceSystem");
            for (int i = 1; i < lines.Length; i++)
                csv.AppendLine(lines[i].TrimEnd('\r') + "," + EscapeCsv(sourceSystem));
        }

        int dataRows = lines.Length - 1;

        _logger.LogInformation(
            "CsvPassThroughTransformer: passed through {Rows} rows from {Adapter}",
            dataRows, adapterName);

        return new TransformResult
        {
            CsvContent        = csv.ToString(),
            RowCount          = dataRows,
            SuggestedFileName = $"{adapterName.ToLower()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv",
        };
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static TransformResult Empty(string adapterName) => new()
    {
        CsvContent        = "ExternalReferenceId,Amount,Description,SourceSystem" + Environment.NewLine,
        RowCount          = 0,
        SuggestedFileName = $"{adapterName.ToLower()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv",
    };
}
