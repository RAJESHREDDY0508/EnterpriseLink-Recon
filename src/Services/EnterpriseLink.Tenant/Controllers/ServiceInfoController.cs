using Microsoft.AspNetCore.Mvc;
using System.Reflection;

namespace EnterpriseLink.Tenant.Controllers;

/// <summary>
/// Exposes basic service metadata — used by the Gateway and monitoring tools.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ServiceInfoController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
    {
        return Ok(new
        {
            service = "EnterpriseLink.Tenant",
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0",
            environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
            timestamp = DateTimeOffset.UtcNow
        });
    }
}
