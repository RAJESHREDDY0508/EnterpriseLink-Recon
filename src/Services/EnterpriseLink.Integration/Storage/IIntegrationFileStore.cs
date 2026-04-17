namespace EnterpriseLink.Integration.Storage;

/// <summary>
/// Writes adapter-produced CSV content to local storage so it can be
/// published as a <c>FileUploadedEvent</c> and processed by the Worker service.
/// </summary>
public interface IIntegrationFileStore
{
    /// <summary>
    /// Writes <paramref name="csvContent"/> to an isolated
    /// <c>{tenantId}/{uploadId}/{fileName}</c> path under the configured storage root
    /// and returns the relative path for use in the event payload.
    /// </summary>
    Task<string> WriteAsync(
        Guid tenantId,
        Guid uploadId,
        string fileName,
        string csvContent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes raw bytes (e.g. a downloaded SFTP file) to isolated storage.
    /// Returns the relative path.
    /// </summary>
    Task<string> WriteBytesAsync(
        Guid tenantId,
        Guid uploadId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a relative storage path to its absolute filesystem path.</summary>
    string ResolveAbsolutePath(string relativePath);
}
