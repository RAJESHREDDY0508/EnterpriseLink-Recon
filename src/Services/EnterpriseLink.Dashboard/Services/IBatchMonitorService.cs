using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;

namespace EnterpriseLink.Dashboard.Services;

/// <summary>
/// Provides read-only access to upload batch status for the Batch Monitor dashboard.
///
/// <para>
/// Queries the <c>ProcessedUploads</c> table with cross-tenant visibility (no tenant
/// isolation filter). Optional per-request tenant filtering is applied by the
/// implementation when <see cref="BatchMonitorQuery.TenantId"/> is supplied.
/// </para>
///
/// <para><b>Acceptance criterion: Batch status exposed</b></para>
/// </summary>
public interface IBatchMonitorService
{
    /// <summary>
    /// Returns a paginated list of upload records, optionally filtered by status and tenant.
    /// Results are ordered by <c>CreatedAt</c> descending (most recent first).
    /// </summary>
    /// <param name="query">Filter and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of <see cref="ProcessedUploadSummaryDto"/> records.</returns>
    Task<PagedResult<ProcessedUploadSummaryDto>> GetUploadsAsync(
        BatchMonitorQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single upload record by its idempotency key.
    /// </summary>
    /// <param name="uploadId">The <c>FileUploadedEvent.UploadId</c> to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matching <see cref="ProcessedUploadSummaryDto"/>, or <c>null</c> if not found.</returns>
    Task<ProcessedUploadSummaryDto?> GetUploadByIdAsync(
        Guid uploadId,
        CancellationToken cancellationToken = default);
}
