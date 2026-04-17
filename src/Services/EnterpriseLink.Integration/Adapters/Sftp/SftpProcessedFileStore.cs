using System.Text.Json;

namespace EnterpriseLink.Integration.Adapters.Sftp;

/// <summary>
/// Tracks which remote SFTP files have already been downloaded and ingested,
/// preventing duplicate processing across polling cycles.
///
/// <para>
/// State is persisted as a JSON file on the local filesystem under
/// <c>{storageRoot}/.sftp-state/{connectorName}.json</c>.
/// </para>
/// </summary>
public sealed class SftpProcessedFileStore
{
    private readonly string _stateDirectory;
    private readonly ILogger<SftpProcessedFileStore> _logger;

    public SftpProcessedFileStore(string storageRoot, ILogger<SftpProcessedFileStore> logger)
    {
        _stateDirectory = Path.Combine(storageRoot, ".sftp-state");
        _logger         = logger;
        Directory.CreateDirectory(_stateDirectory);
    }

    /// <summary>
    /// Returns the set of remote file paths already processed by <paramref name="connectorName"/>.
    /// </summary>
    public async Task<HashSet<string>> LoadProcessedAsync(
        string connectorName,
        CancellationToken ct = default)
    {
        var path = StatePath(connectorName);
        if (!File.Exists(path)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<HashSet<string>>(json) ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SftpProcessedFileStore: failed to load state for '{Connector}' — " +
                "treating all files as unprocessed", connectorName);
            return [];
        }
    }

    /// <summary>
    /// Adds <paramref name="remoteFilePath"/> to the processed set and persists the state.
    /// </summary>
    public async Task MarkProcessedAsync(
        string connectorName,
        string remoteFilePath,
        CancellationToken ct = default)
    {
        var processed = await LoadProcessedAsync(connectorName, ct);
        processed.Add(remoteFilePath);

        var path = StatePath(connectorName);
        await File.WriteAllTextAsync(
            path,
            JsonSerializer.Serialize(processed, new JsonSerializerOptions { WriteIndented = true }),
            ct);
    }

    private string StatePath(string connectorName) =>
        Path.Combine(_stateDirectory, $"{connectorName}.json");
}
