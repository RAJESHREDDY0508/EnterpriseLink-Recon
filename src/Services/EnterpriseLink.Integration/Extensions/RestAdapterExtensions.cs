using EnterpriseLink.Integration.Adapters.Rest;
using EnterpriseLink.Integration.Transformation;

namespace EnterpriseLink.Integration.Extensions;

public static class RestAdapterExtensions
{
    public static IServiceCollection AddRestAdapter(this IServiceCollection services)
    {
        services.AddSingleton<RestResponseMapper>();
        services.AddSingleton<JsonDataTransformer>();
        services.AddHostedService<RestAdapterService>();

        return services;
    }
}
