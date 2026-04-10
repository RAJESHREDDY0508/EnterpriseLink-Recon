using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Dashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseLink.Dashboard.Controllers;

/// <summary>
/// Exposes upload batch status for the Batch Monitor dashboard module.
///
/// <para>
/// All endpoints are read-only and cross-tenant. No authentication is required
/// for the Dashboard service — it is deployed behind the API Gateway which enforces
/// network-level access control.
/// </para>
///
/// <para><b>Story 1 — Batch Monitoring API</b></para>
/// <para>Acceptance criterion: <b>Batch status exposed</b></para>
/// <list type="bullet">
///   <item><description><c>GET /api/uploads</c> — paginated list with optional status and tenant filters.</description></item>
///   <item><description><c>GET /api/uploads/{uploadId}</c> — single upload detail by idempotency key.</description></item>
/// </list>
/// </summary>
[ApiController]
[Route("api/uploads")]
[Produces("application/json")]
public sealed class BatchMonitorController : ControllerBase
{
    private readonly IBatchMonitorService _service;
    private readonly ILogger<BatchMonitorController> _logger;

    /// <summary>
    /// Initialises the controller with its required dependencies.
    /// </summary>
    /// <param name="service">Batch monitor query service.</param>
    /// <param name="logger">Structured logger.</param>
    public BatchMonitorController(
        IBatchMonitorService service,
        ILogger<BatchMonitorController> logger)
    {
        _service = service;
        _logger = logger;
    }

    // ── GET /api/uploads ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of upload records ordered by creation time (most recent first).
    ///
    /// <para>
    /// Supports optional query-string filters for <c>status</c>
    /// (<c>Processing</c> | <c>Completed</c> | <c>Failed</c>) and <c>tenantId</c>.
    /// Omitting both filters returns all uploads across all tenants.
    /// </para>
    /// </summary>
    /// <param name="query">Pagination and filter parameters.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// <c>200 OK</c> with a <see cref="PagedResult{T}"/> of <see cref="ProcessedUploadSummaryDto"/>.
    /// </returns>
    /// <response code="200">Paginated upload list returned.</response>
    /// <response code="400">Query parameters failed validation (e.g. page &lt; 1).</response>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<ProcessedUploadSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetUploadsAsync(
        [FromQuery] BatchMonitorQuery query,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "BatchMonitor: listing uploads. Page={Page} PageSize={PageSize} Status={Status} TenantId={TenantId}",
            query.Page, query.PageSize, query.Status, query.TenantId);

        var result = await _service.GetUploadsAsync(query, cancellationToken);
        return Ok(result);
    }

    // ── GET /api/uploads/{uploadId} ───────────────────────────────────────────

    /// <summary>
    /// Returns the status and metadata for a single upload identified by its idempotency key.
    /// </summary>
    /// <param name="uploadId">The <c>FileUploadedEvent.UploadId</c> to look up.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>
    /// <c>200 OK</c> with <see cref="ProcessedUploadSummaryDto"/> on success.<br/>
    /// <c>404 Not Found</c> if no upload with this ID exists.
    /// </returns>
    /// <response code="200">Upload record returned.</response>
    /// <response code="404">No upload with the specified ID was found.</response>
    [HttpGet("{uploadId:guid}")]
    [ProducesResponseType(typeof(ProcessedUploadSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUploadByIdAsync(
        Guid uploadId,
        CancellationToken cancellationToken)
    {
        var upload = await _service.GetUploadByIdAsync(uploadId, cancellationToken);

        if (upload is null)
        {
            _logger.LogWarning("BatchMonitor: upload {UploadId} not found.", uploadId);
            return NotFound(new { error = $"Upload '{uploadId}' not found." });
        }

        return Ok(upload);
    }
}
