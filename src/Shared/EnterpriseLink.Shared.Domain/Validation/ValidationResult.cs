namespace EnterpriseLink.Shared.Domain.Validation;

/// <summary>
/// The outcome of a single-validator pass over a <c>ParsedRow</c>.
///
/// <para><b>Factory methods</b>
/// Always use <see cref="Success"/> or <see cref="Failure"/> — the constructor is
/// private to enforce invariants (a successful result always has zero errors; a
/// failed result always has at least one).
/// </para>
///
/// <para><b>Allocation</b>
/// <see cref="Success"/> returns a shared static instance to avoid per-row heap
/// allocation on the hot path — the vast majority of rows in a clean file are valid.
/// <see cref="Failure"/> always allocates because the error list differs per row.
/// </para>
/// </summary>
public sealed class ValidationResult
{
    // Shared success singleton — avoids allocation on the hot (valid) path.
    private static readonly ValidationResult _success = new(true, []);

    /// <summary>Returns the shared success result (no errors).</summary>
    public static ValidationResult Success() => _success;

    /// <summary>
    /// Creates a failure result containing the supplied errors.
    /// </summary>
    /// <param name="errors">
    /// At least one <see cref="ValidationError"/> describing the failure.
    /// If the collection is empty the result is still marked as a failure
    /// (caller must supply a meaningful error).
    /// </param>
    public static ValidationResult Failure(IEnumerable<ValidationError> errors) =>
        new(false, errors.ToList().AsReadOnly());

    private ValidationResult(bool isValid, IReadOnlyList<ValidationError> errors)
    {
        IsValid = isValid;
        Errors = errors;
    }

    /// <summary><c>true</c> when the row passed this validator; <c>false</c> otherwise.</summary>
    public bool IsValid { get; }

    /// <summary>
    /// The validation errors produced by this validator.
    /// Empty when <see cref="IsValid"/> is <c>true</c>.
    /// </summary>
    public IReadOnlyList<ValidationError> Errors { get; }
}
