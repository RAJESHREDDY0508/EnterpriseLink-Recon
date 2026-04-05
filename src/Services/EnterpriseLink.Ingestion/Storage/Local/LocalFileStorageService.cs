using Microsoft.Extensions.Options;

namespace EnterpriseLink.Ingestion.Storage.Local;

/// <summary>
/// Stores uploaded files on the local filesystem.
///
/// <para>
/// This provider is the default for development and on-premises deployments.
/// For production cloud deployments, swap to the Azure Blob Storage provider
/// (future story) by changing <c>FileStorage:Provider</c> to <c>azureblob</c>
/// — no controller or validation code changes required.
/// </para>
///
/// <para><b>Directory layout</b></para>
/// <code>
/// {BasePath}/
///   └── {tenantId}/            ← tenant isolation at the filesystem level
///         └── {uploadId}/      ← one directory per upload session
///               └── {fileName} ← sanitised original file name
/// </code>
///
/// <para><b>Streaming</b></para>
/// Content is copied via <see cref="Stream.CopyToAsync(Stream, CancellationToken)"/> —
/// the full file is never loaded into a <c>byte[]</c> or <c>string</c>. Memory use
/// is bounded by the internal buffer size (80 KB by default).
///
/// <para><b>Concurrency</b></para>
/// Because every upload writes to a unique <c>{uploadId}</c> directory, parallel
/// uploads from the same tenant never contend on the same path.
///
/// <para><b>Partial-file cleanup</b></para>
/// If <see cref="StoreAsync"/> is cancelled or throws after creating the target file,
/// the incomplete file is deleted before the exception propagates. This prevents
/// orphaned zero- or partial-byte files from accumulating on disk.
/// </summary>
public sealed class LocalFileStorageService : IFileStorageService
{
    private readonly string _basePath;
    private readonly ILogger<LocalFileStorageService> _logger;

    /// <summary>
    /// Initialises the service and ensures the configured base directory exists.
    /// </summary>
    /// <param name="options">
    /// Strongly-typed local storage options providing the <c>BasePath</c>.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>BasePath</c> points to an existing file (not a directory).
    /// A misconfigured path would cause all subsequent file writes to fail with
    /// unhelpful OS errors, so the service fails fast at construction time.
    /// </exception>
    public LocalFileStorageService(
        IOptions<LocalStorageOptions> options,
        ILogger<LocalFileStorageService> logger)
    {
        _logger = logger;

        // Resolve relative paths against the application base directory so that
        // "uploads" in config means <app-dir>/uploads, not the OS working directory.
        _basePath = Path.IsPathRooted(options.Value.BasePath)
            ? options.Value.BasePath
            : Path.Combine(AppContext.BaseDirectory, options.Value.BasePath);

        // Guard: BasePath must not already exist as a plain file. If it does, every
        // Directory.CreateDirectory call will throw an opaque UnauthorizedAccessException.
        // Detecting the misconfiguration at startup produces a clear failure message.
        if (File.Exists(_basePath))
        {
            throw new InvalidOperationException(
                $"LocalFileStorageService: BasePath '{_basePath}' exists as a file. " +
                "BasePath must be a directory. Update 'FileStorage:Local:BasePath' in configuration.");
        }

        _logger.LogInformation(
            "LocalFileStorageService initialised. BasePath={BasePath}", _basePath);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para><b>Path construction</b></para>
    /// <c>relativePath = {tenantId}/{uploadId}/{sanitisedFileName}</c><br/>
    /// <c>fullPath = {BasePath}/{relativePath}</c>
    ///
    /// <para><b>File name sanitisation</b></para>
    /// Only the <c>Path.GetFileName</c> component is used — any directory
    /// traversal sequences in the original file name are stripped.
    /// </remarks>
    public async Task<StorageResult> StoreAsync(
        Guid tenantId,
        Guid uploadId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        // Sanitise: strip path components to prevent directory-traversal attacks.
        var safeFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = $"{uploadId}.csv";

        // Construct tenant-scoped, upload-scoped path.
        var relativePath = Path.Combine(
            tenantId.ToString(),
            uploadId.ToString(),
            safeFileName);

        var fullPath = Path.Combine(_basePath, relativePath);

        // Ensure all intermediate directories exist before writing.
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);

        _logger.LogDebug(
            "Writing file to local storage. FullPath={FullPath} FileSizeBytes={FileSizeBytes}",
            fullPath, file.Length);

        // Stream the file content — never load entirely into memory.
        // Any exception (cancellation, disk-full, I/O error) after the file is created
        // leaves an incomplete file on disk. The finally block deletes it so callers
        // never see a partial write that might be mistaken for a valid stored file.
        var fileCreated = false;
        try
        {
            await using var sourceStream = file.OpenReadStream();
            await using var targetStream = new FileStream(
                fullPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81_920, // 80 KB — default CopyToAsync buffer
                useAsync: true);

            fileCreated = true;
            await sourceStream.CopyToAsync(targetStream, cancellationToken);
        }
        catch
        {
            // Attempt to remove the partial file so the caller can retry cleanly.
            if (fileCreated && File.Exists(fullPath))
            {
                try
                {
                    File.Delete(fullPath);
                    _logger.LogWarning(
                        "Partial file deleted after failed write. FullPath={FullPath}",
                        fullPath);
                }
                catch (Exception deleteEx)
                {
                    // Log but do not mask the original exception.
                    _logger.LogError(deleteEx,
                        "Failed to delete partial file after write error. " +
                        "Manual cleanup required. FullPath={FullPath}", fullPath);
                }
            }

            throw;
        }

        var storedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "File stored successfully. RelativePath={RelativePath} TenantId={TenantId} UploadId={UploadId}",
            relativePath, tenantId, uploadId);

        return new StorageResult(
            RelativePath: relativePath,
            FullPath: fullPath,
            Provider: "local",
            StoredAt: storedAt);
    }
}
