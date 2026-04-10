using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Shared.Domain.Enums;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Dashboard.Services;

/// <summary>
/// EF Core implementation of <see cref="IBatchMonitorService"/>.
///
/// <para>
/// Uses <c>IgnoreQueryFilters()</c> on <c>ProcessedUploads</c> to include soft-deleted
/// records and bypass the EF Core soft-delete filter, giving operators full visibility
/// of all upload lifecycle states including failed-and-deleted records.
/// </para>
///
/// <para>
/// The service is registered as <b>scoped</b> — one instance per HTTP request or
/// Blazor Server circuit action. This matches the scope of <c>AppDbContext</c>.
/// </para>
/// </summary>
public sealed class BatchMonitorService : IBatchMonitorService
{
    private readonly AppDbContext _context;
    private readonly ILogger<BatchMonitorService> _logger;

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// </summary>
    /// <param name="context">Scoped EF Core context. All queries are read-only (<c>AsNoTracking</c>).</param>
    /// <param name="logger">Structured logger.</param>
    public BatchMonitorService(AppDbContext context, ILogger<BatchMonitorService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<ProcessedUploadSummaryDto>> GetUploadsAsync(
        BatchMonitorQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.ProcessedUploads
            .IgnoreQueryFilters()
            .AsNoTracking();

        // ── Optional filters ──────────────────────────────────────────────────

        if (query.Status is not null &&
            Enum.TryParse<UploadProcessingStatus>(query.Status, ignoreCase: true, out var statusEnum))
        {
            q = q.Where(u => u.Status == statusEnum);
        }

        if (query.TenantId.HasValue)
        {
            q = q.Where(u => u.TenantId == query.TenantId.Value);
        }

        // ── Pagination ────────────────────────────────────────────────────────

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(u => u.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "BatchMonitor query returned {Count}/{Total} uploads. Page={Page} Status={Status} TenantId={TenantId}",
            items.Count, totalCount, query.Page, query.Status, query.TenantId);

        return new PagedResult<ProcessedUploadSummaryDto>(
            items.Select(u => new ProcessedUploadSummaryDto(
                u.UploadId,
                u.TenantId,
                u.Status.ToString(),
                u.RowsInserted,
                u.SourceSystem,
                u.CreatedAt,
                u.UpdatedAt)).ToList(),
            totalCount,
            query.Page,
            query.PageSize);
    }

    /// <inheritdoc />
    public async Task<ProcessedUploadSummaryDto?> GetUploadByIdAsync(
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        var upload = await _context.ProcessedUploads
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.UploadId == uploadId, cancellationToken);

        if (upload is null)
        {
            _logger.LogDebug("BatchMonitor: upload {UploadId} not found.", uploadId);
            return null;
        }

        return new ProcessedUploadSummaryDto(
            upload.UploadId,
            upload.TenantId,
            upload.Status.ToString(),
            upload.RowsInserted,
            upload.SourceSystem,
            upload.CreatedAt,
            upload.UpdatedAt);
    }
}
