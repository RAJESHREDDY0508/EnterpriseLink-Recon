namespace EnterpriseLink.Auth.Configuration;

/// <summary>
/// Strongly-typed configuration for Microsoft Entra ID (formerly Azure Active Directory).
///
/// <para>
/// Populated from the <c>AzureAd</c> section of <c>appsettings.json</c>.
/// Microsoft.Identity.Web reads this section automatically when configured
/// via <c>AddMicrosoftIdentityWebApi</c> from the Microsoft.Identity.Web library.
/// </para>
///
/// <para><b>Configuration Keys</b></para>
/// <list type="table">
///   <listheader>
///     <term>Key</term>
///     <description>Purpose</description>
///   </listheader>
///   <item>
///     <term>Instance</term>
///     <description>Entra ID authority root. Always <c>https://login.microsoftonline.com/</c> for commercial clouds.</description>
///   </item>
///   <item>
///     <term>TenantId</term>
///     <description>
///       The Entra ID directory (tenant) GUID of the organisation that owns the registered application.
///       Use <c>common</c> to accept tokens from any Entra ID tenant (multi-tenant app).
///     </description>
///   </item>
///   <item>
///     <term>ClientId</term>
///     <description>Application (client) ID as registered in the Entra ID App Registration portal.</description>
///   </item>
///   <item>
///     <term>Audience</term>
///     <description>
///       Expected <c>aud</c> claim in incoming tokens. Typically <c>api://&lt;ClientId&gt;</c>.
///       Must match the scope exposed by the App Registration.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Developer Setup</b></para>
/// Store secrets locally with the .NET Secret Manager — never commit real values:
/// <code>
/// dotnet user-secrets set "AzureAd:TenantId" "your-entra-tenant-id"
/// dotnet user-secrets set "AzureAd:ClientId" "your-app-client-id"
/// </code>
///
/// <para><b>Production</b></para>
/// Inject via Azure Key Vault references in App Configuration or environment variables.
/// </summary>
public sealed class EntraIdOptions
{
    /// <summary>The configuration section name that maps to this class.</summary>
    public const string SectionName = "AzureAd";

    /// <summary>
    /// Entra ID authority base URL.
    /// Default: <c>https://login.microsoftonline.com/</c>
    /// </summary>
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    /// <summary>
    /// Directory (tenant) ID of the Entra ID organisation that registered the application.
    /// Use <c>common</c> for multi-tenant applications.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Application (client) ID from the Entra ID App Registration.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Expected token audience. Must match the API scope exposed in the App Registration.
    /// Typically formatted as <c>api://&lt;ClientId&gt;</c>.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Full Entra ID authority URL computed from <see cref="Instance"/> and <see cref="TenantId"/>.
    /// Used internally by Microsoft.Identity.Web for OIDC discovery and JWKS retrieval.
    /// </summary>
    public string Authority => $"{Instance.TrimEnd('/')}/{TenantId}";
}
