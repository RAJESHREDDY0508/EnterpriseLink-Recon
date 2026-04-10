using EnterpriseLink.Dashboard.Components;
using EnterpriseLink.Dashboard.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json;

const string ServiceName = "EnterpriseLink.Dashboard";

// ── Bootstrap logger ──────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting {ServiceName}", ServiceName);

    var builder = WebApplication.CreateBuilder(args);

    // ── Structured Logging (Serilog) ──────────────────────────────────────────
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", ServiceName)
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}"));

    // ── EF Core + Dashboard query services ───────────────────────────────────
    builder.Services.AddDashboardServices(builder.Configuration);

    // ── API ───────────────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "EnterpriseLink Dashboard API",
            Version = "v1",
            Description =
                "Read-only operational dashboard API. Exposes batch status, " +
                "validation errors, and the audit trail for the EnterpriseLink Recon platform.",
        });

        var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            c.IncludeXmlComments(xmlPath);
    });

    // ── Blazor Server (Frontend Dashboard) ───────────────────────────────────
    // AddRazorComponents enables Razor component rendering.
    // AddInteractiveServerComponents enables real-time SignalR-backed interactivity
    // (used by the Batch Monitor, Error Viewer, and Audit Logs pages for 30-second
    // polling refresh via PeriodicTimer without a page reload).
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Dashboard service is running"));

    // ── Build & Pipeline ──────────────────────────────────────────────────────
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Dashboard API v1"));
    }

    app.UseSerilogRequestLogging();
    app.UseStaticFiles();
    app.UseAntiforgery();
    app.UseAuthorization();

    // API controllers — serve /api/* endpoints for external consumers.
    app.MapControllers();

    // Blazor Server — serve the interactive frontend on all non-API routes.
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    // Health endpoint.
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = WriteHealthResponse,
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "{ServiceName} terminated unexpectedly", ServiceName);
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// ── Health response writer ────────────────────────────────────────────────────
static async Task WriteHealthResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var response = new
    {
        status = report.Status.ToString(),
        service = ServiceName,
        timestamp = DateTimeOffset.UtcNow,
        duration = report.TotalDuration,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration,
        }),
    };
    await context.Response.WriteAsync(
        JsonSerializer.Serialize(response,
            new JsonSerializerOptions { WriteIndented = false }));
}

// Required for Microsoft.AspNetCore.Mvc.Testing integration test factory.
public partial class Program { }
