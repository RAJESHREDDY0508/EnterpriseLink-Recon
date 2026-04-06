using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Worker.Storage;

/// <summary>
/// Strongly-typed configuration for the local file storage root used by the Worker
/// service when resolving relative storage paths to absolute filesystem paths.
///
/// <para>
/// The <c>BasePath</c> must match the <c>FileStorage:Local:BasePath</c> configured in
/// the Ingestion service — both services share the same physical storage root so that
/// files written by Ingestion can be read by the Worker.
/// </para>
///
/// <para><b>Configuration example</b></para>
/// <code>
/// {
///   "FileStorage": {
///     "Local": {
///       "BasePath": "uploads"
///     }
///   }
/// }
/// </code>
/// </summary>
public sealed class FileStorageResolverOptions
{
    /// <summary>Configuration section path that maps to this class.</summary>
    public const string SectionName = "FileStorage:Local";

    /// <summary>
    /// Root directory where the Ingestion service stores uploaded files.
    /// May be an absolute path or a path relative to the application base directory.
    /// Must be accessible (readable) by the Worker service process.
    /// Default: <c>uploads</c>.
    /// </summary>
    [Required(ErrorMessage = "FileStorage:Local:BasePath is required.")]
    public string BasePath { get; set; } = "uploads";
}
