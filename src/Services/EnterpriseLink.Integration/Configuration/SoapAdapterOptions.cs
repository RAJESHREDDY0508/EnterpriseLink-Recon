using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Integration.Configuration;

/// <summary>
/// Configuration for a single SOAP adapter instance.
/// Multiple adapters can be configured — each entry in the <c>SoapAdapters</c>
/// array runs as an independent polling loop.
/// </summary>
public sealed class SoapAdapterOptions
{
    /// <summary>Logical name used in logs, status endpoints, and manual trigger paths.</summary>
    [Required] public string Name { get; init; } = string.Empty;

    /// <summary>Internal EnterpriseLink tenant all ingested records belong to.</summary>
    [Required] public Guid TenantId { get; init; }

    /// <summary>
    /// URL of the WSDL document. Used by <c>WsdlInspector</c> to enumerate operations
    /// and validate that <see cref="OperationName"/> exists before the first call.
    /// </summary>
    [Required] public string WsdlUrl { get; init; } = string.Empty;

    /// <summary>Actual SOAP endpoint URL (may differ from WSDL URL).</summary>
    [Required] public string EndpointUrl { get; init; } = string.Empty;

    /// <summary>SOAP operation name to invoke (e.g. <c>GetTransactions</c>).</summary>
    [Required] public string OperationName { get; init; } = string.Empty;

    /// <summary>SOAPAction HTTP header value required by many SOAP 1.1 services.</summary>
    public string SoapAction { get; init; } = string.Empty;

    /// <summary>XML namespace used when building the SOAP request body.</summary>
    [Required] public string SoapNamespace { get; init; } = string.Empty;

    /// <summary>Polling interval. Set to 0 to disable scheduled polling (manual trigger only).</summary>
    public int PollingIntervalSeconds { get; init; } = 300;

    /// <summary>Whether this adapter is active. Disabled adapters are registered but never polled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Value written to the <c>SourceSystem</c> column of ingested transactions.</summary>
    [Required] public string SourceSystem { get; init; } = string.Empty;

    /// <summary>
    /// Maps external field names (keys) to internal CSV column names (values).
    /// Required internal columns: <c>ExternalReferenceId</c>, <c>Amount</c>.
    /// Optional: <c>Description</c>.
    /// </summary>
    public Dictionary<string, string> FieldMappings { get; init; } = new();

    /// <summary>Optional static request parameters to include in the SOAP body.</summary>
    public Dictionary<string, string> RequestParameters { get; init; } = new();

    /// <summary>HTTP timeout for SOAP calls in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
