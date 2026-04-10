using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;

namespace EnterpriseLink.Dashboard.Services;

/// <summary>
/// Provides read-only access to validation errors for the Error Viewer dashboard.
///
/// <para>
/// Queries the <c>InvalidTransactions</c> table with cross-tenant visibility using
/// <c>IgnoreQueryFilters()</c>. Optional per-request tenant and upload filtering is
/// applied by the implementation when the corresponding query parameters are set.
/// </para>
///
/// <para><b>Acceptance criterion: Validation errors queryable</b></para>
/// </summary>
public interface IErrorViewerService
{
    /// <summary>
    /// Returns a paginated list of invalid transaction records, optionally filtered by
    /// upload, tenant, and failure reason. Results are ordered by <c>RowNumber</c> ascending
    /// so operators can walk through the source file in order.
    /// </summary>
    /// <param name="query">Filter and pagination parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A page of <see cref="InvalidTransactionDto"/> records.</returns>
    Task<PagedResult<InvalidTransactionDto>> GetErrorsAsync(
        ErrorViewerQuery query,
        CancellationToken cancellationToken = default);
}
