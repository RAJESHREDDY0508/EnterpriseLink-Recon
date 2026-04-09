using EnterpriseLink.Worker.Parsing;

namespace EnterpriseLink.Worker.Validation.Duplicate;

/// <summary>
/// Detects duplicate rows within a single upload session using either a hash-based
/// or key-based strategy.
///
/// <para><b>Scope</b>
/// Detection is intra-upload only — rows are compared against other rows in the
/// same CSV file, not against previously imported transactions in the database.
/// Cross-upload deduplication is the responsibility of the idempotency guard and
/// database unique constraints.
/// </para>
///
/// <para><b>Strategy</b>
/// The default implementation (<see cref="FingerprintDuplicateDetector"/>) computes
/// a SHA-256 fingerprint of a row's key fields (Amount + ExternalReferenceId or, if
/// absent, all field values). A <c>HashSet&lt;string&gt;</c> tracks seen fingerprints
/// for O(1) average-case lookup. Because the detector is scoped per upload call the
/// set is bounded by the number of distinct rows in a single file.
/// </para>
///
/// <para><b>Thread safety</b>
/// The default implementation is NOT thread-safe. Each upload gets a fresh instance
/// via scoped DI so concurrent uploads never share state.
/// </para>
/// </summary>
public interface IDuplicateDetector
{
    /// <summary>
    /// Returns <c>true</c> if this row has already been seen in the current upload
    /// session; <c>false</c> if it is unique (and records it as seen).
    /// </summary>
    /// <param name="row">The row to check. Must not be null.</param>
    /// <returns>
    /// <c>true</c> when a row with the same fingerprint was previously passed to
    /// this method in the current instance's lifetime; <c>false</c> otherwise.
    /// </returns>
    bool IsDuplicate(ParsedRow row);
}
