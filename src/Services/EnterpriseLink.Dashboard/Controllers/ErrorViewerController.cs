using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseLink.Dashboard.Controllers;

/// <summary>
/// Exposes validation error data for the Error Viewer dashboard module.
///
/// <para>
/// Two complementary endpoints are provided: an upload-scoped endpoint for drilling
/// into the errors of a specific batch, and a global endpoint for cross-upload
/// error analysis and trend monitoring.
/// </para>
///
/// <para><b>Story 2 — Error Viewer API</b></para>
/// <para>Acceptance criterion: <b>Validation errors queryable</b></para>
/// <list type="bullet">
///   <item><description><c>GET /api/uploads/{uploadId}/errors</c> — errors scoped to a single upload.</description></item>
///   <item><description><c>GET /api/errors</c> — cross-upload error view with optional filters.</description></item>
/// </list>
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class ErrorViewerController : ControllerBase
{
    private readonly IErrorViewerService _service;
    private readonly ILogger<ErrorViewerController> _logger;

    /// <summary>
    /// Initialises the controller with its required dependencies.
    /// </summary>
    /// <param name="service">Error viewer query service.</param>
    /// <param name="logger">Structured logger.</param>
    public ErrorViewerController(
        IErrorViewerService service,
        ILogger<ErrorViewerController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ── GET /api/uploads/{uploadId}/errors ────────────────────────────────────

    /// <summary>
    /// Returns paginated validation errors for a specific upload batch.
    ///
    /// <para>
    /// Results are ordered by row number ascending so operators can walk through
    /// the source CSV file in sequence. Additional filters (<c>failureReason</c>,
    /// <c>tenantId</c>) can be applied via query-string parameters.
    /// </para>
    /// </summary>
    /// <param name="uploadId">The upload whose errors to retrieve.</param>
    /// <param name="query">Additional filter and pagination parameters.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="PagedResult{T}"/> of <see cref="InvalidTransactionDto"/>.
    /// </returns>
    /// <response code="200">Paginated error list returned.</response>
    /// <response code="400">Query parameters failed validation.</response>
    [HttpGet("api/uploads/{uploadId:guid}/errors")]
    [ProducesResponseType(typeof(PagedResult<InvalidTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUploadErrorsAsync(
        Guid uploadId,
        [FromQuery] ErrorViewerQuery query,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ErrorViewer: listing errors for upload {UploadId}. Page={Page} FailureReason={FailureReason}",
            uploadId, query.Page, query.FailureReason);

        // Override UploadId from the route parameter — route value takes precedence.
        var scoped = new ErrorViewerQuery
        {
            Page = query.Page,
            PageSize = query.PageSize,
            UploadId = uploadId,
            TenantId = query.TenantId,
            FailureReason = query.FailureReason,
        };

        var result = await _service.GetErrorsAsync(scoped, cancellationToken);
        return Ok(result);
    }

    // ── GET /api/errors ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated cross-upload view of all validation errors, optionally
    /// filtered by upload, tenant, and failure reason.
    ///
    /// <para>
    /// Useful for identifying systemic validation issues (e.g. a source system
    /// consistently producing negative amounts) that span multiple upload batches.
    /// </para>
    /// </summary>
    /// <param name="query">Filter and pagination parameters.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="PagedResult{T}"/> of <see cref="InvalidTransactionDto"/>.
    /// </returns>
    /// <response code="200">Paginated error list returned.</response>
    /// <response code="400">Query parameters failed validation.</response>
    [HttpGet("api/errors")]
    [ProducesResponseType(typeof(PagedResult<InvalidTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAllErrorsAsync(
        [FromQuery] ErrorViewerQuery query,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ErrorViewer: listing all errors. Page={Page} UploadId={UploadId} " +
            "TenantId={TenantId} FailureReason={FailureReason}",
            query.Page, query.UploadId, query.TenantId, query.FailureReason);

        var result = await _service.GetErrorsAsync(query, cancellationToken);
        return Ok(result);
    }
}
