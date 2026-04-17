using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Integration.Configuration;

/// <summary>
/// Top-level integration service configuration (storage root, messaging).
/// </summary>
public sealed class IntegrationOptions
{
    public const string SectionName = "Integration";

    /// <summary>
    /// Root directory where adapter-produced CSV files are written before
    /// being published as <c>FileUploadedEvent</c> payloads.
    /// </summary>
    [Required]
    public string StorageRoot { get; init; } = "integration-files";
}

/// <summary>
/// RabbitMQ messaging configuration shared by all adapters.
/// </summary>
public sealed class MessagingOptions
{
    public const string SectionName = "RabbitMQ";

    [Required] public string Host { get; init; } = "localhost";
    [Required] public string VirtualHost { get; init; } = "enterpriselink";
    [Required] public string Username { get; init; } = "eluser";
    [Required] public string Password { get; init; } = "elpassword";
    public int RetryCount { get; init; } = 3;
    public int RetryIntervalSeconds { get; init; } = 5;
}
