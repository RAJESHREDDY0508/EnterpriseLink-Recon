using System.Net.Http.Headers;
using System.Text;
using EnterpriseLink.Integration.Configuration;

namespace EnterpriseLink.Integration.Adapters.Rest;

/// <summary>
/// Delegating HTTP handler that applies the configured authentication scheme
/// to every outgoing request made by the REST adapter.
/// </summary>
public sealed class RestAuthHandler : DelegatingHandler
{
    private readonly RestAdapterOptions _options;

    public RestAuthHandler(RestAdapterOptions options)
        : base(new HttpClientHandler())
    {
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        switch (_options.AuthScheme)
        {
            case RestAuthScheme.Bearer:
                if (!string.IsNullOrWhiteSpace(_options.AuthToken))
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Bearer", _options.AuthToken);
                break;

            case RestAuthScheme.ApiKey:
                if (!string.IsNullOrWhiteSpace(_options.AuthToken))
                    request.Headers.TryAddWithoutValidation(
                        _options.ApiKeyHeader, _options.AuthToken);
                break;

            case RestAuthScheme.Basic:
                if (!string.IsNullOrWhiteSpace(_options.BasicCredentials))
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue("Basic", _options.BasicCredentials);
                break;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
