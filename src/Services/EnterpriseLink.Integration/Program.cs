using EnterpriseLink.Integration.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json;

const string ServiceName = "EnterpriseLink.Integration";

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting {ServiceName}", ServiceName);

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", ServiceName)
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}"));

    // ── Storage: local filesystem (same root as Ingestion service) ────────────
    builder.Services.AddIntegrationStorage(builder.Configuration);

    // ── Messaging: MassTransit + RabbitMQ ─────────────────────────────────────
    builder.Services.AddIntegrationMessaging(builder.Configuration);

    // ── Story 1: SOAP Adapter ─────────────────────────────────────────────────
    builder.Services.AddSoapAdapter();

    // ── Story 2: REST Adapter ─────────────────────────────────────────────────
    builder.Services.AddRestAdapter();

    // ── Story 3: SFTP Connector ───────────────────────────────────────────────
    builder.Services.AddSftpConnector();

    // ── API: status + manual trigger endpoints ────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title   = "EnterpriseLink Integration Service",
            Version = "v1",
            Description =
                "Adapter host for SOAP, REST, and SFTP legacy integrations. " +
                "Each adapter polls its external system and publishes FileUploadedEvent " +
                "to the Worker via RabbitMQ.",
        });
    });

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Integration service is running"));

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Integration Service v1"));
    }

    app.UseSerilogRequestLogging();
    app.MapControllers();
    app.MapHealthChecks("/health", new HealthCheckOptions
    {
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                status    = report.Status.ToString(),
                service   = ServiceName,
                timestamp = DateTimeOffset.UtcNow,
                checks    = report.Entries.Select(e => new
                {
                    name        = e.Key,
                    status      = e.Value.Status.ToString(),
                    description = e.Value.Description,
                }),
            }));
        },
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

public partial class Program { }
