using System.Security.Cryptography;
using System.Text;
using EnterpriseLink.Worker.Parsing;

namespace EnterpriseLink.Worker.Validation.Duplicate;

/// <summary>
/// Hash-based duplicate detector that computes a SHA-256 fingerprint over a
/// row's key fields and tracks seen fingerprints in a <c>HashSet&lt;string&gt;</c>.
///
/// <para><b>Fingerprint strategy</b>
/// The fingerprint is computed from the following candidate key fields in priority
/// order, all case-insensitive:
/// <list type="number">
///   <item>
///     <description>
///       <b>Composite key</b> — <c>ExternalReferenceId</c> (or aliases
///       <c>Id</c>, <c>ReferenceId</c>, <c>TransactionId</c>) combined with
///       <c>Amount</c> (or aliases <c>Value</c>, <c>TotalAmount</c>). Used when
///       at least one key field is present and non-empty. This mirrors the field
///       mapping used by <c>TransactionBatchInserter</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Full-row fallback</b> — when no key field is found, all field values
///       sorted by header name are concatenated. This handles source files that use
///       non-standard column names.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para><b>Complexity</b>
/// SHA-256 is O(n) in the concatenated string length. HashSet lookup is O(1)
/// average. Total per-row cost is proportional to the number of characters in the
/// key fields — typically well under 1 µs.
/// </para>
///
/// <para><b>Memory</b>
/// The <c>HashSet</c> holds one 64-character hex string per unique row. For 1 million
/// rows this is ≈ 64 MB — acceptable for a single-file session. The instance is
/// discarded at the end of each upload.
/// </para>
/// </summary>
public sealed class FingerprintDuplicateDetector : IDuplicateDetector
{
    private static readonly string[] KeyCandidates =
    [
        "ExternalReferenceId", "Id", "ReferenceId", "TransactionId"
    ];

    private static readonly string[] AmountCandidates =
    [
        "Amount", "Value", "TotalAmount"
    ];

    private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool IsDuplicate(ParsedRow row)
    {
        var fingerprint = ComputeFingerprint(row);
        return !_seen.Add(fingerprint);   // Add returns false when already present
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string ComputeFingerprint(ParsedRow row)
    {
        var key = BuildKey(row.Fields);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes);   // 64-char uppercase hex, no alloc in .NET 8
    }

    private static string BuildKey(IReadOnlyDictionary<string, string> fields)
    {
        // Try composite key: reference-id + amount
        var refId = GetFirst(fields, KeyCandidates);
        var amount = GetFirst(fields, AmountCandidates);

        if (!string.IsNullOrEmpty(refId) || !string.IsNullOrEmpty(amount))
            return $"{refId}|{amount}";

        // Full-row fallback: sort by header for determinism
        var sb = new StringBuilder();
        foreach (var kv in fields.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        }

        return sb.ToString();
    }

    private static string GetFirst(IReadOnlyDictionary<string, string> fields, string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (fields.TryGetValue(candidate, out var value) &&
                !string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }
}
