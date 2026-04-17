namespace EnterpriseLink.Integration.Adapters.Soap;

/// <summary>
/// Builds SOAP 1.1 request envelopes for a given operation and parameter set.
/// </summary>
public sealed class SoapEnvelopeBuilder
{
    private const string SoapEnvelopeNs = "http://schemas.xmlsoap.org/soap/envelope/";

    /// <summary>
    /// Builds a SOAP 1.1 envelope XML string for <paramref name="operationName"/>
    /// with the supplied <paramref name="parameters"/> as child elements.
    /// </summary>
    /// <param name="operationName">SOAP operation local name.</param>
    /// <param name="soapNamespace">Target XML namespace for the operation element.</param>
    /// <param name="parameters">Key-value pairs written as child elements of the operation.</param>
    public string Build(
        string operationName,
        string soapNamespace,
        IDictionary<string, string>? parameters = null)
    {
        var ns = string.IsNullOrWhiteSpace(soapNamespace) ? string.Empty : $" xmlns:tns=\"{soapNamespace}\"";
        var opTag = string.IsNullOrWhiteSpace(soapNamespace)
            ? operationName
            : $"tns:{operationName}";

        var paramXml = string.Empty;
        if (parameters is { Count: > 0 })
        {
            paramXml = string.Concat(
                parameters.Select(kv =>
                    $"<{kv.Key}>{System.Security.SecurityElement.Escape(kv.Value)}</{kv.Key}>"));
        }

        return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <soap:Envelope xmlns:soap="{SoapEnvelopeNs}"{ns}>
                  <soap:Header/>
                  <soap:Body>
                    <{opTag}>{paramXml}</{opTag}>
                  </soap:Body>
                </soap:Envelope>
                """;
    }
}
