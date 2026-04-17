using EnterpriseLink.Integration.Configuration;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Integration.Storage;

/// <summary>
/// Local-filesystem implementation of <see cref="IIntegrationFileStore"/>.
/// Files are written to <c>{StorageRoot}/{tenantId}/{uploadId}/{fileName}</c>.
/// </summary>
public sealed class LocalIntegrationFileStore : IIntegrationFileStore
{
    private readonly string _root;
    private readonly ILogger<LocalIntegrationFileStore> _logger;

    public LocalIntegrationFileStore(
        IOptions<IntegrationOptions> options,
        ILogger<LocalIntegrationFileStore> logger)
    {
        _root = options.Value.StorageRoot;
        _logger = logger;
    }

    public async Task<string> WriteAsync(
        Guid tenantId,
        Guid uploadId,
        string fileName,
        string csvContent,
        CancellationToken cancellationToken = default)
    {
        var (relativePath, absolutePath) = BuildPaths(tenantId, uploadId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await File.WriteAllTextAsync(absolutePath, csvContent, cancellationToken);

        _logger.LogInformation(
            "Integration file written. Path={RelativePath} Bytes={Bytes}",
            relativePath, csvContent.Length);

        return relativePath;
    }

    public async Task<string> WriteBytesAsync(
        Guid tenantId,
        Guid uploadId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var (relativePath, absolutePath) = BuildPaths(tenantId, uploadId, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await File.WriteAllBytesAsync(absolutePath, content, cancellationToken);

        _logger.LogInformation(
            "Integration file (bytes) written. Path={RelativePath} Bytes={Bytes}",
            relativePath, content.Length);

        return relativePath;
    }

    public string ResolveAbsolutePath(string relativePath) =>
        Path.Combine(_root, relativePath);

    private (string relative, string absolute) BuildPaths(Guid tenantId, Guid uploadId, string fileName)
    {
        var safeName = Path.GetFileName(fileName); // strip any directory traversal
        var relative = Path.Combine(tenantId.ToString(), uploadId.ToString(), safeName);
        var absolute = Path.Combine(_root, relative);
        return (relative, absolute);
    }
}
