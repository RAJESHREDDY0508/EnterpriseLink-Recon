using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Worker.Configuration;

/// <summary>
/// Strongly-typed configuration for the RabbitMQ connection used by the Worker service.
///
/// <para>Populated from the <c>RabbitMQ</c> section of <c>appsettings.json</c>.</para>
///
/// <para><b>Retry policy</b></para>
/// Consumer-level retries use exponential back-off:
/// <c>interval = RetryIntervalSeconds ^ attemptNumber</c>.
/// After <see cref="RetryCount"/> retries the message is moved to the dead-letter
/// queue (<c>file-uploaded-processing_error</c>) for manual inspection.
///
/// <para><b>Concurrency:</b> <see cref="PrefetchCount"/> controls how many unacknowledged messages RabbitMQ
/// delivers to this consumer at once. Set to 1 for strictly ordered processing;
/// increase for higher throughput when order does not matter.</para>
/// </summary>
public sealed class WorkerMessagingOptions
{
    /// <summary>The configuration section name that maps to this class.</summary>
    public const string SectionName = "RabbitMQ";

    /// <summary>RabbitMQ hostname or Docker service name. Default: <c>localhost</c>.</summary>
    [Required(ErrorMessage = "RabbitMQ:Host is required.")]
    public string Host { get; set; } = "localhost";

    /// <summary>RabbitMQ virtual host. Default: <c>enterpriselink</c>.</summary>
    [Required(ErrorMessage = "RabbitMQ:VirtualHost is required.")]
    public string VirtualHost { get; set; } = "enterpriselink";

    /// <summary>RabbitMQ username. Default: <c>eluser</c>.</summary>
    [Required(ErrorMessage = "RabbitMQ:Username is required.")]
    public string Username { get; set; } = "eluser";

    /// <summary>RabbitMQ password. Default: <c>elpassword</c>.</summary>
    [Required(ErrorMessage = "RabbitMQ:Password is required.")]
    public string Password { get; set; } = "elpassword";

    /// <summary>
    /// Number of consumer-level retry attempts on processing failures.
    /// Default: <c>3</c>. Range: 1–10.
    /// </summary>
    [Range(1, 10, ErrorMessage = "RabbitMQ:RetryCount must be between 1 and 10.")]
    public int RetryCount { get; set; } = 3;

    /// <summary>
    /// Base interval in seconds for exponential back-off.
    /// Effective waits: <c>RetryIntervalSeconds^1</c>, <c>^2</c>, <c>^3</c>, …
    /// Default: <c>5</c> (gives 5 s → 25 s → 125 s).
    /// Range: 1–60.
    /// </summary>
    [Range(1, 60, ErrorMessage = "RabbitMQ:RetryIntervalSeconds must be between 1 and 60.")]
    public int RetryIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Number of unacknowledged messages RabbitMQ delivers to this consumer concurrently.
    /// Default: <c>16</c>. Range: 1–256.
    /// </summary>
    [Range(1, 256, ErrorMessage = "RabbitMQ:PrefetchCount must be between 1 and 256.")]
    public int PrefetchCount { get; set; } = 16;
}
