namespace EnterpriseLink.Dashboard.Dtos;

/// <summary>
/// Read-only projection of an <c>InvalidTransaction</c> row for the Error Viewer dashboard.
///
/// <para>
/// Exposes the data needed for operators to diagnose and correct rejected CSV rows:
/// the source upload, the 1-based row number within the file, the raw field data,
/// the formatted validation errors, and the pipeline stage that first rejected the row.
/// </para>
/// </summary>
/// <param name="InvalidTransactionId">Primary key of the error record.</param>
/// <param name="UploadId">The upload batch that produced this invalid row.</param>
/// <param name="TenantId">The tenant that owns this record.</param>
/// <param name="RowNumber">1-based row number within the source CSV file (header excluded).</param>
/// <param name="RawData">
/// JSON-serialised dictionary of column → value pairs from the source CSV row.
/// Preserved verbatim for diagnostic replay.
/// </param>
/// <param name="ValidationErrors">
/// JSON array of formatted error strings: <c>"[ErrorCode] FieldName: Message"</c>.
/// </param>
/// <param name="FailureReason">
/// Pipeline stage that first rejected the row: <c>Schema</c>, <c>BusinessRule</c>,
/// or <c>Duplicate</c>.
/// </param>
/// <param name="CreatedAt">UTC timestamp when this error record was persisted.</param>
public sealed record InvalidTransactionDto(
    Guid InvalidTransactionId,
    Guid UploadId,
    Guid TenantId,
    int RowNumber,
    string RawData,
    string ValidationErrors,
    string FailureReason,
    DateTimeOffset CreatedAt);
