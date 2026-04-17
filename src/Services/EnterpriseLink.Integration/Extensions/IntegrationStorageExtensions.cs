using EnterpriseLink.Integration.Configuration;
using EnterpriseLink.Integration.Storage;

namespace EnterpriseLink.Integration.Extensions;

public static class IntegrationStorageExtensions
{
    public static IServiceCollection AddIntegrationStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<IntegrationOptions>()
            .Bind(configuration.GetSection(IntegrationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IIntegrationFileStore, LocalIntegrationFileStore>();
        return services;
    }
}
