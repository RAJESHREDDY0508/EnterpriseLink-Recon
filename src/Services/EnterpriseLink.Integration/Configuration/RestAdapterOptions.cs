using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Integration.Configuration;

/// <summary>Authentication scheme used by the REST adapter.</summary>
public enum RestAuthScheme
{
    None,
    Bearer,
    ApiKey,
    Basic
}

/// <summary>
/// Configuration for a single REST adapter instance.
/// </summary>
public sealed class RestAdapterOptions
{
    /// <summary>Logical name used in logs and manual trigger paths.</summary>
    [Required] public string Name { get; init; } = string.Empty;

    /// <summary>Internal EnterpriseLink tenant all ingested records belong to.</summary>
    [Required] public Guid TenantId { get; init; }

    /// <summary>Full URL of the REST API endpoint to poll.</summary>
    [Required] public string BaseUrl { get; init; } = string.Empty;

    /// <summary>HTTP method (GET, POST). Defaults to GET.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Authentication scheme for the endpoint.</summary>
    public RestAuthScheme AuthScheme { get; init; } = RestAuthScheme.None;

    /// <summary>Bearer token or API key value (set via user-secrets in dev).</summary>
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>
    /// For <see cref="RestAuthScheme.ApiKey"/>: the header name to use
    /// (e.g. <c>X-API-Key</c>).
    /// </summary>
    public string ApiKeyHeader { get; init; } = "X-API-Key";

    /// <summary>
    /// For <see cref="RestAuthScheme.Basic"/>: base64-encoded "username:password".
    /// </summary>
    public string BasicCredentials { get; init; } = string.Empty;

    /// <summary>
    /// JSONPath-style dot-path to the array of records in the response.
    /// E.g. <c>records</c> or <c>data.items</c>.
    /// Leave empty if the root of the response is an array.
    /// </summary>
    public string ResponseArrayPath { get; init; } = string.Empty;

    /// <summary>Polling interval in seconds.</summary>
    public int PollingIntervalSeconds { get; init; } = 300;

    /// <summary>Whether this adapter is active.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Value written to the <c>SourceSystem</c> column.</summary>
    [Required] public string SourceSystem { get; init; } = string.Empty;

    /// <summary>Maps external JSON property names to internal CSV column names.</summary>
    public Dictionary<string, string> FieldMappings { get; init; } = new();

    /// <summary>Optional query parameters appended to the URL.</summary>
    public Dictionary<string, string> QueryParameters { get; init; } = new();

    /// <summary>HTTP timeout for REST calls in seconds.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
