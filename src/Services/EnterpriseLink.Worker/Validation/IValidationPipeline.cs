using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Parsing;

namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// Orchestrates the full validation pipeline for a stream of <see cref="ParsedRow"/>
/// objects, separating them into valid rows (ready for batch insert) and invalid
/// rows (ready for error persistence).
///
/// <para><b>Pipeline stages — in order</b>
/// <list type="number">
///   <item>
///     <description>
///       <b>Schema validation</b> — all <see cref="IRowValidator"/> implementations
///       registered with <c>ValidationStage.Schema</c> must pass before the row
///       advances. Required-field presence and type-parseability are enforced here.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Business rules</b> — all <see cref="IRowValidator"/> implementations
///       registered with <c>ValidationStage.BusinessRule</c> run against rows that
///       passed schema. Rules are extensible: register a new implementation and it
///       is picked up automatically.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Duplicate detection</b> — rows that passed both prior gates are checked
///       for intra-upload duplicates by <c>IDuplicateDetector</c>. Duplicates
///       are diverted to the error stream with <c>FailureReason = Duplicate</c>.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para><b>Output</b>
/// <see cref="ClassifyAsync"/> returns two lazy async sequences — callers consume
/// them separately. The valid sequence feeds <c>IBatchRowInserter</c>; the invalid
/// sequence feeds <c>IInvalidRowPersister</c>.
/// </para>
/// </summary>
public interface IValidationPipeline
{
    /// <summary>
    /// Runs all validation stages over <paramref name="rows"/> and partitions them
    /// into valid and invalid streams.
    /// </summary>
    /// <param name="rows">Lazy async row stream from the CSV parser.</param>
    /// <param name="cancellationToken">Propagated to all async operations.</param>
    /// <returns>
    /// A tuple of two read-only lists produced after fully consuming
    /// <paramref name="rows"/>. Both lists are non-null; either may be empty.
    /// <list type="bullet">
    ///   <item><description><c>Valid</c> — rows that passed all three stages.</description></item>
    ///   <item><description><c>Invalid</c> — rows that failed at least one stage, with errors attached.</description></item>
    /// </list>
    /// </returns>
    Task<(IReadOnlyList<ParsedRow> Valid, IReadOnlyList<(ParsedRow Row, IReadOnlyList<ValidationError> Errors, string FailureReason)> Invalid)>
        ClassifyAsync(
            IAsyncEnumerable<ParsedRow> rows,
            CancellationToken cancellationToken = default);
}
