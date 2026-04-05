namespace EnterpriseLink.Shared.Contracts.Schemas;

/// <summary>
/// Stable identifiers for all versioned JSON Schema files embedded in this assembly.
///
/// <para>
/// Schema files live under <c>schemas/messages/{version}/</c> at the solution root and
/// are copied into this project as embedded resources at build time. Using constants
/// avoids magic strings scattered across validators and tests.
/// </para>
///
/// <para><b>Embedding convention</b></para>
/// Each schema file is embedded with the logical name:
/// <c>EnterpriseLink.Shared.Contracts.Schemas.{version}.{FileName}</c>
/// where <c>{FileName}</c> is the file name with hyphens replaced by underscores.
///
/// <para><b>Adding a new schema</b></para>
/// <list type="number">
///   <item>Add the <c>.schema.json</c> file to <c>schemas/messages/{version}/</c>.</item>
///   <item>Copy it into this project folder and mark it as <c>EmbeddedResource</c>.</item>
///   <item>Add a constant to this class for use by validators and tests.</item>
/// </list>
/// </summary>
public static class KnownSchemas
{
    /// <summary>
    /// Embedded resource name for the <c>FileUploadedEvent</c> v1 JSON Schema.
    /// </summary>
    public const string FileUploadedEventV1 =
        "EnterpriseLink.Shared.Contracts.Schemas.v1.file_uploaded_event.schema.json";
}
