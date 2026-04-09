using System.Text.Json;
using EnterpriseLink.Shared.Domain.Entities;
using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Shared.Infrastructure.Persistence;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.Parsing;
using Microsoft.Extensions.Options;

namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// EF Core implementation of <see cref="IInvalidRowPersister"/> that maps rejected
/// <see cref="ParsedRow"/> objects to <see cref="InvalidTransaction"/> entities and
/// commits them in configurable-size batches.
///
/// <para><b>Serialisation</b>
/// <c>ParsedRow.Fields</c> is serialised to JSON for the <c>RawData</c> column so
/// the exact source-CSV bytes are preserved for operator review.
/// <c>ValidationErrors</c> is serialised as a JSON array of formatted strings
/// (format: <c>"[ErrorCode] FieldName: Message"</c>).
/// </para>
///
/// <para><b>Tenant injection</b>
/// <c>TenantId</c> is NOT set manually — <c>AppDbContext.ApplyTenantId</c> injects
/// it automatically from the scoped <c>WorkerTenantContext</c> that was set by
/// <c>FileUploadedEventConsumer</c> before calling this persister.
/// </para>
///
/// <para><b>Batch size</b>
/// Reuses <see cref="BatchInsertOptions.BatchSize"/> to keep the configuration
/// surface small. Schema/duplicate failures are typically a small fraction of total
/// rows so the actual batch count is usually 1.
/// </para>
/// </summary>
public sealed class EfInvalidRowPersister : IInvalidRowPersister
{
    private readonly AppDbContext _context;
    private readonly int _batchSize;
    private readonly ILogger<EfInvalidRowPersister> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false
    };

    /// <summary>Initialises the persister with its required dependencies.</summary>
    /// <param name="context">Scoped EF Core context.</param>
    /// <param name="options">Batch insert options — controls <c>BatchSize</c>.</param>
    /// <param name="logger">Structured logger.</param>
    public EfInvalidRowPersister(
        AppDbContext context,
        IOptions<BatchInsertOptions> options,
        ILogger<EfInvalidRowPersister> logger)
    {
        _context = context;
        _batchSize = options.Value.BatchSize;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> PersistAsync(
        IReadOnlyList<(ParsedRow Row, IReadOnlyList<ValidationError> Errors, string FailureReason)> invalidRows,
        Guid uploadId,
        CancellationToken cancellationToken = default)
    {
        if (invalidRows.Count == 0)
            return 0;

        var totalPersisted = 0;
        var batch = new List<InvalidTransaction>(_batchSize);

        foreach (var (row, errors, failureReason) in invalidRows)
        {
            batch.Add(MapToEntity(row, errors, failureReason, uploadId));

            if (batch.Count >= _batchSize)
            {
                totalPersisted += await CommitBatchAsync(batch, cancellationToken);
                batch.Clear();
            }
        }

        if (batch.Count > 0)
            totalPersisted += await CommitBatchAsync(batch, cancellationToken);

        _logger.LogInformation(
            "Invalid row persistence complete. UploadId={UploadId} TotalPersisted={TotalPersisted}",
            uploadId, totalPersisted);

        return totalPersisted;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<int> CommitBatchAsync(
        List<InvalidTransaction> batch,
        CancellationToken cancellationToken)
    {
        await _context.InvalidTransactions.AddRangeAsync(batch, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
        return batch.Count;
    }

    private static InvalidTransaction MapToEntity(
        ParsedRow row,
        IReadOnlyList<ValidationError> errors,
        string failureReason,
        Guid uploadId)
    {
        var rawData = JsonSerializer.Serialize(row.Fields, JsonOpts);

        var errorStrings = errors.Select(e =>
            $"[{e.Code}] {e.FieldName}: {e.Message}").ToList();

        var validationErrors = JsonSerializer.Serialize(errorStrings, JsonOpts);

        return new InvalidTransaction
        {
            UploadId = uploadId,
            RowNumber = row.RowNumber,
            RawData = rawData,
            ValidationErrors = validationErrors,
            FailureReason = failureReason,
        };
    }
}
