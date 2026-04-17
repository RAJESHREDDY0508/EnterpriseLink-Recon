using EnterpriseLink.Integration.Adapters.Rest;
using EnterpriseLink.Integration.Adapters.Sftp;
using EnterpriseLink.Integration.Adapters.Soap;
using EnterpriseLink.Integration.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace EnterpriseLink.Integration.Controllers;

/// <summary>
/// Provides status visibility and manual trigger endpoints for all configured adapters.
/// </summary>
[ApiController]
[Route("api/integration")]
public sealed class IntegrationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly SoapAdapterService _soapService;
    private readonly RestAdapterService _restService;
    private readonly SftpConnectorService _sftpService;
    private readonly ILogger<IntegrationController> _logger;

    public IntegrationController(
        IConfiguration configuration,
        SoapAdapterService soapService,
        RestAdapterService restService,
        SftpConnectorService sftpService,
        ILogger<IntegrationController> logger)
    {
        _configuration = configuration;
        _soapService   = soapService;
        _restService   = restService;
        _sftpService   = sftpService;
        _logger        = logger;
    }

    /// <summary>Lists all configured adapters and their enabled state.</summary>
    [HttpGet("adapters")]
    public IActionResult GetAdapters()
    {
        var soap = _configuration.GetSection("SoapAdapters")
            .Get<List<SoapAdapterOptions>>() ?? [];
        var rest = _configuration.GetSection("RestAdapters")
            .Get<List<RestAdapterOptions>>() ?? [];
        var sftp = _configuration.GetSection("SftpConnectors")
            .Get<List<SftpConnectorOptions>>() ?? [];

        return Ok(new
        {
            soap = soap.Select(a => new { a.Name, a.Enabled, a.SourceSystem, a.PollingIntervalSeconds }),
            rest = rest.Select(a => new { a.Name, a.Enabled, a.SourceSystem, a.PollingIntervalSeconds }),
            sftp = sftp.Select(a => new { a.Name, a.Enabled, a.SourceSystem, a.PollingIntervalSeconds }),
        });
    }

    /// <summary>
    /// Manually triggers a single SOAP adapter cycle by name.
    /// Useful for testing without waiting for the polling interval.
    /// </summary>
    [HttpPost("soap/{name}/trigger")]
    public async Task<IActionResult> TriggerSoap(string name, CancellationToken ct)
    {
        var adapters = _configuration.GetSection("SoapAdapters")
            .Get<List<SoapAdapterOptions>>() ?? [];

        var adapter = adapters.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        if (adapter is null)
            return NotFound(new { error = $"SOAP adapter '{name}' not found." });

        _logger.LogInformation("Manual trigger: SOAP adapter '{Name}'", name);
        await _soapService.RunCycleAsync(adapter, ct);
        return Ok(new { triggered = name, type = "soap" });
    }

    /// <summary>Manually triggers a single REST adapter cycle by name.</summary>
    [HttpPost("rest/{name}/trigger")]
    public async Task<IActionResult> TriggerRest(string name, CancellationToken ct)
    {
        var adapters = _configuration.GetSection("RestAdapters")
            .Get<List<RestAdapterOptions>>() ?? [];

        var adapter = adapters.FirstOrDefault(a =>
            string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));

        if (adapter is null)
            return NotFound(new { error = $"REST adapter '{name}' not found." });

        _logger.LogInformation("Manual trigger: REST adapter '{Name}'", name);
        await _restService.RunCycleAsync(adapter, ct);
        return Ok(new { triggered = name, type = "rest" });
    }

    /// <summary>Manually triggers a single SFTP connector cycle by name.</summary>
    [HttpPost("sftp/{name}/trigger")]
    public async Task<IActionResult> TriggerSftp(string name, CancellationToken ct)
    {
        var connectors = _configuration.GetSection("SftpConnectors")
            .Get<List<SftpConnectorOptions>>() ?? [];

        var connector = connectors.FirstOrDefault(c =>
            string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        if (connector is null)
            return NotFound(new { error = $"SFTP connector '{name}' not found." });

        _logger.LogInformation("Manual trigger: SFTP connector '{Name}'", name);
        await _sftpService.RunCycleAsync(connector, ct);
        return Ok(new { triggered = name, type = "sftp" });
    }
}
