namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// Marker interface for validators that enforce domain business rules (e.g.
/// non-negative amounts, reference-ID format). Registered separately from
/// <see cref="ISchemaValidator"/> so that <see cref="ValidationPipeline"/>
/// runs schema checks before business rules — avoiding expensive rule evaluation
/// on rows that already fail structural requirements.
///
/// <para>
/// Implementations must also implement <see cref="IRowValidator"/> — this interface
/// adds no members; it exists solely as a DI registration tag.
/// </para>
///
/// <para><b>Extensibility</b>
/// To add a new business rule: implement this interface (and <see cref="IRowValidator"/>),
/// register it with the DI container in <c>WorkerValidationExtensions</c>. No existing
/// code changes are required.
/// </para>
/// </summary>
public interface IBusinessRuleValidator : IRowValidator { }
