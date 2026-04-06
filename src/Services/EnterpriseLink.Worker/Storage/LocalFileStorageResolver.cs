using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Storage;

/// <summary>
/// <see cref="IFileStorageResolver"/> implementation for local filesystem storage.
///
/// <para>
/// Joins the configured <see cref="FileStorageResolverOptions.BasePath"/> with the
/// relative path from <c>FileUploadedEvent.StoragePath</c> to produce the absolute
/// path that the <c>CsvStreamingParser</c> reads from.
/// </para>
///
/// <para><b>Path traversal prevention</b>
/// The resolved full path is verified to start with the canonical <c>BasePath</c>
/// using <see cref="Path.GetFullPath(string)"/>. Any relative path containing <c>../</c>
/// sequences that would escape the storage root is rejected with
/// <see cref="ArgumentException"/>, even if the sequences cancel each other out.
/// </para>
///
/// <para><b>Shared storage root</b>
/// Both the Ingestion service (writer) and the Worker service (reader) must point
/// to the same physical directory. In Docker Compose this is achieved via a shared
/// volume; on bare metal via a shared UNC path or NFS mount.
/// </para>
/// </summary>
public sealed class LocalFileStorageResolver : IFileStorageResolver
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageResolver> _logger;

    /// <summary>
    /// Initialises the resolver and resolves <c>BasePath</c> to its canonical
    /// absolute form.
    /// </summary>
    /// <param name="options">Storage resolver options.</param>
    /// <param name="logger">Structured logger.</param>
    public LocalFileStorageResolver(
        IOptions<FileStorageResolverOptions> options,
        ILogger<LocalFileStorageResolver> logger)
    {
        _logger = logger;

        // Resolve relative paths against the application base directory so that
        // "uploads" in config means <app-dir>/uploads, matching the Ingestion service.
        var configured = options.Value.BasePath;
        _basePath = Path.GetFullPath(
            Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, configured));

        _logger.LogInformation(
            "LocalFileStorageResolver initialised. BasePath={BasePath}", _basePath);
    }

    /// <inheritdoc />
    public string ResolveFullPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException(
                "relativePath must not be null or whitespace.", nameof(relativePath));

        // Combine and canonicalise. GetFullPath collapses any ../  sequences.
        var candidate = Path.GetFullPath(Path.Combine(_basePath, relativePath));

        // Security: the resolved path must remain inside the configured storage root.
        // Without this check, a crafted relativePath could escape to read arbitrary files.
        if (!candidate.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Path traversal attempt blocked. RelativePath={RelativePath} " +
                "ResolvedPath={ResolvedPath} BasePath={BasePath}",
                relativePath, candidate, _basePath);

            throw new ArgumentException(
                $"The relative path '{relativePath}' resolves outside the configured " +
                $"storage root '{_basePath}'. Path traversal is not permitted.",
                nameof(relativePath));
        }

        _logger.LogDebug(
            "Resolved storage path. RelativePath={RelativePath} FullPath={FullPath}",
            relativePath, candidate);

        return candidate;
    }
}
