using System.Text.Json;

namespace EnterpriseLink.Integration.Adapters.Rest;

/// <summary>
/// Navigates a JSON response to the configured array path and extracts
/// the raw JSON array element for downstream transformation.
/// </summary>
public sealed class RestResponseMapper
{
    private readonly ILogger<RestResponseMapper> _logger;

    public RestResponseMapper(ILogger<RestResponseMapper> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Navigates <paramref name="json"/> using <paramref name="arrayPath"/>
    /// (dot-separated property names) and returns the JSON array element as a string.
    /// If <paramref name="arrayPath"/> is empty, returns the root element if it is an array.
    /// </summary>
    public string ExtractArray(string json, string arrayPath)
    {
        if (string.IsNullOrWhiteSpace(json)) return "[]";

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "RestResponseMapper: invalid JSON response");
            return "[]";
        }

        using (doc)
        {
            var element = doc.RootElement;

            if (!string.IsNullOrWhiteSpace(arrayPath))
            {
                foreach (var segment in arrayPath.Split('.'))
                {
                    if (element.ValueKind != JsonValueKind.Object ||
                        !element.TryGetProperty(segment, out var child))
                    {
                        _logger.LogWarning(
                            "RestResponseMapper: path segment '{Segment}' not found in response",
                            segment);
                        return "[]";
                    }
                    element = child;
                }
            }

            if (element.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "RestResponseMapper: resolved path is not a JSON array (kind={Kind})",
                    element.ValueKind);
                return "[]";
            }

            return element.GetRawText();
        }
    }
}
