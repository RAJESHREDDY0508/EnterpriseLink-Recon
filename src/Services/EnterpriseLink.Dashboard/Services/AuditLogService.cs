using EnterpriseLink.Dashboard.Dtos;
using EnterpriseLink.Dashboard.Queries;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseLink.Dashboard.Services;

/// <summary>
/// EF Core implementation of <see cref="IAuditLogService"/>.
///
/// <para>
/// Because <c>DashboardTenantContext</c> has <c>HasTenant = false</c>, the
/// <c>AppDbContext</c> AuditLog query filter evaluates to <c>true</c> for all rows
/// (<c>!HasTenant</c>), so no explicit <c>IgnoreQueryFilters()</c> call is required.
/// Cross-tenant visibility is built into the filter design for this context type.
/// </para>
///
/// <para>
/// Time-range queries hit the <c>IX_AuditLogs_OccurredAt</c> index and are efficient
/// even against tables with millions of rows.
/// </para>
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _context;
    private readonly ILogger<AuditLogService> _logger;

    /// <summary>
    /// Initialises the service with its required dependencies.
    /// </summary>
    /// <param name="context">Scoped EF Core context.</param>
    /// <param name="logger">Structured logger.</param>
    public AuditLogService(AppDbContext context, ILogger<AuditLogService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(
        AuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _context.AuditLogs
            .IgnoreQueryFilters()
            .AsNoTracking();

        // ── Optional filters ──────────────────────────────────────────────────

        if (!string.IsNullOrWhiteSpace(query.EntityType))
            q = q.Where(a => a.EntityType == query.EntityType);

        if (query.TenantId.HasValue)
            q = q.Where(a => a.TenantId == query.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action == query.Action);

        if (query.From.HasValue)
            q = q.Where(a => a.OccurredAt >= query.From.Value);

        if (query.To.HasValue)
            q = q.Where(a => a.OccurredAt <= query.To.Value);

        // ── Pagination ────────────────────────────────────────────────────────

        var totalCount = await q.CountAsync(cancellationToken);

        var items = await q
            .OrderByDescending(a => a.OccurredAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        _logger.LogDebug(
            "AuditLog query returned {Count}/{Total} entries. EntityType={EntityType} Action={Action}",
            items.Count, totalCount, query.EntityType, query.Action);

        return new PagedResult<AuditLogDto>(
            items.Select(a => new AuditLogDto(
                a.AuditLogId,
                a.EntityType,
                a.EntityId,
                a.TenantId,
                a.Action,
                a.OldValues,
                a.NewValues,
                a.OccurredAt)).ToList(),
            totalCount,
            query.Page,
            query.PageSize);
    }
}
