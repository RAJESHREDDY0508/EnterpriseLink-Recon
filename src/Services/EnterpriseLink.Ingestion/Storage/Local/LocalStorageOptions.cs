using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Ingestion.Storage.Local;

/// <summary>
/// Configuration for the local filesystem storage provider.
///
/// <para>
/// Nested under <c>FileStorage:Local</c> in <c>appsettings.json</c>.
/// The full configuration key path is <c>FileStorage:Local:BasePath</c>.
/// </para>
///
/// <para><b>Example</b></para>
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
/// <para><b>Directory layout created by <see cref="LocalFileStorageService"/></b></para>
/// <code>
/// {BasePath}/
///   └── {tenantId}/
///         └── {uploadId}/
///               └── {fileName}
/// </code>
/// </summary>
public sealed class LocalStorageOptions
{
    /// <summary>
    /// Root directory under which all uploaded files are stored.
    /// Supports absolute and relative paths.
    /// Relative paths are resolved from the application's working directory.
    ///
    /// <para>Default: <c>uploads</c> (relative — resolves to the service's working directory).</para>
    ///
    /// <para>
    /// <b>Production</b>: use an absolute path on a mounted persistent volume,
    /// e.g. <c>/mnt/data/enterpriselink/uploads</c> or <c>D:\EnterpriseLink\Uploads</c>.
    /// </para>
    /// </summary>
    [Required(ErrorMessage = "FileStorage:Local:BasePath is required when using the local provider.")]
    [MinLength(1, ErrorMessage = "FileStorage:Local:BasePath must not be empty.")]
    public string BasePath { get; set; } = "uploads";
}
