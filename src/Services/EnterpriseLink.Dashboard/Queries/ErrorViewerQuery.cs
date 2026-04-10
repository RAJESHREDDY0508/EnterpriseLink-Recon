using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Dashboard.Queries;

/// <summary>
/// Query parameters for the Error Viewer endpoints:
/// <c>GET /api/errors</c> and <c>GET /api/uploads/{uploadId}/errors</c>.
///
/// <para>
/// Filters can be combined. For example, supplying both <see cref="TenantId"/> and
/// <see cref="FailureReason"/> narrows the result to schema failures for a specific tenant.
/// </para>
/// </summary>
public sealed class ErrorViewerQuery
{
    /// <summary>1-based page number. Defaults to <c>1</c>.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Page must be 1 or greater.")]
    public int Page { get; init; } = 1;

    /// <summary>
    /// Maximum records per page. Capped at 100. Defaults to <c>20</c>.
    /// </summary>
    [Range(1, 100, ErrorMessage = "PageSize must be between 1 and 100.")]
    public int PageSize { get; init; } = 20;

    /// <summary>
    /// Optional upload filter. When supplied, only errors from this specific upload are returned.
    /// Used by <c>GET /api/uploads/{uploadId}/errors</c> and can also be supplied to
    /// <c>GET /api/errors</c> for cross-upload error comparison.
    /// </summary>
    public Guid? UploadId { get; init; }

    /// <summary>
    /// Optional tenant filter. Omit to return errors across all tenants.
    /// </summary>
    public Guid? TenantId { get; init; }

    /// <summary>
    /// Optional failure-reason filter. Accepted values: <c>Schema</c>, <c>BusinessRule</c>,
    /// <c>Duplicate</c>. Case-insensitive. Omit to return all failure types.
    /// </summary>
    public string? FailureReason { get; init; }
}
