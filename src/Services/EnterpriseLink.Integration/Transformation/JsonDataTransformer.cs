using System.Text;
using System.Text.Json;

namespace EnterpriseLink.Integration.Transformation;

/// <summary>
/// Transforms JSON data (e.g. REST API response) into the standard internal CSV format.
///
/// <para>
/// Navigates to the array of records using <c>ResponseArrayPath</c> (dot-separated),
/// then maps each object's properties via the configured field mappings.
/// </para>
/// </summary>
public sealed class JsonDataTransformer : IDataTransformer
{
    private static readonly string[] InternalColumns =
        ["ExternalReferenceId", "Amount", "Description", "SourceSystem"];

    private readonly ILogger<JsonDataTransformer> _logger;

    public JsonDataTransformer(ILogger<JsonDataTransformer> logger)
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

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(rawData);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "JsonDataTransformer: failed to parse JSON from {Adapter}", adapterName);
            return Empty(adapterName);
        }

        using (doc)
        {
            // Find the array — root may already be an array, or a nested path
            var arrayElement = doc.RootElement;

            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogInformation(
                    "JsonDataTransformer: root is not an array for {Adapter}; " +
                    "wrap in ResponseArrayPath if needed", adapterName);
                return Empty(adapterName);
            }

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", InternalColumns));

            int rowCount = 0;
            foreach (var element in arrayElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;

                var row = BuildRow(element, fieldMappings, sourceSystem);
                csv.AppendLine(row);
                rowCount++;
            }

            _logger.LogInformation(
                "JsonDataTransformer: produced {Rows} rows from {Adapter}", rowCount, adapterName);

            return new TransformResult
            {
                CsvContent        = csv.ToString(),
                RowCount          = rowCount,
                SuggestedFileName = $"{adapterName.ToLower()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv",
            };
        }
    }

    private static string BuildRow(
        JsonElement record,
        Dictionary<string, string> fieldMappings,
        string sourceSystem)
    {
        string Get(string internalName)
        {
            var externalKey = fieldMappings
                .FirstOrDefault(kv => kv.Value == internalName).Key;
            if (externalKey is null) return string.Empty;

            if (record.TryGetProperty(externalKey, out var prop))
                return EscapeCsv(prop.ToString());

            return string.Empty;
        }

        return string.Join(",", [
            Get("ExternalReferenceId"),
            Get("Amount"),
            Get("Description"),
            EscapeCsv(sourceSystem),
        ]);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static TransformResult Empty(string adapterName) => new()
    {
        CsvContent        = string.Join(",", InternalColumns) + Environment.NewLine,
        RowCount          = 0,
        SuggestedFileName = $"{adapterName.ToLower()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv",
    };
}
