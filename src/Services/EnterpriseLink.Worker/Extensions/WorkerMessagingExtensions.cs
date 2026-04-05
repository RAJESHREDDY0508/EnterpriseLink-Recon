using EnterpriseLink.Shared.Contracts.Events;
using EnterpriseLink.Worker.Configuration;
using EnterpriseLink.Worker.Consumers;
using MassTransit;

namespace EnterpriseLink.Worker.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering MassTransit,
/// the RabbitMQ transport, and all message consumers for the Worker service.
///
/// <para>
/// All topology, retry, and prefetch configuration lives here so that
/// <c>Program.cs</c> stays declarative and individual consumers stay free of
/// infrastructure concerns.
/// </para>
/// </summary>
public static class WorkerMessagingExtensions
{
    /// <summary>
    /// Registers MassTransit with RabbitMQ transport and wires all Worker service consumers.
    ///
    /// <para><b>Queue topology</b></para>
    /// <list type="table">
    ///   <listheader>
    ///     <term>Queue</term>
    ///     <description>Consumer / purpose</description>
    ///   </listheader>
    ///   <item>
    ///     <term><c>file-uploaded-processing</c></term>
    ///     <description>
    ///       <see cref="FileUploadedEventConsumer"/> — bound to the
    ///       <c>FileUploadedEvent</c> fanout exchange published by the Ingestion service.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Consumer retry policy (acceptance criterion: handles messages)</b></para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>Exponential back-off</b>: base = <c>RetryIntervalSeconds</c>;
    ///       effective waits with defaults: 5 s → 25 s → 125 s.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       After all retries are exhausted, MassTransit moves the message to
    ///       <c>file-uploaded-processing_error</c> (dead-letter queue) for
    ///       manual inspection and reprocessing.
    ///     </description>
    ///   </item>
    /// </list>
    ///
    /// <para><b>Usage in <c>Program.cs</c></b></para>
    /// <code>
    /// builder.Services.AddWorkerMessaging(builder.Configuration);
    /// </code>
    /// </summary>
    /// <param name="services">The DI container to register services into.</param>
    /// <param name="configuration">Application configuration (reads <c>RabbitMQ</c> section).</param>
    /// <returns>The same <see cref="IServiceCollection"/> for fluent chaining.</returns>
    public static IServiceCollection AddWorkerMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<WorkerMessagingOptions>()
            .Bind(configuration.GetSection(WorkerMessagingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var opts = configuration
            .GetSection(WorkerMessagingOptions.SectionName)
            .Get<WorkerMessagingOptions>() ?? new WorkerMessagingOptions();

        services.AddMassTransit(bus =>
        {
            // ── Consumer registrations ────────────────────────────────────────
            bus.AddConsumer<FileUploadedEventConsumer>();

            // ── Transport: RabbitMQ ───────────────────────────────────────────
            bus.UsingRabbitMq((ctx, cfg) =>
            {
                cfg.Host(opts.Host, opts.VirtualHost, h =>
                {
                    h.Username(opts.Username);
                    h.Password(opts.Password);
                });

                // ── Queue: FileUploadedEvent ───────────────────────────────────
                // Explicit queue name keeps it stable across assembly renames.
                // MassTransit binds this queue to the exchange created by the
                // Ingestion service publisher for FileUploadedEvent automatically.
                cfg.ReceiveEndpoint("file-uploaded-processing", ep =>
                {
                    // ── Consumer-level retry: exponential back-off ─────────────
                    // Applied before the consumer runs. Satisfies acceptance
                    // criterion: "Handles messages" (including transient failures).
                    ep.UseMessageRetry(r =>
                        r.Exponential(
                            retryLimit: opts.RetryCount,
                            minInterval: TimeSpan.FromSeconds(1),
                            maxInterval: TimeSpan.FromSeconds(
                                Math.Pow(opts.RetryIntervalSeconds, opts.RetryCount)),
                            intervalDelta: TimeSpan.FromSeconds(opts.RetryIntervalSeconds)));

                    // Concurrent message processing limit per consumer instance.
                    ep.PrefetchCount = opts.PrefetchCount;

                    ep.ConfigureConsumer<FileUploadedEventConsumer>(ctx);
                });
            });
        });

        return services;
    }
}
