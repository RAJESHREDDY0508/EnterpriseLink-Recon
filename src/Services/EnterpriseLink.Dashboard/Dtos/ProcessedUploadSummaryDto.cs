namespace EnterpriseLink.Dashboard.Dtos;

/// <summary>
/// Read-only projection of a <c>ProcessedUpload</c> row for the Batch Monitor dashboard.
///
/// <para>
/// Contains the fields required for operational visibility: upload identity,
/// owning tenant, lifecycle status, row count, source system, and timestamps.
/// Internal EF navigation properties and concurrency tokens are intentionally
/// excluded.
/// </para>
/// </summary>
/// <param name="UploadId">Idempotency key — matches the originating <c>FileUploadedEvent.UploadId</c>.</param>
/// <param name="TenantId">The tenant that owns this upload batch.</param>
/// <param name="Status">Processing lifecycle state: <c>Processing</c>, <c>Completed</c>, or <c>Failed</c>.</param>
/// <param name="RowsInserted">Number of <c>Transaction</c> rows committed. Zero while <c>Processing</c>.</param>
/// <param name="SourceSystem">Upstream system that produced the CSV file (e.g. "SAP", "Salesforce").</param>
/// <param name="CreatedAt">UTC timestamp when the upload record was first created (claim time).</param>
/// <param name="UpdatedAt">UTC timestamp of the most recent status update.</param>
public sealed record ProcessedUploadSummaryDto(
    Guid UploadId,
    Guid TenantId,
    string Status,
    int RowsInserted,
    string SourceSystem,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
