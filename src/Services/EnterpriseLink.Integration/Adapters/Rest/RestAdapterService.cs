using System.Text;
using EnterpriseLink.Integration.Configuration;
using EnterpriseLink.Integration.Messaging;
using EnterpriseLink.Integration.Storage;
using EnterpriseLink.Integration.Transformation;

namespace EnterpriseLink.Integration.Adapters.Rest;

/// <summary>
/// Background service that polls one or more REST API endpoints on a configurable
/// schedule, maps JSON responses to CSV, stores the file, and publishes a
/// <c>FileUploadedEvent</c> for the Worker service to process.
///
/// <para><b>Acceptance criterion:</b> External APIs integrated.</para>
/// <list type="bullet">
///   <item>Supports Bearer, ApiKey, and Basic authentication schemes.</item>
///   <item>Navigates nested JSON response using <c>ResponseArrayPath</c>.</item>
///   <item>Maps JSON fields to internal CSV schema via <c>FieldMappings</c>.</item>
///   <item>Publishes <c>FileUploadedEvent</c> → Worker ingests data.</item>
/// </list>
/// </summary>
public sealed class RestAdapterService : BackgroundService
{
    private readonly IReadOnlyList<RestAdapterOptions> _adapters;
    private readonly RestResponseMapper _mapper;
    private readonly JsonDataTransformer _transformer;
    private readonly IIntegrationFileStore _fileStore;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly ILogger<RestAdapterService> _logger;

    public RestAdapterService(
        IConfiguration configuration,
        RestResponseMapper mapper,
        JsonDataTransformer transformer,
        IIntegrationFileStore fileStore,
        IIntegrationEventPublisher publisher,
        ILogger<RestAdapterService> logger)
    {
        _adapters    = configuration.GetSection("RestAdapters")
                           .Get<List<RestAdapterOptions>>() ?? [];
        _mapper      = mapper;
        _transformer = transformer;
        _fileStore   = fileStore;
        _publisher   = publisher;
        _logger      = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = _adapters.Where(a => a.Enabled).ToList();

        if (enabled.Count == 0)
        {
            _logger.LogInformation("RestAdapterService: no enabled adapters configured — idle.");
            return;
        }

        _logger.LogInformation(
            "RestAdapterService starting {Count} adapter(s): {Names}",
            enabled.Count, string.Join(", ", enabled.Select(a => a.Name)));

        var tasks = enabled.Select(a => PollLoopAsync(a, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task PollLoopAsync(RestAdapterOptions adapter, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(
            adapter.PollingIntervalSeconds > 0 ? adapter.PollingIntervalSeconds : 300);

        using var timer = new PeriodicTimer(interval);

        _logger.LogInformation(
            "REST adapter '{Name}' polling every {Interval}s", adapter.Name, interval.TotalSeconds);

        do
        {
            await RunCycleAsync(adapter, ct);
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    internal async Task RunCycleAsync(RestAdapterOptions adapter, CancellationToken ct)
    {
        _logger.LogInformation("REST adapter '{Name}' — starting cycle", adapter.Name);

        try
        {
            // Build URL with optional query parameters
            var url = adapter.BaseUrl;
            if (adapter.QueryParameters.Count > 0)
            {
                var qs = string.Join("&",
                    adapter.QueryParameters.Select(kv =>
                        $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
                url = $"{url}?{qs}";
            }

            using var handler = new RestAuthHandler(adapter);
            using var http    = new HttpClient(handler, disposeHandler: false)
            {
                Timeout = TimeSpan.FromSeconds(adapter.TimeoutSeconds),
            };

            var method  = new HttpMethod(adapter.Method.ToUpperInvariant());
            using var request  = new HttpRequestMessage(method, url);
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var rawJson = await response.Content.ReadAsStringAsync(ct);

            // Navigate to the array
            var arrayJson = _mapper.ExtractArray(rawJson, adapter.ResponseArrayPath);

            var result = _transformer.Transform(
                arrayJson, adapter.FieldMappings, adapter.SourceSystem, adapter.Name);

            if (result.RowCount == 0)
            {
                _logger.LogInformation(
                    "REST adapter '{Name}' — 0 rows returned, skipping publish", adapter.Name);
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
                "REST adapter '{Name}' — cycle complete. Rows={Rows} UploadId={UploadId}",
                adapter.Name, result.RowCount, uploadId);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "REST adapter '{Name}' — cycle failed. Will retry on next interval.",
                adapter.Name);
        }
    }
}
