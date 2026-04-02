using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Ingestion.Configuration;

/// <summary>
/// Strongly-typed configuration for the RabbitMQ message broker connection.
///
/// <para>Populated from the <c>RabbitMQ</c> section of <c>appsettings.json</c>.</para>
///
/// <para><b>Configuration example</b></para>
/// <code>
/// {
///   "RabbitMQ": {
///     "Host": "rabbitmq",
///     "VirtualHost": "enterpriselink",
///     "Username": "eluser",
///     "Password": "elpassword"
///   }
/// }
/// </code>
///
/// <para><b>Retry policy</b></para>
/// <see cref="RetryCount"/> and <see cref="RetryIntervalSeconds"/> control the
/// exponential back-off retry policy applied to all MassTransit publish operations.
/// The effective wait intervals are:
/// <c>RetryIntervalSeconds^n</c> for attempt <c>n</c> (1-based).
/// Default: 3 attempts at 1 s, 5 s, 25 s.
/// </summary>
public sealed class MessagingOptions
{
    /// <summary>The configuration section name that maps to this class.</summary>
    public const string SectionName = "RabbitMQ";

    /// <summary>
    /// RabbitMQ hostname or Docker service name.
    /// Default: <c>localhost</c>.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ:Host is required.")]
    public string Host { get; set; } = "localhost";

    /// <summary>
    /// RabbitMQ virtual host. Provides logical separation between environments.
    /// Default: <c>enterpriselink</c>.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ:VirtualHost is required.")]
    public string VirtualHost { get; set; } = "enterpriselink";

    /// <summary>
    /// RabbitMQ username.
    /// Store secrets in .NET Secret Manager (dev) or Azure Key Vault (prod).
    /// Default: <c>eluser</c>.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ:Username is required.")]
    public string Username { get; set; } = "eluser";

    /// <summary>
    /// RabbitMQ password.
    /// Store secrets in .NET Secret Manager (dev) or Azure Key Vault (prod).
    /// Default: <c>elpassword</c>.
    /// </summary>
    [Required(ErrorMessage = "RabbitMQ:Password is required.")]
    public string Password { get; set; } = "elpassword";

    /// <summary>
    /// Number of retry attempts on transient publish failures.
    /// Must be between 1 and 10.
    /// Default: <c>3</c>.
    /// </summary>
    [Range(1, 10, ErrorMessage = "RabbitMQ:RetryCount must be between 1 and 10.")]
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Base interval in seconds for the exponential back-off retry policy.
    /// Effective wait: <c>RetryIntervalSeconds ^ attemptNumber</c>.
    /// Must be between 1 and 60.
    /// Default: <c>5</c> (gives 5 s, 25 s, 125 s for three attempts).
    /// </summary>
    [Range(1, 60, ErrorMessage = "RabbitMQ:RetryIntervalSeconds must be between 1 and 60.")]
    public int RetryIntervalSeconds { get; set; } = 5;
}
