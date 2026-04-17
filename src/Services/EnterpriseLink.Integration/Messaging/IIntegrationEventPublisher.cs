namespace EnterpriseLink.Integration.Messaging;

/// <summary>
/// Publishes a <c>FileUploadedEvent</c> to the message broker after an adapter
/// has written its transformed CSV to local storage. The Worker service consumes
/// the event and processes the CSV through the standard validation pipeline.
/// </summary>
public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes a <c>FileUploadedEvent</c> for the given adapter-produced file.
    /// </summary>
    /// <param name="tenantId">Tenant that owns the ingested records.</param>
    /// <param name="uploadId">Unique ID for this ingestion cycle — used as idempotency key.</param>
    /// <param name="storagePath">Relative path returned by <see cref="Storage.IIntegrationFileStore"/>.</param>
    /// <param name="fileName">Original or generated file name.</param>
    /// <param name="fileSizeBytes">Size of the written file in bytes.</param>
    /// <param name="dataRowCount">Estimated data row count (header excluded).</param>
    /// <param name="sourceSystem">Source system identifier (e.g. <c>LegacyERP-SOAP</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task PublishFileUploadedAsync(
        Guid tenantId,
        Guid uploadId,
        string storagePath,
        string fileName,
        long fileSizeBytes,
        int dataRowCount,
        string sourceSystem,
        CancellationToken cancellationToken = default);
}
