using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Dashboard.Services;

/// <summary>
/// EF Core implementation of <see cref="IErrorViewerService"/>.
///
/// <para>
/// Uses <c>IgnoreQueryFilters()</c> to bypass both the soft-delete and tenant-isolation
/// query filters on <c>InvalidTransactions</c>, giving the Dashboard cross-tenant
/// visibility into all validation failures. Optional per-request filters
/// (<c>UploadId</c>, <c>TenantId</c>, <c>FailureReason</c>) are applied in LINQ
/// after the global filters are suppressed.
/// </para>
/// </summary>
public sealed class ErrorViewerService : IErrorViewerService
{
    private readonly AppDbContext _context;
    private readonly ILogger<ErrorViewerService> _logger;

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// </summary>
    /// <param name="context">Scoped EF Core context.</param>
    /// <param name="logger">Structured logger.</param>
    public ErrorViewerService(AppDbContext context, ILogger<ErrorViewerService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<InvalidTransactionDto>> GetErrorsAsync(
        ErrorViewerQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.InvalidTransactions
            .IgnoreQueryFilters()
            .AsNoTracking();

        // ── Optional filters ──────────────────────────────────────────────────

        if (query.UploadId.HasValue)
            q = q.Where(e => e.UploadId == query.UploadId.Value);

        if (query.TenantId.HasValue)
            q = q.Where(e => e.TenantId == query.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(query.FailureReason))
            q = q.Where(e => e.FailureReason == query.FailureReason);

        // ── Pagination ────────────────────────────────────────────────────────

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderBy(e => e.UploadId)
            .ThenBy(e => e.RowNumber)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "ErrorViewer query returned {Count}/{Total} errors. " +
            "UploadId={UploadId} TenantId={TenantId} FailureReason={FailureReason}",
            items.Count, totalCount, query.UploadId, query.TenantId, query.FailureReason);

        return new PagedResult<InvalidTransactionDto>(
            items.Select(e => new InvalidTransactionDto(
                e.InvalidTransactionId,
                e.UploadId,
                e.TenantId,
                e.RowNumber,
                e.RawData,
                e.ValidationErrors,
                e.FailureReason,
                e.CreatedAt)).ToList(),
            totalCount,
            query.Page,
            query.PageSize);
    }
}
