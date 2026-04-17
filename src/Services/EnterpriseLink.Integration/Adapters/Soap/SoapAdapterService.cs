using System.Net.Http.Headers;
using System.Text;
using EnterpriseLink.Integration.Configuration;
using EnterpriseLink.Integration.Messaging;
using EnterpriseLink.Integration.Storage;
using EnterpriseLink.Integration.Transformation;

namespace EnterpriseLink.Integration.Adapters.Soap;

/// <summary>
/// Background service that polls one or more SOAP endpoints on a configurable
/// schedule, transforms the XML response to CSV, stores the file, and publishes
/// a <c>FileUploadedEvent</c> for the Worker service to process.
///
/// <para><b>Acceptance criterion:</b> WSDL-based integration works.</para>
/// <list type="bullet">
///   <item>Fetches WSDL at startup and validates the configured operation exists.</item>
///   <item>Builds a SOAP 1.1 envelope for the configured operation.</item>
///   <item>POSTs to the SOAP endpoint with the correct <c>SOAPAction</c> header.</item>
///   <item>Parses the XML response, strips the envelope, transforms to CSV.</item>
///   <item>Stores CSV and publishes <c>FileUploadedEvent</c> → Worker ingests data.</item>
/// </list>
/// </summary>
public sealed class SoapAdapterService : BackgroundService
{
    private readonly IReadOnlyList<SoapAdapterOptions> _adapters;
    private readonly WsdlInspector _wsdlInspector;
    private readonly SoapEnvelopeBuilder _envelopeBuilder;
    private readonly SoapResponseParser _responseParser;
    private readonly XmlDataTransformer _transformer;
    private readonly IIntegrationFileStore _fileStore;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<SoapAdapterService> _logger;

    public SoapAdapterService(
        IConfiguration configuration,
        WsdlInspector wsdlInspector,
        SoapEnvelopeBuilder envelopeBuilder,
        SoapResponseParser responseParser,
        XmlDataTransformer transformer,
        IIntegrationFileStore fileStore,
        IIntegrationEventPublisher publisher,
        IHttpClientFactory httpFactory,
        ILogger<SoapAdapterService> logger)
    {
        _adapters        = configuration.GetSection("SoapAdapters")
                               .Get<List<SoapAdapterOptions>>() ?? [];
        _wsdlInspector   = wsdlInspector;
        _envelopeBuilder = envelopeBuilder;
        _responseParser  = responseParser;
        _transformer     = transformer;
        _fileStore       = fileStore;
        _publisher       = publisher;
        _httpFactory     = httpFactory;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _adapters.Where(a => a.Enabled).ToList();

        if (enabled.Count == 0)
        {
            _logger.LogInformation("SoapAdapterService: no enabled adapters configured — idle.");
            return;
        }

        _logger.LogInformation(
            "SoapAdapterService starting {Count} adapter(s): {Names}",
            enabled.Count, string.Join(", ", enabled.Select(a => a.Name)));

        // Validate each adapter's WSDL at startup (non-fatal — log and continue)
        foreach (var adapter in enabled)
        {
            try
            {
                await _wsdlInspector.ValidateOperationAsync(
                    adapter.WsdlUrl, adapter.OperationName, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SoapAdapterService: WSDL validation failed for {Adapter} — " +
                    "will still attempt polling", adapter.Name);
            }
        }

        // Start one polling loop per adapter
        var tasks = enabled.Select(a => PollLoopAsync(a, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task PollLoopAsync(SoapAdapterOptions adapter, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(
            adapter.PollingIntervalSeconds > 0 ? adapter.PollingIntervalSeconds : 300);

        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "SOAP adapter '{Name}' polling every {Interval}s", adapter.Name, interval.TotalSeconds);

        // Fire immediately on first tick, then respect the interval
        do
        {
            await RunCycleAsync(adapter, ct);
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    internal async Task RunCycleAsync(SoapAdapterOptions adapter, CancellationToken ct)
    {
        _logger.LogInformation("SOAP adapter '{Name}' — starting cycle", adapter.Name);

        try
        {
            var envelope = _envelopeBuilder.Build(
                adapter.OperationName,
                adapter.SoapNamespace,
                adapter.RequestParameters);

            var http = _httpFactory.CreateClient("soap");
            http.Timeout = TimeSpan.FromSeconds(adapter.TimeoutSeconds);

            using var request = new HttpRequestMessage(HttpMethod.Post, adapter.EndpointUrl)
            {
                Content = new StringContent(envelope, Encoding.UTF8, "text/xml"),
            };

            if (!string.IsNullOrWhiteSpace(adapter.SoapAction))
                request.Headers.Add("SOAPAction", $"\"{adapter.SoapAction}\"");

            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var rawXml = await response.Content.ReadAsStringAsync(ct);
            var bodyXml = _responseParser.ExtractBody(rawXml);

            var result = _transformer.Transform(
                bodyXml, adapter.FieldMappings, adapter.SourceSystem, adapter.Name);

            if (result.RowCount == 0)
            {
                _logger.LogInformation(
                    "SOAP adapter '{Name}' — 0 rows returned, skipping publish", adapter.Name);
                return;
            }

            var uploadId = Guid.NewGuid();
            var storagePath = await _fileStore.WriteAsync(
                adapter.TenantId, uploadId, result.SuggestedFileName,
                result.CsvContent, ct);

            var fileBytes = Encoding.UTF8.GetByteCount(result.CsvContent);

            await _publisher.PublishFileUploadedAsync(
                adapter.TenantId, uploadId, storagePath,
                result.SuggestedFileName, fileBytes,
                result.RowCount, adapter.SourceSystem, ct);

            _logger.LogInformation(
                "SOAP adapter '{Name}' — cycle complete. Rows={Rows} UploadId={UploadId}",
                adapter.Name, result.RowCount, uploadId);
        }
        catch (OperationCanceledException) { /* host shutting down */ }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SOAP adapter '{Name}' — cycle failed. Will retry on next interval.",
                adapter.Name);
        }
    }
}
