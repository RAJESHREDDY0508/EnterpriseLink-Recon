using EnterpriseLink.Shared.Contracts.Events;
using MassTransit;

namespace EnterpriseLink.Integration.Messaging;

/// <summary>
/// MassTransit implementation of <see cref="IIntegrationEventPublisher"/>.
/// Publishes a <see cref="FileUploadedEvent"/> to the same RabbitMQ exchange
/// that the Ingestion service uses, so the Worker processes adapter files
/// through the identical CSV validation and batch-insert pipeline.
/// </summary>
public sealed class MassTransitIntegrationPublisher : IIntegrationEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitIntegrationPublisher> _logger;

    public MassTransitIntegrationPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitIntegrationPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task PublishFileUploadedAsync(
        Guid tenantId,
        Guid uploadId,
        string storagePath,
        string fileName,
        long fileSizeBytes,
        int dataRowCount,
        string sourceSystem,
        CancellationToken cancellationToken = default)
    {
        var @event = new FileUploadedEvent
        {
            UploadId      = uploadId,
            TenantId      = tenantId,
            StoragePath   = storagePath,
            FileName      = fileName,
            FileSizeBytes = fileSizeBytes,
            DataRowCount  = dataRowCount,
            SourceSystem  = sourceSystem,
            UploadedAt    = DateTimeOffset.UtcNow,
        };

        _logger.LogInformation(
            "Publishing FileUploadedEvent. UploadId={UploadId} SourceSystem={SourceSystem} Rows={Rows}",
            uploadId, sourceSystem, dataRowCount);

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);

            _logger.LogInformation(
                "FileUploadedEvent published successfully. UploadId={UploadId}", uploadId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to publish FileUploadedEvent. UploadId={UploadId}", uploadId);
            throw;
        }
    }
}
