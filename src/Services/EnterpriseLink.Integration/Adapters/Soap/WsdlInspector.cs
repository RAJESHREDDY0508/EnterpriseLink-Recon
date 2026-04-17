using System.Xml.Linq;

namespace EnterpriseLink.Integration.Adapters.Soap;

/// <summary>
/// Fetches a WSDL document and enumerates the operations it exposes.
/// Used at adapter startup to validate that the configured
/// <c>OperationName</c> exists before the first polling cycle.
/// </summary>
public sealed class WsdlInspector
{
    private static readonly XNamespace Wsdl = "http://schemas.xmlsoap.org/wsdl/";

    private readonly HttpClient _http;
    private readonly ILogger<WsdlInspector> _logger;

    public WsdlInspector(HttpClient http, ILogger<WsdlInspector> logger)
    {
        _http   = http;
        _logger = logger;
    }

    /// <summary>
    /// Downloads and parses the WSDL at <paramref name="wsdlUrl"/> and returns
    /// all <see cref="WsdlOperation"/> entries found in the <c>portType</c> elements.
    /// </summary>
    public async Task<IReadOnlyList<WsdlOperation>> GetOperationsAsync(
        string wsdlUrl,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching WSDL from {WsdlUrl}", wsdlUrl);

        string xml;
        try
        {
            xml = await _http.GetStringAsync(wsdlUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch WSDL from {WsdlUrl}", wsdlUrl);
            throw new InvalidOperationException(
                $"Cannot fetch WSDL from '{wsdlUrl}': {ex.Message}", ex);
        }

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse WSDL XML from {WsdlUrl}", wsdlUrl);
            throw new InvalidOperationException(
                $"WSDL response from '{wsdlUrl}' is not valid XML: {ex.Message}", ex);
        }

        var operations = doc.Descendants(Wsdl + "operation")
            .Select(op => new WsdlOperation
            {
                Name          = op.Attribute("name")?.Value ?? string.Empty,
                InputMessage  = op.Element(Wsdl + "input")?.Attribute("message")?.Value,
                OutputMessage = op.Element(Wsdl + "output")?.Attribute("message")?.Value,
            })
            .Where(op => !string.IsNullOrEmpty(op.Name))
            .DistinctBy(op => op.Name)
            .ToList();

        _logger.LogInformation(
            "WSDL at {WsdlUrl} exposes {Count} operation(s): {Operations}",
            wsdlUrl, operations.Count,
            string.Join(", ", operations.Select(o => o.Name)));

        return operations;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="operationName"/> exists in the WSDL.
    /// Logs a warning and returns <c>false</c> if it does not.
    /// </summary>
    public async Task<bool> ValidateOperationAsync(
        string wsdlUrl,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var ops = await GetOperationsAsync(wsdlUrl, cancellationToken);
        var exists = ops.Any(o =>
            string.Equals(o.Name, operationName, StringComparison.OrdinalIgnoreCase));

        if (!exists)
            _logger.LogWarning(
                "Operation '{Operation}' not found in WSDL {WsdlUrl}. " +
                "Available: {Available}",
                operationName, wsdlUrl,
                string.Join(", ", ops.Select(o => o.Name)));

        return exists;
    }
}
