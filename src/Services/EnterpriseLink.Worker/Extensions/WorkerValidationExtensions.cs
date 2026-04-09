using EnterpriseLink.Worker.Validation;
using EnterpriseLink.Worker.Validation.Duplicate;
using EnterpriseLink.Worker.Validation.Rules;

namespace EnterpriseLink.Worker.Extensions;

/// <summary>
/// Extension methods that register the Sprint 9 validation engine services.
///
/// <para><b>Registration order matters</b>
/// Schema validators are registered before business-rule validators so the DI
/// container resolves them in the correct pipeline order. The
/// <see cref="ValidationPipeline"/> depends on two separate
/// <c>IEnumerable&lt;ISchemaValidator&gt;</c> and
/// <c>IEnumerable&lt;IBusinessRuleValidator&gt;</c> sequences rather than a single
/// <c>IEnumerable&lt;IRowValidator&gt;</c>, so ordering within each group is
/// preserved by DI resolution order.
/// </para>
/// </summary>
public static class WorkerValidationExtensions
{
    /// <summary>
    /// Registers all validation-engine services:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       Schema validators (<see cref="RequiredFieldsValidator"/>) as singletons —
    ///       stateless; one instance serves all concurrent uploads.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Business-rule validators (<see cref="NonNegativeAmountRule"/>) as singletons.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IDuplicateDetector"/> → <see cref="FingerprintDuplicateDetector"/>
    ///       as <b>scoped</b> — each upload message scope gets a fresh instance with an
    ///       empty seen-fingerprint set.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IValidationPipeline"/> → <see cref="ValidationPipeline"/> as scoped.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IInvalidRowPersister"/> → <see cref="EfInvalidRowPersister"/> as scoped.
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="services">The application's service collection.</param>
    /// <returns>The same <paramref name="services"/> instance for fluent chaining.</returns>
    public static IServiceCollection AddWorkerValidation(this IServiceCollection services)
    {
        // ── Schema validators (singleton — stateless) ──────────────────────────
        services.AddSingleton<ISchemaValidator, RequiredFieldsValidator>();

        // ── Business-rule validators (singleton — stateless) ──────────────────
        // New rules: add a line here; no other file changes required.
        services.AddSingleton<IBusinessRuleValidator, NonNegativeAmountRule>();

        // ── Duplicate detector (scoped — stateful per upload) ──────────────────
        services.AddScoped<IDuplicateDetector, FingerprintDuplicateDetector>();

        // ── Pipeline + error persister (scoped) ───────────────────────────────
        services.AddScoped<IValidationPipeline, ValidationPipeline>();
        services.AddScoped<IInvalidRowPersister, EfInvalidRowPersister>();

        return services;
    }
}
