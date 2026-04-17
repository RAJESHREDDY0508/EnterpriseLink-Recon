using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Integration.Configuration;

/// <summary>
/// Configuration for a single SFTP connector instance.
/// </summary>
public sealed class SftpConnectorOptions
{
    /// <summary>Logical name used in logs and manual trigger paths.</summary>
    [Required] public string Name { get; init; } = string.Empty;

    /// <summary>Internal EnterpriseLink tenant all ingested records belong to.</summary>
    [Required] public Guid TenantId { get; init; }

    /// <summary>SFTP server hostname or IP address.</summary>
    [Required] public string Host { get; init; } = string.Empty;

    /// <summary>SSH/SFTP port. Default: 22.</summary>
    public int Port { get; init; } = 22;

    /// <summary>SFTP username.</summary>
    [Required] public string Username { get; init; } = string.Empty;

    /// <summary>SFTP password (set via user-secrets in dev; environment variable in prod).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>
    /// Path of the private key file for key-based authentication.
    /// When set, <see cref="Password"/> is used as the key passphrase (if any).
    /// </summary>
    public string PrivateKeyPath { get; init; } = string.Empty;

    /// <summary>Remote directory path to scan for new files.</summary>
    [Required] public string RemotePath { get; init; } = "/";

    /// <summary>
    /// File name glob pattern. Only matching files are downloaded.
    /// E.g. <c>*.csv</c>, <c>transactions_*.csv</c>.
    /// </summary>
    public string FilePattern { get; init; } = "*.csv";

    /// <summary>
    /// Remote path to move processed files to after successful ingestion.
    /// Leave empty to leave files in place (relies on the processed-file store
    /// to avoid reprocessing).
    /// </summary>
    public string ArchivePath { get; init; } = string.Empty;

    /// <summary>Polling interval in seconds.</summary>
    public int PollingIntervalSeconds { get; init; } = 60;

    /// <summary>Whether this connector is active.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Value written to the <c>SourceSystem</c> column.</summary>
    [Required] public string SourceSystem { get; init; } = string.Empty;

    /// <summary>Connection timeout in seconds.</summary>
    public int ConnectionTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Maximum number of files to download per polling cycle.
    /// Prevents a single cycle from consuming excessive I/O when a large backlog exists.
    /// </summary>
    public int MaxFilesPerCycle { get; init; } = 50;
}
