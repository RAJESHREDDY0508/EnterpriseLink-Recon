using MassTransit;

namespace EnterpriseLink.Ingestion.Messaging;

/// <summary>
/// MassTransit implementation of <see cref="IEventPublisher"/>.
///
/// <para>
/// Delegates publish to MassTransit's <see cref="IPublishEndpoint"/>, which resolves
/// the target exchange/topic from the event type name using MassTransit's default
/// topology conventions. For <c>FileUploadedEvent</c> this produces an exchange named
/// <c>EnterpriseLink.Shared.Contracts.Events:FileUploadedEvent</c> in RabbitMQ.
/// </para>
///
/// <para><b>Retry policy</b></para>
/// Retry is configured at the MassTransit bus level in
/// <see cref="Extensions.MessagingServiceExtensions.AddIngestionMessaging"/>, not here.
/// This separation means the retry policy is applied uniformly to all publish operations
/// rather than being duplicated per publisher class.
///
/// The configured policy is:
/// <list type="bullet">
///   <item><description>Up to <b>3 retry attempts</b> on transient exceptions.</description></item>
///   <item><description><b>Exponential back-off</b>: 1 s → 5 s → 25 s intervals.</description></item>
///   <item><description>After all retries are exhausted the exception propagates to the caller.</description></item>
/// </list>
///
/// <para>
/// <b>Observability:</b> each publish is logged at <c>Information</c> level before
/// the call and at <c>Warning</c> level if an exception is thrown, enabling correlation
/// between upload events in Ingestion logs and processing events in Worker logs via
/// <c>UploadId</c>.
/// </para>
/// </summary>
public sealed class MassTransitEventPublisher : IEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<MassTransitEventPublisher> _logger;

    /// <summary>
    /// Initialises the publisher with the MassTransit publish endpoint.
    /// </summary>
    /// <param name="publishEndpoint">
    /// MassTransit scoped publish endpoint injected by the DI container.
    /// Resolved per HTTP request, so publish context (headers, correlation IDs) is
    /// correctly scoped.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    public MassTransitEventPublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        // Null events would produce an untyped message in MassTransit and cause opaque
        // serialisation errors on the broker. Fail fast here with a clear argument error.
        ArgumentNullException.ThrowIfNull(@event);

        var eventType = typeof(TEvent).Name;

        _logger.LogInformation(
            "Publishing {EventType} to message broker.", eventType);

        try
        {
            await _publishEndpoint.Publish(@event, cancellationToken);

            _logger.LogInformation(
                "Successfully published {EventType} to message broker.", eventType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to publish {EventType} to message broker. " +
                "MassTransit retry policy will handle transient failures.", eventType);

            throw;
        }
    }
}
