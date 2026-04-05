using EnterpriseLink.Ingestion.Configuration;
using EnterpriseLink.Ingestion.Messaging;
using EnterpriseLink.Shared.Contracts.Events;
using MassTransit;

namespace EnterpriseLink.Ingestion.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering MassTransit
/// and the RabbitMQ message broker connection used by the Ingestion service.
///
/// <para>
/// All retry and topology configuration lives here so that <c>Program.cs</c>
/// stays declarative and individual publisher/consumer classes stay free of
/// infrastructure concerns.
/// </para>
/// </summary>
public static class MessagingServiceExtensions
{
    /// <summary>
    /// Registers MassTransit with RabbitMQ transport and an exponential back-off
    /// retry policy for all publish operations.
    ///
    /// <para><b>Topology</b></para>
    /// MassTransit creates a fanout exchange per event type using the fully-qualified
    /// type name as the exchange name. For <see cref="FileUploadedEvent"/> this produces:
    /// <c>EnterpriseLink.Shared.Contracts.Events:FileUploadedEvent</c>.
    /// The Worker service binds its consumer queue to this exchange.
    ///
    /// <para><b>Retry policy (acceptance criterion: retry on failure)</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>Exponential back-off</b>: interval = <c>RetryIntervalSeconds ^ attempt</c>.
    ///       With defaults (base = 5): 5 s → 25 s → 125 s for 3 attempts.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       Retry applies to transient exceptions (broker unreachable, connection reset).
    ///       Non-transient exceptions (schema violations) propagate immediately.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       After all retries are exhausted the exception bubbles to the controller,
    ///       which returns HTTP 500. The stored file is not rolled back.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Usage in <c>Program.cs</c></b></para>
    /// <code>
    /// builder.Services.AddIngestionMessaging(builder.Configuration);
    /// </code>
    /// </summary>
    /// <param name="services">The DI container to register services into.</param>
    /// <param name="configuration">Application configuration (reads <c>RabbitMQ</c> section).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    public static IServiceCollection AddIngestionMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register and validate options at startup.
        services
            .AddOptions<MessagingOptions>()
            .Bind(configuration.GetSection(MessagingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var opts = configuration
            .GetSection(MessagingOptions.SectionName)
            .Get<MessagingOptions>() ?? new MessagingOptions();

        services.AddMassTransit(bus =>
        {
            // ── Transport: RabbitMQ ───────────────────────────────────────────
            bus.UsingRabbitMq((ctx, cfg) =>
            {
                // Wrap host configuration so that if the broker throws during startup
                // (e.g. wrong VirtualHost name) the exception message never contains the
                // raw password. Credentials are replaced with a safe placeholder.
                try
                {
                    cfg.Host(opts.Host, opts.VirtualHost, h =>
                    {
                        h.Username(opts.Username);
                        h.Password(opts.Password);
                    });
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to configure RabbitMQ host '{opts.Host}/{opts.VirtualHost}' " +
                        $"for user '{opts.Username}'. " +
                        "Check RabbitMQ connectivity and credentials in configuration. " +
                        $"Inner: {ex.Message}", ex);
                }

                // ── Retry policy: exponential back-off ────────────────────────
                // Applied to all Send/Publish operations from this bus instance.
                // Satisfies acceptance criterion: "Retry on failure".
                cfg.UseMessageRetry(r =>
                    r.Exponential(
                        retryLimit: opts.RetryCount,
                        minInterval: TimeSpan.FromSeconds(1),
                        maxInterval: TimeSpan.FromSeconds(
                            Math.Pow(opts.RetryIntervalSeconds, opts.RetryCount)),
                        intervalDelta: TimeSpan.FromSeconds(opts.RetryIntervalSeconds)));

                // Configure endpoints for all registered consumers (none in Ingestion;
                // Worker service registers its consumers against the same exchange).
                cfg.ConfigureEndpoints(ctx);
            });
        });

        // Register the publisher abstraction — Scoped matches MassTransit's IPublishEndpoint lifetime.
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        return services;
    }
}
