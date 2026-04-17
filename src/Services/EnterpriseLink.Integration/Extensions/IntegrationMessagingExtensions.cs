using EnterpriseLink.Integration.Configuration;
using EnterpriseLink.Integration.Messaging;
using EnterpriseLink.Shared.Contracts.Events;
using MassTransit;

namespace EnterpriseLink.Integration.Extensions;

public static class IntegrationMessagingExtensions
{
    public static IServiceCollection AddIntegrationMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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
            bus.UsingRabbitMq((_, cfg) =>
            {
                cfg.Host(opts.Host, opts.VirtualHost, h =>
                {
                    h.Username(opts.Username);
                    h.Password(opts.Password);
                });

                cfg.UseMessageRetry(r =>
                    r.Exponential(
                        retryLimit: opts.RetryCount,
                        minInterval: TimeSpan.FromSeconds(1),
                        maxInterval: TimeSpan.FromSeconds(
                            Math.Pow(opts.RetryIntervalSeconds, opts.RetryCount)),
                        intervalDelta: TimeSpan.FromSeconds(opts.RetryIntervalSeconds)));
            });
        });

        services.AddScoped<IIntegrationEventPublisher, MassTransitIntegrationPublisher>();
        return services;
    }
}
