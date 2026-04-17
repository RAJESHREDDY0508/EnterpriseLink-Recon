using EnterpriseLink.Integration.Adapters.Soap;
using EnterpriseLink.Integration.Transformation;

namespace EnterpriseLink.Integration.Extensions;

public static class SoapAdapterExtensions
{
    public static IServiceCollection AddSoapAdapter(this IServiceCollection services)
    {
        services.AddHttpClient("soap");
        services.AddHttpClient<WsdlInspector>();

        services.AddSingleton<WsdlInspector>();
        services.AddSingleton<SoapEnvelopeBuilder>();
        services.AddSingleton<SoapResponseParser>();
        services.AddSingleton<XmlDataTransformer>();
        services.AddHostedService<SoapAdapterService>();

        return services;
    }
}
