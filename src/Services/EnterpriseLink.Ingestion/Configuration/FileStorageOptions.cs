using EnterpriseLink.Ingestion.Storage.Local;
using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Ingestion.Configuration;

/// <summary>
/// Top-level strongly-typed configuration for the file storage subsystem.
///
/// <para>
/// Populated from the <c>FileStorage</c> section of <c>appsettings.json</c>.
/// The <see cref="Provider"/> value controls which <c>IFileStorageService</c>
/// implementation is registered at startup — no code change is required to swap providers.
/// </para>
///
/// <para><b>Full configuration example</b></para>
/// <code>
/// {
///   "FileStorage": {
///     "Provider": "local",
///     "Local": {
///       "BasePath": "/var/data/enterpriselink/uploads"
///     }
///   }
/// }
/// </code>
///
/// <para><b>Supported providers</b></para>
/// <list type="table">
///   <listheader>
///     <term>Provider value</term>
///     <description>Implementation</description>
///   </listheader>
///   <item>
///     <term><c>local</c></term>
///     <description>
///       Stores files on the local filesystem. Suitable for development and
///       on-premises deployments. See <see cref="LocalStorageOptions"/>.
///     </description>
///   </item>
///   <item>
///     <term><c>azureblob</c></term>
///     <description>Planned — Azure Blob Storage (future story).</description>
///   </item>
/// </list>
/// </summary>
public sealed class FileStorageOptions
{
    /// <summary>The configuration section name that maps to this class.</summary>
    public const string SectionName = "FileStorage";

    /// <summary>
    /// Selects the active storage provider.
    /// Case-insensitive. Defaults to <c>local</c>.
    /// </summary>
    [Required(ErrorMessage = "FileStorage:Provider is required.")]
    public string Provider { get; set; } = "local";

    /// <summary>
    /// Configuration for the local filesystem provider.
    /// Ignored when <see cref="Provider"/> is not <c>local</c>.
    /// </summary>
    public LocalStorageOptions Local { get; set; } = new();
}
