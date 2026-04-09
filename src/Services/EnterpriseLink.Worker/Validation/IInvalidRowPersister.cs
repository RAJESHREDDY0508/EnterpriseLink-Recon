using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Parsing;

namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// Persists rows that failed validation to the <c>InvalidTransactions</c> table so
/// that operators can review, correct, and re-submit them.
///
/// <para><b>Acceptance criterion: Invalid records stored separately</b>
/// Rejected rows are never written to the main <c>Transactions</c> table. Instead,
/// each row is stored in <c>InvalidTransactions</c> with its raw field data, all
/// validation error messages, and the pipeline stage that first rejected it.
/// </para>
///
/// <para><b>Batching</b>
/// The default implementation (<c>EfInvalidRowPersister</c>) collects rejected rows
/// into configurable-size batches and calls <c>SaveChangesAsync</c> once per batch
/// — the same pattern as <c>TransactionBatchInserter</c> — so that a file with
/// many invalid rows does not issue one DB round-trip per row.
/// </para>
/// </summary>
public interface IInvalidRowPersister
{
    /// <summary>
    /// Persists all <paramref name="invalidRows"/> to the <c>InvalidTransactions</c> table.
    /// </summary>
    /// <param name="invalidRows">
    /// The sequence of rejected rows produced by <c>IValidationPipeline.ClassifyAsync</c>.
    /// Each element is a tuple of the original <see cref="ParsedRow"/>, the list of
    /// <see cref="ValidationError"/> instances, and the pipeline stage name
    /// (<c>"Schema"</c>, <c>"BusinessRule"</c>, or <c>"Duplicate"</c>).
    /// </param>
    /// <param name="uploadId">
    /// The <c>FileUploadedEvent.UploadId</c> that produced these rows. Written to
    /// every <c>InvalidTransaction</c> record so errors can be grouped by upload.
    /// </param>
    /// <param name="cancellationToken">Propagated to all async DB operations.</param>
    /// <returns>The total number of invalid rows persisted.</returns>
    Task<int> PersistAsync(
        IReadOnlyList<(ParsedRow Row, IReadOnlyList<ValidationError> Errors, string FailureReason)> invalidRows,
        Guid uploadId,
        CancellationToken cancellationToken = default);
}
