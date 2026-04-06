namespace EnterpriseLink.Worker.Storage;

/// <summary>
/// Resolves a relative storage path (as recorded in <c>FileUploadedEvent.StoragePath</c>)
/// to an absolute filesystem path that the Worker can open for reading.
///
/// <para>
/// The Ingestion service stores files at
/// <c>{BasePath}/{tenantId}/{uploadId}/{fileName}</c> and publishes only the relative
/// portion (<c>{tenantId}/{uploadId}/{fileName}</c>) in the event. The Worker must
/// combine this with the shared storage root to obtain the full path.
/// </para>
///
/// <para>
/// In local development and on-premises deployments both services share the same
/// filesystem, so the resolver simply joins <c>BasePath</c> with the relative path.
/// In cloud deployments (future story) the resolver would download the blob from
/// Azure Blob Storage to a temp file before returning its path.
/// </para>
/// </summary>
public interface IFileStorageResolver
{
    /// <summary>
    /// Combines the configured storage root with <paramref name="relativePath"/>
    /// and returns the resulting absolute path.
    /// </summary>
    /// <param name="relativePath">
    /// Relative storage path from <c>FileUploadedEvent.StoragePath</c>.
    /// Must be a valid relative path in the format
    /// <c>{tenantId}/{uploadId}/{fileName}</c>. Must not be null or empty.
    /// </param>
    /// <returns>
    /// The absolute path where the CSV file can be opened for reading.
    /// The file is not guaranteed to exist — callers must verify existence
    /// before reading.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="relativePath"/> is null, empty, or contains
    /// path-traversal sequences that would escape the configured storage root.
    /// </exception>
    string ResolveFullPath(string relativePath);
}
