using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Worker.Idempotency;

/// <summary>
/// EF Core implementation of <see cref="IUploadIdempotencyGuard"/> backed by the
/// <c>ProcessedUploads</c> table in SQL Server.
///
/// <para><b>Claim mechanism</b></para>
/// <see cref="TryBeginAsync"/> first checks whether a row exists for the
/// <c>UploadId</c>. If not, it inserts a new <c>Processing</c> row. If a concurrent
/// worker races to insert the same row, the database PK constraint fires and
/// <see cref="DbUpdateException"/> is caught — the loser returns <c>false</c>.
///
/// <para><b>IgnoreQueryFilters usage</b></para>
/// All queries in this class use <c>IgnoreQueryFilters()</c> to bypass the
/// <c>IsDeleted</c> soft-delete filter. This ensures that even a soft-deleted
/// <c>ProcessedUpload</c> record (e.g., from a manual cleanup) is detected and
/// prevents re-processing.
///
/// <para><b>ChangeTracker management</b>
/// After each <c>SaveChangesAsync</c> call the change tracker is cleared so that
/// tracked entities from the idempotency guard do not interfere with the subsequent
/// batch insert performed by <see cref="Batch.TransactionBatchInserter"/> (both share
/// the same scoped <c>AppDbContext</c> instance).
/// </para>
/// </summary>
public sealed class EfUploadIdempotencyGuard : IUploadIdempotencyGuard
{
    private readonly AppDbContext _context;
    private readonly ILogger<EfUploadIdempotencyGuard> _logger;

    /// <summary>Initialises the guard with its required dependencies.</summary>
    /// <param name="context">Scoped EF Core context shared with the batch inserter.</param>
    /// <param name="logger">Structured logger.</param>
    public EfUploadIdempotencyGuard(
        AppDbContext context,
        ILogger<EfUploadIdempotencyGuard> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> TryBeginAsync(
        Guid uploadId,
        Guid tenantId,
        string sourceSystem,
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters: bypass IsDeleted so soft-deleted records are detected.
        var existing = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .Where(p => p.UploadId == uploadId)
            .Select(p => new { p.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            if (existing.Status == UploadProcessingStatus.Completed)
            {
                _logger.LogWarning(
                    "Upload already completed — skipping duplicate message. " +
                    "UploadId={UploadId} TenantId={TenantId}",
                    uploadId, tenantId);
                return false;
            }

            // Processing or Failed: allow retry. Load the full record and reset to
            // Processing so the retry is treated as a fresh attempt.
            var stale = await _context.ProcessedUploads
                .IgnoreQueryFilters()
                .FirstAsync(p => p.UploadId == uploadId, cancellationToken);

            var previousStatus = stale.Status;
            stale.Status = UploadProcessingStatus.Processing;
            stale.RowsInserted = 0;

            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            _logger.LogInformation(
                "Retrying upload previously in status {PreviousStatus}. " +
                "UploadId={UploadId} TenantId={TenantId}",
                previousStatus, uploadId, tenantId);

            return true;
        }

        // First time seeing this upload — attempt to claim it.
        _context.ProcessedUploads.Add(new ProcessedUpload
        {
            UploadId = uploadId,
            TenantId = tenantId,
            Status = UploadProcessingStatus.Processing,
            SourceSystem = sourceSystem,
            RowsInserted = 0,
        });

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            _context.ChangeTracker.Clear();

            _logger.LogInformation(
                "Upload claimed for processing. UploadId={UploadId} TenantId={TenantId}",
                uploadId, tenantId);

            return true;
        }
        catch (DbUpdateException)
        {
            // Another worker instance inserted a row between our SELECT and INSERT.
            // The PK constraint fired — the other worker wins; we skip.
            _context.ChangeTracker.Clear();

            _logger.LogWarning(
                "Concurrent claim detected — another worker already claimed this upload. " +
                "UploadId={UploadId} TenantId={TenantId}",
                uploadId, tenantId);

            return false;
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        Guid uploadId,
        int rowsInserted,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.UploadId == uploadId, cancellationToken);

        if (record is null)
        {
            _logger.LogError(
                "Cannot mark upload as complete — ProcessedUpload record not found. " +
                "UploadId={UploadId}",
                uploadId);
            throw new InvalidOperationException(
                $"ProcessedUpload {uploadId} not found — cannot mark as Completed. " +
                "The record may have been deleted concurrently.");
        }

        record.Status = UploadProcessingStatus.Completed;
        record.RowsInserted = rowsInserted;

        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();

        _logger.LogInformation(
            "Upload marked complete. UploadId={UploadId} RowsInserted={RowsInserted}",
            uploadId, rowsInserted);
    }

    /// <inheritdoc />
    public async Task FailAsync(
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        var record = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.UploadId == uploadId, cancellationToken);

        if (record is null)
        {
            _logger.LogError(
                "Cannot mark upload as failed — ProcessedUpload record not found. " +
                "UploadId={UploadId}",
                uploadId);
            return;
        }

        record.Status = UploadProcessingStatus.Failed;

        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();

        _logger.LogWarning(
            "Upload marked as failed. UploadId={UploadId}",
            uploadId);
    }
}
