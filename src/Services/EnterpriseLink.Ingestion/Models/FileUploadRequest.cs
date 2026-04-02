namespace EnterpriseLink.Ingestion.Models;

/// <summary>
/// Multipart form model for a CSV file upload request.
///
/// <para>
/// Bound from the HTTP multipart/form-data body by ASP.NET Core's model binder
/// using <c>[FromForm]</c>. All fields are validated by
/// <c>FileUploadRequestValidator</c> before controller logic executes.
/// </para>
///
/// <para><b>Required form fields</b></para>
/// <list type="bullet">
///   <item><description><c>file</c> — the CSV file (part name must match property name).</description></item>
///   <item><description><c>sourceSystem</c> — identifies the upstream system of record.</description></item>
/// </list>
///
/// <para><b>Optional form fields</b></para>
/// <list type="bullet">
///   <item><description><c>description</c> — free-text description of the upload batch.</description></item>
/// </list>
///
/// <para><b>Example curl</b></para>
/// <code>
/// curl -X POST https://host/api/ingestion/upload \
///   -H "Authorization: Bearer {token}" \
///   -F "file=@transactions.csv;type=text/csv" \
///   -F "sourceSystem=Salesforce" \
///   -F "description=Q1 reconciliation batch"
/// </code>
/// </summary>
public sealed class FileUploadRequest
{
    /// <summary>
    /// The CSV file to upload. Must have a <c>.csv</c> extension and a compatible
    /// Content-Type (<c>text/csv</c>, <c>text/plain</c>, or <c>application/octet-stream</c>).
    /// </summary>
    public IFormFile File { get; set; } = null!;

    /// <summary>
    /// Identifies the upstream system of record that produced this file.
    /// Examples: <c>Salesforce</c>, <c>SAP</c>, <c>Oracle-ERP</c>.
    /// Required. 1–100 characters. Letters, digits, hyphens, underscores, and spaces only.
    /// </summary>
    public string SourceSystem { get; set; } = string.Empty;

    /// <summary>
    /// Free-text description of this upload batch (optional).
    /// Maximum 500 characters.
    /// </summary>
    public string? Description { get; set; }
}
