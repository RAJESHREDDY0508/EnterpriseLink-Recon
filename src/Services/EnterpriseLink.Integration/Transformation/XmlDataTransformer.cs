using System.Text;
using System.Xml.Linq;

namespace EnterpriseLink.Integration.Transformation;

/// <summary>
/// Transforms XML data (e.g. SOAP response body) into the standard internal CSV format.
///
/// <para>
/// Discovers repeating elements by finding the most-common element name at the
/// second level of the document, then applies the field mappings to
/// extract child element values for each record.
/// </para>
/// </summary>
public sealed class XmlDataTransformer : IDataTransformer
{
    private static readonly string[] InternalColumns =
        ["ExternalReferenceId", "Amount", "Description", "SourceSystem"];

    private readonly ILogger<XmlDataTransformer> _logger;

    public XmlDataTransformer(ILogger<XmlDataTransformer> logger)
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

        XDocument doc;
        try
        {
            doc = XDocument.Parse(rawData);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "XmlDataTransformer: failed to parse XML from {Adapter}", adapterName);
            return Empty(adapterName);
        }

        // Find the most-repeated element name at depth 2 — these are the record elements.
        var root = doc.Root;
        if (root is null) return Empty(adapterName);

        // Strip SOAP envelope if present (Body → first child)
        var body = root.Descendants()
            .FirstOrDefault(e => e.Name.LocalName is "Body" or "body");
        var searchRoot = body ?? root;

        // Detect records: if direct children share the same element name, they ARE the records.
        // Otherwise, the first child is a container wrapper — its children are the records.
        var directChildren = searchRoot.Elements().ToList();
        List<XElement> records;
        if (directChildren.Count > 1 &&
            directChildren.Select(e => e.Name.LocalName).Distinct().Count() == 1)
        {
            records = directChildren;
        }
        else if (directChildren.Count == 1)
        {
            var nested = directChildren[0].Elements().ToList();
            records = nested.Count > 0 ? nested : directChildren;
        }
        else
        {
            records = directChildren;
        }

        if (records.Count == 0)
        {
            _logger.LogInformation(
                "XmlDataTransformer: no records found in {Adapter} response", adapterName);
            return Empty(adapterName);
        }

        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", InternalColumns));

        int rowCount = 0;
        foreach (var record in records)
        {
            var row = BuildRow(record, fieldMappings, sourceSystem);
            csv.AppendLine(row);
            rowCount++;
        }

        _logger.LogInformation(
            "XmlDataTransformer: produced {Rows} rows from {Adapter}", rowCount, adapterName);

        return new TransformResult
        {
            CsvContent        = csv.ToString(),
            RowCount          = rowCount,
            SuggestedFileName = $"{adapterName.ToLower()}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv",
        };
    }

    private static string BuildRow(
        XElement record,
        Dictionary<string, string> fieldMappings,
        string sourceSystem)
    {
        string Get(string internalName)
        {
            var externalKey = fieldMappings
                .FirstOrDefault(kv => kv.Value == internalName).Key;
            if (externalKey is null) return string.Empty;

            var value = record.Elements()
                .FirstOrDefault(e => e.Name.LocalName == externalKey)?.Value ?? string.Empty;

            return EscapeCsv(value);
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
