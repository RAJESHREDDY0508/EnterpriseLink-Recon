namespace EnterpriseLink.Ingestion.Models;

/// <summary>
/// Response payload returned by <c>POST /api/ingestion/upload</c> on success.
///
/// <para>
/// The <see cref="UploadId"/> is the stable reference clients use to poll
/// for processing status once the Worker service picks up the job.
/// </para>
///
/// <para>
/// The <see cref="StoragePath"/> is the tenant-scoped relative path at which the raw
/// file has been persisted. It is safe to log and return to callers; it does not
/// expose filesystem topology or absolute server paths.
/// </para>
/// </summary>
/// <param name="UploadId">
/// Unique identifier for this upload session. Clients present this GUID to query
/// processing status, retrieve row-level errors, and audit the ingestion lifecycle.
/// </param>
/// <param name="TenantId">
/// Internal EnterpriseLink tenant identifier resolved from the caller's JWT
/// <c>tenant_id</c> claim. All rows ingested from this file will be scoped to
/// this tenant.
/// </param>
/// <param name="FileName">Original file name as provided by the client.</param>
/// <param name="FileSizeBytes">File size in bytes as reported by the multipart boundary.</param>
/// <param name="DataRowCount">
/// Number of data rows detected (header row excluded).
/// Determined by streaming through the file — the full content is never held in memory.
/// </param>
/// <param name="SourceSystem">
/// The upstream system of record that produced the file,
/// as provided in the <c>sourceSystem</c> form field.
/// </param>
/// <param name="StoragePath">
/// Relative storage path where the raw file has been persisted.
/// Format: <c>{tenantId}/{uploadId}/{fileName}</c>.
/// <para>
/// For the <c>local</c> provider this is a filesystem path fragment.
/// For the <c>azureblob</c> provider (future) this will be a blob name.
/// </para>
/// </param>
/// <param name="AcceptedAt">
/// UTC timestamp at which the server accepted, validated, and stored the file.
/// </param>
public sealed record UploadResult(
    Guid UploadId,
    Guid TenantId,
    string FileName,
    long FileSizeBytes,
    int DataRowCount,
    string SourceSystem,
    string StoragePath,
    DateTimeOffset AcceptedAt);
