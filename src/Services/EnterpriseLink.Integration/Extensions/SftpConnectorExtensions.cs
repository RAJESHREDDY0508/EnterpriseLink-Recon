using EnterpriseLink.Integration.Adapters.Sftp;
using EnterpriseLink.Integration.Transformation;

namespace EnterpriseLink.Integration.Extensions;

public static class SftpConnectorExtensions
{
    public static IServiceCollection AddSftpConnector(this IServiceCollection services)
    {
        services.AddSingleton<CsvPassThroughTransformer>();
        services.AddHostedService<SftpConnectorService>();

        return services;
    }
}
