using EnterpriseLink.Worker.Parsing;

namespace EnterpriseLink.Worker.Batch;

/// <summary>
/// Persists <see cref="ParsedRow"/> objects to the database in configurable-size batches.
///
/// <para><b>Acceptance criteria</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <b>Commit every N records</b> — rows are accumulated in a buffer of size
///       <c>BatchSize</c> (see <see cref="Configuration.BatchInsertOptions"/>) and
///       committed via a single <c>SaveChangesAsync</c> per batch. One SQL round-trip
///       per batch regardless of N.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>O(1) memory</b> — the batch buffer is reused (cleared after each commit);
///       the change tracker is cleared between batches so EF Core does not accumulate
///       tracked entities across the full file lifetime.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Caller responsibility</b></para>
/// The caller is responsible for the idempotency check
/// (<see cref="Idempotency.IUploadIdempotencyGuard.TryBeginAsync"/>) before invoking
/// this method. <see cref="InsertAsync"/> does not deduplicate rows.
///
/// <para><b>Tenant isolation</b>
/// The current tenant is taken from the scoped <c>WorkerTenantContext</c> via
/// <c>AppDbContext.ApplyTenantId()</c>. The caller must set the tenant context
/// before invoking <see cref="InsertAsync"/>.
/// </para>
/// </summary>
public interface IBatchRowInserter
{
    /// <summary>
    /// Streams <paramref name="rows"/>, maps each to a <c>Transaction</c> entity,
    /// and commits them to the database in batches of <c>BatchSize</c>.
    /// </summary>
    /// <param name="rows">
    /// Lazy async sequence of parsed CSV rows — never materialised in full.
    /// </param>
    /// <param name="tenantId">
    /// Tenant that owns these transactions; used for structured logging only
    /// (the tenant is injected automatically by <c>AppDbContext.ApplyTenantId</c>).
    /// </param>
    /// <param name="uploadId">Upload correlation ID for structured logging.</param>
    /// <param name="sourceSystem">Source system name for structured logging.</param>
    /// <param name="cancellationToken">
    /// Propagated from the MassTransit consume context; cancellation is honoured
    /// between batches (a partial batch may be committed before the token is observed).
    /// </param>
    /// <returns>Total number of rows inserted across all batches.</returns>
    Task<int> InsertAsync(
        IAsyncEnumerable<ParsedRow> rows,
        Guid tenantId,
        Guid uploadId,
        string sourceSystem,
        CancellationToken cancellationToken = default);
}
