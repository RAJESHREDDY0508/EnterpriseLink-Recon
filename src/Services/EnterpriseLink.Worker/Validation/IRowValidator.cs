using EnterpriseLink.Shared.Domain.Validation;
using EnterpriseLink.Worker.Parsing;

namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// A single validation rule that inspects a <see cref="ParsedRow"/> and returns
/// a <see cref="ValidationResult"/>.
///
/// <para><b>Extensibility</b>
/// The rule framework is open for extension: add a new rule by implementing this
/// interface and registering it with the DI container. The
/// <see cref="IValidationPipeline"/> collects all registered
/// <see cref="IRowValidator"/> implementations from the container and runs them
/// in sequence — no existing code needs to change.
/// </para>
///
/// <para><b>Order</b>
/// Validators are executed in their DI registration order. Schema validators must
/// be registered before business-rule validators so that a row with a missing
/// required field is not also evaluated by expensive business rules.
/// </para>
///
/// <para><b>Thread safety</b>
/// Implementations must be stateless (or internally thread-safe). The pipeline
/// may execute the same validator instance concurrently across multiple uploads
/// because validators are registered as singletons.
/// </para>
/// </summary>
public interface IRowValidator
{
    /// <summary>
    /// Evaluates the row and returns a <see cref="ValidationResult"/> indicating
    /// whether the row passes or fails this rule.
    /// </summary>
    /// <param name="row">The parsed CSV row to inspect.</param>
    /// <returns>
    /// <see cref="ValidationResult.Success"/> when the row satisfies this rule;
    /// <see cref="ValidationResult.Failure"/> with one or more
    /// <see cref="ValidationError"/> instances when it does not.
    /// </returns>
    ValidationResult Validate(ParsedRow row);
}
