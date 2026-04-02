namespace EnterpriseLink.Ingestion.Storage;

/// <summary>
/// Immutable result returned by <see cref="IFileStorageService.StoreAsync"/> after a file
/// has been durably written to the backing store.
///
/// <para>
/// The design intentionally separates the relative path (safe to surface in API responses
/// and audit logs) from the full path or URI (kept server-side for operational use only).
/// </para>
/// </summary>
/// <param name="RelativePath">
/// Tenant-scoped, upload-scoped relative path within the storage root.
/// Format: <c>{tenantId}/{uploadId}/{sanitisedFileName}</c>.
/// <para>This value is safe to include in API responses and audit records.</para>
/// </param>
/// <param name="FullPath">
/// Absolute filesystem path (local provider) or fully-qualified blob URI (Azure Blob
/// Storage provider). <b>Do not surface this value in API responses</b> — it may
/// expose infrastructure topology.
/// </param>
/// <param name="Provider">
/// Storage provider identifier. Currently <c>"local"</c>. Will be <c>"azureblob"</c>
/// when the Azure provider is wired in a future story.
/// </param>
/// <param name="StoredAt">UTC timestamp at which the write operation completed.</param>
public sealed record StorageResult(
    string RelativePath,
    string FullPath,
    string Provider,
    DateTimeOffset StoredAt);
