namespace EnterpriseLink.Auth.Services;

/// <summary>
/// Maps an Entra ID organisation identity to an EnterpriseLink internal <see cref="Guid"/> TenantId.
///
/// <para><b>Why this mapping is necessary</b></para>
/// <para>
/// Entra ID tokens carry a <c>tid</c> (tenant ID) claim that identifies the customer's
/// Entra ID directory — not our internal tenant record. This service bridges the two
/// identity systems so that downstream services work exclusively with our internal
/// <see cref="Guid"/>-based TenantId rather than Entra directory GUIDs.
/// </para>
///
/// <para><b>Implementations</b></para>
/// <list type="bullet">
///   <item>
///     <description>
///       <see cref="ConfigurationTenantMappingService"/> — reads the mapping from
///       <c>appsettings.json / TenantMappings</c>. Suitable for development and
///       environments with a small, stable set of tenants.
///     </description>
///   </item>
///   <item>
///     <description>
///       Future: <c>DatabaseTenantMappingService</c> — queries <c>Tenants</c> table
///       by <c>EntraDirectoryId</c> column. Required for dynamic tenant onboarding.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Security contract</b></para>
/// A <see langword="null"/> return value means the Entra tenant is <em>not registered</em>
/// in EnterpriseLink. Callers MUST treat this as an unauthorized request and return
/// <c>401 Unauthorized</c> — never <c>403 Forbidden</c> (to avoid leaking registration state).
/// </summary>
public interface ITenantMappingService
{
    /// <summary>
    /// Resolves the EnterpriseLink internal TenantId for a given Entra ID tenant.
    /// </summary>
    /// <param name="entraTenantId">
    /// The <c>tid</c> claim value from the validated Entra ID JWT.
    /// This is the GUID string of the customer's Entra ID directory.
    /// </param>
    /// <returns>
    /// The internal <see cref="Guid"/> TenantId if the Entra tenant is registered;
    /// <see langword="null"/> if it is unknown or not yet onboarded.
    /// </returns>
    Guid? MapEntraTenant(string entraTenantId);
}
