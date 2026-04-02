namespace EnterpriseLink.Ingestion.Messaging;

/// <summary>
/// Abstraction over the message broker used by the Ingestion service to publish
/// integration events after a file upload has been accepted and stored.
///
/// <para>
/// Decoupling the controller from MassTransit directly allows the publisher to be
/// swapped (e.g. in-memory bus for tests, Azure Service Bus for multi-region) and
/// tested in isolation without an AMQP broker.
/// </para>
///
/// <para><b>Retry contract</b></para>
/// Implementations must honour the retry-on-failure acceptance criterion:
/// transient broker unavailability must trigger configurable retry attempts with
/// exponential back-off before the exception is allowed to propagate.
/// The current implementation delegates retry to MassTransit's built-in policy
/// (see <c>MassTransitEventPublisher</c> and <c>MessagingServiceExtensions</c>).
///
/// <para><b>At-least-once guarantee</b></para>
/// MassTransit with the outbox pattern (future story) provides at-least-once
/// delivery. Consumers of published events must be idempotent on
/// <c>FileUploadedEvent.UploadId</c>.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes an integration event to the message broker.
    ///
    /// <para>
    /// On transient broker failures the implementation retries according to the
    /// configured policy before propagating the exception. If the broker is
    /// completely unavailable after all retries the exception is allowed to bubble
    /// to the controller, which returns a 500 response. The stored file is NOT
    /// rolled back — a separate dead-letter recovery process (future story) will
    /// re-enqueue the event.
    /// </para>
    /// </summary>
    /// <typeparam name="TEvent">
    /// The integration event type. Must be a reference type with a stable schema.
    /// </typeparam>
    /// <param name="event">The event payload to publish.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <returns>A <see cref="Task"/> that completes when the event has been accepted by the broker.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the broker rejects the message or is unreachable after all retries.
    /// </exception>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : class;
}
