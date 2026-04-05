using EnterpriseLink.Ingestion.Messaging;
using EnterpriseLink.Ingestion.Models;
using EnterpriseLink.Ingestion.Storage;
using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Shared.Infrastructure.Middleware;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseLink.Ingestion.Controllers;

/// <summary>
/// Handles file ingestion for the EnterpriseLink Recon platform.
///
/// <para>
/// The upload pipeline is designed for high-volume CSV ingestion without blocking the API.
/// Files are accepted, validated, stored, and a row count is streamed. Actual CSV parsing
/// and persistence are dispatched asynchronously to the Worker service via RabbitMQ (Story 3).
/// </para>
///
/// <para><b>Upload pipeline</b></para>
/// <code>
/// Client
///   │  POST /api/ingestion/upload
///   │  Content-Type: multipart/form-data
///   │  Authorization: Bearer {entra-jwt}
///   ▼
/// Kestrel (connection layer)
///   │  Rejects requests larger than MaxFileSizeBytes at the TCP level.
///   │  Files &lt;= MemoryBufferThreshold → held in memory.
///   │  Files &gt; MemoryBufferThreshold → spooled to temp file on disk.
///   ▼
/// IngestionController.UploadAsync()
///   │  1. FluentValidation: extension, content-type, size, metadata
///   │  2. Resolve tenant_id from JWT claim
///   │  3. Stream row count (header excluded)
///   │  4. IFileStorageService.StoreAsync → file persisted at {tenantId}/{uploadId}/{file}
///   │  5. IEventPublisher.PublishAsync → FileUploadedEvent → RabbitMQ
///   │  6. Return UploadResult with UploadId, StoragePath, row count
///   ▼
/// Worker Service — consumes FileUploadedEvent, parses CSV, persists rows
/// </code>
///
/// <para><b>Streaming guarantee</b></para>
/// ASP.NET Core spools files exceeding <c>IngestionOptions.MemoryBufferThresholdBytes</c>
/// to disk. Row counting and storage write both use <c>OpenReadStream()</c> — the full
/// file content is never loaded into a single in-memory string or byte array.
/// </summary>
[ApiController]
[Route("api/ingestion")]
[Produces("application/json")]
public sealed class IngestionController : ControllerBase
{
    private readonly IValidator<FileUploadRequest> _validator;
    private readonly IFileStorageService _storageService;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<IngestionController> _logger;

    /// <summary>
    /// Initialises the controller with its required dependencies.
    /// </summary>
    /// <param name="validator">FluentValidation validator for file upload requests.</param>
    /// <param name="storageService">Storage backend — local or Azure Blob (swappable).</param>
    /// <param name="eventPublisher">Message broker publisher — MassTransit/RabbitMQ.</param>
    /// <param name="logger">Structured logger.</param>
    public IngestionController(
        IValidator<FileUploadRequest> validator,
        IFileStorageService storageService,
        IEventPublisher eventPublisher,
        ILogger<IngestionController> logger)
    {
        _validator = validator;
        _storageService = storageService;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    // ── POST /api/ingestion/upload ────────────────────────────────────────────

    /// <summary>
    /// Accepts a CSV file via multipart/form-data upload, stores it durably,
    /// and returns an <see cref="UploadResult"/> with the storage path and row count.
    ///
    /// <para>
    /// The request body is streamed — the server never buffers the entire file in memory.
    /// Files smaller than the configured <c>MemoryBufferThresholdBytes</c> are held
    /// in memory; larger files are automatically spooled to disk by ASP.NET Core.
    /// </para>
    ///
    /// <para><b>Form fields</b></para>
    /// <list type="table">
    ///   <listheader><term>Field</term><description>Description</description></listheader>
    ///   <item><term>file</term><description>Required. The .csv file to upload.</description></item>
    ///   <item><term>sourceSystem</term><description>Required. Upstream system of record (e.g. "Salesforce").</description></item>
    ///   <item><term>description</term><description>Optional. Free-text batch description.</description></item>
    /// </list>
    /// </summary>
    /// <returns>
    /// <c>200 OK</c> with <see cref="UploadResult"/> on success.<br/>
    /// <c>400 Bad Request</c> if metadata validation fails or file is malformed.<br/>
    /// <c>401 Unauthorized</c> if no valid Entra ID token is provided or tenant is unresolvable.<br/>
    /// <c>413 Payload Too Large</c> if the file exceeds <c>MaxFileSizeBytes</c> (Kestrel level).
    /// </returns>
    /// <response code="200">File accepted and stored; returns session ID, storage path, and row count.</response>
    /// <response code="400">Validation failed; response body contains field/message pairs.</response>
    /// <response code="401">Missing or invalid Bearer token, or missing tenant_id claim.</response>
    /// <response code="413">File exceeds the configured maximum size.</response>
    [HttpPost("upload")]
    [Authorize]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    [ProducesResponseType(typeof(UploadResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413RequestEntityTooLarge)]
    public async Task<IActionResult> UploadAsync(
        [FromForm] FileUploadRequest request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Validate metadata and file properties ─────────────────────
        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "File upload rejected due to validation failures: {Errors}",
                string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)));

            return BadRequest(new
            {
                errors = validation.Errors.Select(e => new
                {
                    field = e.PropertyName,
                    message = e.ErrorMessage,
                }),
            });
        }

        // ── Step 2: Resolve tenant from the validated JWT ─────────────────────
        // The Auth service's claims transformation adds "tenant_id" to the principal.
        // Downstream services read it via TenantMiddleware.TenantIdClaim to scope data.
        var tenantIdClaim = User.FindFirst(TenantMiddleware.TenantIdClaim)?.Value;
        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
        {
            _logger.LogWarning(
                "Upload rejected: caller has no tenant_id claim. " +
                "Token must be exchanged via POST /api/auth/token/exchange first.");

            return Unauthorized(new { error = "Tenant identity could not be resolved from the token." });
        }

        var uploadId = Guid.NewGuid();

        // ── Step 3: Stream row count (proves streaming, not buffering) ─────────
        var dataRowCount = await CountDataRowsAsync(request.File, cancellationToken);

        // ── Step 4: Persist file via storage service ──────────────────────────
        // IFileStorageService is storage-agnostic — local or Azure Blob is resolved
        // from configuration at startup with no change to this controller.
        var storageResult = await _storageService.StoreAsync(
            tenantId, uploadId, request.File, cancellationToken);

        // ── Guard: verify storage returned a usable path before publishing ─────
        // A null or empty RelativePath means the storage layer failed silently.
        // Publishing an event with an empty StoragePath would cause every Worker
        // retry to fail and ultimately dead-letter the message. Reject early here
        // so the caller receives a 500 rather than a silent data-integrity hole.
        if (string.IsNullOrWhiteSpace(storageResult.RelativePath))
        {
            _logger.LogError(
                "Storage service returned a blank RelativePath. " +
                "UploadId={UploadId} TenantId={TenantId} FileName={FileName}",
                uploadId, tenantId, request.File.FileName);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                error = "The file was stored but the storage path could not be determined. " +
                        "The upload cannot proceed.",
            });
        }

        // ── Step 5: Publish integration event to RabbitMQ ────────────────────
        // Non-blocking from the client's perspective: the response is returned
        // immediately after publish. Worker service processes the file asynchronously.
        // MassTransit's exponential back-off retry handles transient broker failures.
        await _eventPublisher.PublishAsync(new FileUploadedEvent
        {
            UploadId = uploadId,
            TenantId = tenantId,
            StoragePath = storageResult.RelativePath,
            FileName = request.File.FileName,
            FileSizeBytes = request.File.Length,
            DataRowCount = dataRowCount,
            SourceSystem = request.SourceSystem,
            UploadedAt = DateTimeOffset.UtcNow,
        }, cancellationToken);

        _logger.LogInformation(
            "Upload complete. UploadId={UploadId} TenantId={TenantId} " +
            "FileName={FileName} FileSizeBytes={FileSizeBytes} " +
            "DataRows={DataRows} StoragePath={StoragePath} SourceSystem={SourceSystem}",
            uploadId, tenantId, request.File.FileName,
            request.File.Length, dataRowCount, storageResult.RelativePath, request.SourceSystem);

        return Ok(new UploadResult(
            UploadId: uploadId,
            TenantId: tenantId,
            FileName: request.File.FileName,
            FileSizeBytes: request.File.Length,
            DataRowCount: dataRowCount,
            SourceSystem: request.SourceSystem,
            StoragePath: storageResult.RelativePath,
            AcceptedAt: DateTimeOffset.UtcNow));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Streams through the uploaded file line-by-line to count data rows,
    /// excluding the header. Uses <see cref="StreamReader"/> to avoid loading
    /// the full file into memory.
    /// </summary>
    /// <param name="file">The uploaded form file.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>Number of data rows (header row excluded). Returns 0 for empty files.</returns>
    private static async Task<int> CountDataRowsAsync(
        IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(
            stream,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);

        // Skip the header row.
        var header = await reader.ReadLineAsync(cancellationToken);
        if (header is null)
            return 0;

        var rowCount = 0;
        while (await reader.ReadLineAsync(cancellationToken) is not null)
            rowCount++;

        return rowCount;
    }
}
