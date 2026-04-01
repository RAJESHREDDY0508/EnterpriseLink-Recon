using Microsoft.Extensions.Configuration;

namespace EnterpriseLink.Auth.Services;

/// <summary>
/// Configuration-backed implementation of <see cref="ITenantMappingService"/>.
///
/// <para>
/// Reads the Entra-to-internal tenant mapping from the <c>TenantMappings</c> section
/// of <c>appsettings.json</c>. Each key is an Entra ID <c>tid</c> GUID string; each
/// value is the corresponding EnterpriseLink internal TenantId GUID.
/// </para>
///
/// <para><b>Example configuration</b></para>
/// <code>
/// "TenantMappings": {
///   "11111111-0000-0000-0000-000000000001": "aaaaaaaa-0000-0000-0000-000000000001",
///   "22222222-0000-0000-0000-000000000002": "bbbbbbbb-0000-0000-0000-000000000002"
/// }
/// </code>
///
/// <para><b>Operational notes</b></para>
/// <list type="bullet">
///   <item><description>
///     The mapping is loaded once at startup and cached in a
///     <see cref="Dictionary{TKey, TValue}"/> for O(1) lookups.
///     Restart the service to pick up configuration changes.
///   </description></item>
///   <item><description>
///     Key comparison is case-insensitive to avoid typo-related mismatches.
///   </description></item>
///   <item><description>
///     For dynamic tenant onboarding without restarts, replace this implementation
///     with a database-backed service that queries the <c>Tenants</c> table by
///     an <c>EntraDirectoryId</c> column.
///   </description></item>
/// </list>
///
/// <para><b>Registration</b></para>
/// <code>
/// builder.Services.AddSingleton&lt;ITenantMappingService, ConfigurationTenantMappingService&gt;();
/// </code>
/// Registered as Singleton because the dictionary is immutable after construction.
/// </summary>
public sealed class ConfigurationTenantMappingService : ITenantMappingService
{
    private const string SectionName = "TenantMappings";

    private readonly IReadOnlyDictionary<string, Guid> _mappings;
    private readonly ILogger<ConfigurationTenantMappingService> _logger;

    /// <summary>
    /// Initialises the service and loads the tenant map from configuration.
    /// </summary>
    /// <param name="configuration">Application configuration (injected by DI).</param>
    /// <param name="logger">Structured logger for diagnostic output.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when a value in the <c>TenantMappings</c> section is not a valid GUID.
    /// This is a startup-fatal error — bad configuration must be detected immediately.
    /// </exception>
    public ConfigurationTenantMappingService(
        IConfiguration configuration,
        ILogger<ConfigurationTenantMappingService> logger)
    {
        _logger = logger;

        var raw = configuration
            .GetSection(SectionName)
            .Get<Dictionary<string, string>>();

        if (raw is null || raw.Count == 0)
        {
            _logger.LogWarning(
                "No tenant mappings found in configuration section '{Section}'. " +
                "All token exchange requests will be rejected.",
                SectionName);

            _mappings = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var mappings = new Dictionary<string, Guid>(
            capacity: raw.Count,
            comparer: StringComparer.OrdinalIgnoreCase);

        foreach (var (entraTid, internalId) in raw)
        {
            if (!Guid.TryParse(internalId, out var internalGuid))
            {
                throw new ArgumentException(
                    $"Invalid GUID value '{internalId}' for Entra tenant '{entraTid}' " +
                    $"in configuration section '{SectionName}'.");
            }

            mappings[entraTid] = internalGuid;
        }

        _mappings = mappings;
        _logger.LogInformation(
            "Tenant mapping loaded: {Count} Entra tenant(s) registered.", _mappings.Count);
    }

    /// <inheritdoc />
    public Guid? MapEntraTenant(string entraTenantId)
    {
        if (_mappings.TryGetValue(entraTenantId, out var internalId))
        {
            _logger.LogDebug(
                "Entra tenant {EntraTenantId} mapped to internal TenantId {InternalTenantId}",
                entraTenantId, internalId);
            return internalId;
        }

        _logger.LogWarning(
            "Entra tenant {EntraTenantId} has no registered mapping. Token exchange denied.",
            entraTenantId);
        return null;
    }
}
