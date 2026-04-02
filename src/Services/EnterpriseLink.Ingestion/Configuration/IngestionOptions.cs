using System.ComponentModel.DataAnnotations;

namespace EnterpriseLink.Ingestion.Configuration;

/// <summary>
/// Strongly-typed configuration for the Ingestion service.
///
/// <para>Populated from the <c>Ingestion</c> section of <c>appsettings.json</c>.</para>
///
/// <para><b>Configuration keys</b></para>
/// <list type="table">
///   <listheader>
///     <term>Key</term>
///     <description>Purpose</description>
///   </listheader>
///   <item>
///     <term>MaxFileSizeBytes</term>
///     <description>
///       Maximum accepted file size in bytes. Enforced by both Kestrel (connection layer)
///       and FluentValidation (application layer). Default: 524 288 000 (500 MB).
///     </description>
///   </item>
///   <item>
///     <term>MemoryBufferThresholdBytes</term>
///     <description>
///       Files smaller than this threshold are held in memory; larger files are spooled
///       to a temp file on disk. Default: 1 048 576 (1 MB). Lowering this reduces peak
///       memory use at the cost of more disk I/O.
///     </description>
///   </item>
/// </list>
///
/// <para><b>Registration</b></para>
/// <code>
/// builder.Services
///     .AddOptions&lt;IngestionOptions&gt;()
///     .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
///     .ValidateDataAnnotations()
///     .ValidateOnStart();
/// </code>
/// </summary>
public sealed class IngestionOptions
{
    /// <summary>The configuration section name that maps to this class.</summary>
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Maximum accepted upload file size in bytes.
    /// Files exceeding this limit are rejected at the Kestrel level (connection closed)
    /// before reaching controller code.
    /// Default: 524 288 000 bytes (500 MB).
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "MaxFileSizeBytes must be greater than zero.")]
    public long MaxFileSizeBytes { get; set; } = 524_288_000L;

    /// <summary>
    /// Files smaller than this threshold are held in memory during the request.
    /// Larger files are automatically spooled to disk by ASP.NET Core's form pipeline.
    /// Default: 1 048 576 bytes (1 MB).
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "MemoryBufferThresholdBytes must be greater than zero.")]
    public int MemoryBufferThresholdBytes { get; set; } = 1_048_576;
}
