using System.Xml.Linq;

namespace EnterpriseLink.Integration.Adapters.Soap;

/// <summary>
/// Extracts the SOAP response body inner XML from a raw SOAP response string,
/// stripping the envelope and fault-checking before handing off to the transformer.
/// </summary>
public sealed class SoapResponseParser
{
    private static readonly XNamespace SoapNs = "http://schemas.xmlsoap.org/soap/envelope/";

    private readonly ILogger<SoapResponseParser> _logger;

    public SoapResponseParser(ILogger<SoapResponseParser> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses <paramref name="soapResponse"/> and returns the inner XML of the
    /// <c>soap:Body</c> element (unwrapped from the envelope).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the response contains a <c>soap:Fault</c> element.
    /// </exception>
    public string ExtractBody(string soapResponse)
    {
        if (string.IsNullOrWhiteSpace(soapResponse))
            return string.Empty;

        XDocument doc;
        try
        {
            doc = XDocument.Parse(soapResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SoapResponseParser: response is not valid XML");
            return string.Empty;
        }

        // Check for SOAP Fault
        var fault = doc.Descendants(SoapNs + "Fault").FirstOrDefault()
                 ?? doc.Descendants("Fault").FirstOrDefault();
        if (fault is not null)
        {
            var faultString = fault.Element("faultstring")?.Value
                           ?? fault.Element("Reason")?.Value
                           ?? "Unknown SOAP fault";
            throw new InvalidOperationException($"SOAP Fault: {faultString}");
        }

        // Extract Body content
        var body = doc.Descendants(SoapNs + "Body").FirstOrDefault()
                ?? doc.Descendants("Body").FirstOrDefault();

        if (body is null)
        {
            _logger.LogWarning("SoapResponseParser: no soap:Body found in response");
            return string.Empty;
        }

        // Return the first child of Body (the actual response element) as XML string
        var responseElement = body.Elements().FirstOrDefault();
        return responseElement?.ToString() ?? string.Empty;
    }
}
