namespace EnterpriseLink.Worker.Validation;

/// <summary>
/// Marker interface for validators that enforce structural schema rules (e.g.
/// required fields, type parseability). Registered separately from
/// <see cref="IBusinessRuleValidator"/> so that <see cref="ValidationPipeline"/>
/// can execute schema checks before business-rule checks without hard-coding the
/// validator type names.
///
/// <para>
/// Implementations must also implement <see cref="IRowValidator"/> — this interface
/// adds no members; it exists solely as a DI registration tag.
/// </para>
/// </summary>
public interface ISchemaValidator : IRowValidator { }
