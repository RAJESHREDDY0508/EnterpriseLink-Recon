using System.Text;
using EnterpriseLink.Integration.Configuration;
using EnterpriseLink.Integration.Messaging;
using EnterpriseLink.Integration.Storage;
using EnterpriseLink.Integration.Transformation;
using Renci.SshNet;

namespace EnterpriseLink.Integration.Adapters.Sftp;

/// <summary>
/// Background service that connects to one or more SFTP servers on a configurable
/// schedule, downloads new CSV files, stores them, and publishes a
/// <c>FileUploadedEvent</c> so the Worker processes them through the standard pipeline.
///
/// <para><b>Acceptance criterion:</b> File ingestion supported.</para>
/// <list type="bullet">
///   <item>Connects via password or private-key authentication (SSH.NET).</item>
///   <item>Filters remote files by <c>FilePattern</c> glob.</item>
///   <item>Skips already-processed files using <see cref="SftpProcessedFileStore"/>.</item>
///   <item>Optionally moves processed files to <c>ArchivePath</c>.</item>
///   <item>Publishes <c>FileUploadedEvent</c> → Worker ingests data.</item>
/// </list>
/// </summary>
public sealed class SftpConnectorService : BackgroundService
{
    private readonly IReadOnlyList<SftpConnectorOptions> _connectors;
    private readonly CsvPassThroughTransformer _transformer;
    private readonly IIntegrationFileStore _fileStore;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<SftpConnectorService> _logger;
    private readonly string _storageRoot;

    public SftpConnectorService(
        IConfiguration configuration,
        CsvPassThroughTransformer transformer,
        IIntegrationFileStore fileStore,
        IIntegrationEventPublisher publisher,
        ILogger<SftpConnectorService> logger)
    {
        _connectors  = configuration.GetSection("SftpConnectors")
                           .Get<List<SftpConnectorOptions>>() ?? [];
        _transformer = transformer;
        _fileStore   = fileStore;
        _publisher   = publisher;
        _logger      = logger;
        _storageRoot = configuration.GetSection("Integration")
                           .GetValue<string>("StorageRoot") ?? "integration-files";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _connectors.Where(c => c.Enabled).ToList();

        if (enabled.Count == 0)
        {
            _logger.LogInformation("SftpConnectorService: no enabled connectors configured — idle.");
            return;
        }

        _logger.LogInformation(
            "SftpConnectorService starting {Count} connector(s): {Names}",
            enabled.Count, string.Join(", ", enabled.Select(c => c.Name)));

        var tasks = enabled.Select(c => PollLoopAsync(c, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task PollLoopAsync(SftpConnectorOptions connector, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(
            connector.PollingIntervalSeconds > 0 ? connector.PollingIntervalSeconds : 60);

        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "SFTP connector '{Name}' polling every {Interval}s",
            connector.Name, interval.TotalSeconds);

        do
        {
            await RunCycleAsync(connector, ct);
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    internal async Task RunCycleAsync(SftpConnectorOptions connector, CancellationToken ct)
    {
        _logger.LogInformation("SFTP connector '{Name}' — starting cycle", connector.Name);

        var processedStore = new SftpProcessedFileStore(
            _storageRoot,
            _logger as ILogger<SftpProcessedFileStore>
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<SftpProcessedFileStore>.Instance);

        var alreadyProcessed = await processedStore.LoadProcessedAsync(connector.Name, ct);

        try
        {
            var authMethod = BuildAuthMethod(connector);
            var connInfo = new Renci.SshNet.ConnectionInfo(
                connector.Host,
                connector.Port,
                connector.Username,
                authMethod)
            {
                Timeout = TimeSpan.FromSeconds(connector.ConnectionTimeoutSeconds),
            };
            using var sftp = new SftpClient(connInfo);

            sftp.Connect();
            _logger.LogInformation(
                "SFTP connector '{Name}' — connected to {Host}:{Port}",
                connector.Name, connector.Host, connector.Port);

            var remoteFiles = sftp
                .ListDirectory(connector.RemotePath)
                .Where(f => f.IsRegularFile &&
                            MatchesPattern(f.Name, connector.FilePattern) &&
                            !alreadyProcessed.Contains(f.FullName))
                .Take(connector.MaxFilesPerCycle)
                .ToList();

            _logger.LogInformation(
                "SFTP connector '{Name}' — {Count} new file(s) to process",
                connector.Name, remoteFiles.Count);

            foreach (var remoteFile in remoteFiles)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    await ProcessFileAsync(sftp, connector, remoteFile.FullName,
                        remoteFile.Name, processedStore, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "SFTP connector '{Name}' — failed to process file '{File}'",
                        connector.Name, remoteFile.FullName);
                }
            }

            sftp.Disconnect();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SFTP connector '{Name}' — cycle failed. Will retry on next interval.",
                connector.Name);
        }
    }

    private async Task ProcessFileAsync(
        SftpClient sftp,
        SftpConnectorOptions connector,
        string remotePath,
        string fileName,
        SftpProcessedFileStore processedStore,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "SFTP connector '{Name}' — downloading '{File}'", connector.Name, remotePath);

        using var stream = new MemoryStream();
        sftp.DownloadFile(remotePath, stream);
        var bytes = stream.ToArray();
        var csvText = Encoding.UTF8.GetString(bytes);

        // Transform (pass-through — adds SourceSystem column if absent)
        var result = _transformer.Transform(
            csvText, [], connector.SourceSystem, connector.Name);

        var uploadId = Guid.NewGuid();
        string storagePath;

        if (result.RowCount == 0)
        {
            // Still store the raw bytes so the Worker can inspect the file
            storagePath = await _fileStore.WriteBytesAsync(
                connector.TenantId, uploadId, fileName, bytes, ct);
        }
        else
        {
            storagePath = await _fileStore.WriteAsync(
                connector.TenantId, uploadId, fileName, result.CsvContent, ct);
        }

        await _publisher.PublishFileUploadedAsync(
            connector.TenantId, uploadId, storagePath,
            fileName, bytes.Length,
            result.RowCount, connector.SourceSystem, ct);

        _logger.LogInformation(
            "SFTP connector '{Name}' — published UploadId={UploadId} Rows={Rows} File='{File}'",
            connector.Name, uploadId, result.RowCount, fileName);

        // Archive on SFTP server if configured
        if (!string.IsNullOrWhiteSpace(connector.ArchivePath))
        {
            try
            {
                var archiveDest = connector.ArchivePath.TrimEnd('/') + "/" + fileName;
                sftp.RenameFile(remotePath, archiveDest);
                _logger.LogInformation(
                    "SFTP connector '{Name}' — archived '{File}' → '{Dest}'",
                    connector.Name, remotePath, archiveDest);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SFTP connector '{Name}' — could not archive '{File}' " +
                    "(already processed marker will prevent re-download)",
                    connector.Name, remotePath);
            }
        }

        // Mark as processed regardless of archival outcome
        await processedStore.MarkProcessedAsync(connector.Name, remotePath, ct);
    }

    private static AuthenticationMethod BuildAuthMethod(SftpConnectorOptions connector)
    {
        if (!string.IsNullOrWhiteSpace(connector.PrivateKeyPath))
        {
            var keyFile = string.IsNullOrWhiteSpace(connector.Password)
                ? new PrivateKeyFile(connector.PrivateKeyPath)
                : new PrivateKeyFile(connector.PrivateKeyPath, connector.Password);
            return new PrivateKeyAuthenticationMethod(connector.Username, keyFile);
        }

        return new PasswordAuthenticationMethod(connector.Username, connector.Password);
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern == "*") return true;

        // Simple glob: support leading/trailing wildcards only
        if (pattern.StartsWith('*') && pattern.EndsWith('*'))
        {
            var inner = pattern.Trim('*');
            return fileName.Contains(inner, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.StartsWith('*'))
        {
            var suffix = pattern.TrimStart('*');
            return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        if (pattern.EndsWith('*'))
        {
            var prefix = pattern.TrimEnd('*');
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
