namespace EnterpriseLink.Shared.Contracts.Schemas;

/// <summary>
/// Immutable result returned by <see cref="IEventSchemaValidator"/> after validating
/// a serialised event payload against its JSON Schema contract.
/// </summary>
/// <param name="IsValid">
/// <see langword="true"/> when the payload satisfies all JSON Schema constraints.
/// </param>
/// <param name="Errors">
/// Ordered list of human-readable validation error messages.
/// Empty when <see cref="IsValid"/> is <see langword="true"/>.
/// Each entry identifies the JSON pointer path and the violated constraint.
/// </param>
public sealed record SchemaValidationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    /// <summary>A pre-built result representing a successful validation with no errors.</summary>
    public static readonly SchemaValidationResult Valid =
        new(IsValid: true, Errors: Array.Empty<string>());

    /// <summary>
    /// Creates a failed validation result from a collection of error messages.
    /// </summary>
    /// <param name="errors">One or more validation error descriptions.</param>
    /// <returns>A <see cref="SchemaValidationResult"/> with <see cref="IsValid"/> = <see langword="false"/>.</returns>
    public static SchemaValidationResult Fail(IEnumerable<string> errors)
        => new(IsValid: false, Errors: errors.ToArray());
}
