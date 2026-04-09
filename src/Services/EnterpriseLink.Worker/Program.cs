using EnterpriseLink.Worker.Extensions;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;
using System.Text.Json;

const string ServiceName = "EnterpriseLink.Worker";

// ── Bootstrap logger ──────────────────────────────────────────────────────────
// Worker uses WebApplication so it can expose /health over HTTP while
// MassTransit's IHostedService runs the message consumer loop.
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

    // ── Persistence, batch insert & idempotency ───────────────────────────────
    // Registers AppDbContext (SQL Server), WorkerTenantContext (scoped per message),
    // IBatchRowInserter (commit every N rows) and IUploadIdempotencyGuard (EF Core).
    // Acceptance criteria: "Commit every N records" + "Duplicate processing avoided".
    builder.Services.AddWorkerPersistence(builder.Configuration);

    // ── Storage resolver + CSV streaming parser ───────────────────────────────
    // Registers IFileStorageResolver (resolves relative path → absolute path) and
    // ICsvStreamingParser (CsvHelper-backed, IAsyncEnumerable, O(1) memory).
    // Acceptance criteria: "Handles 5GB files" + "No memory overflow".
    builder.Services.AddWorkerStorage(builder.Configuration);

    // ── Messaging (MassTransit + RabbitMQ) ───────────────────────────────────
    // Registers FileUploadedEventConsumer on the "file-uploaded-processing" queue.
    // MassTransit's IHostedService starts the consumer when the host starts.
    // Acceptance criteria: "Subscribes to queue" + "Handles messages".
    builder.Services.AddWorkerMessaging(builder.Configuration);

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Worker service is running"));

    // ── Build & Pipeline ──────────────────────────────────────────────────────
    var app = builder.Build();

    app.UseSerilogRequestLogging();

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
        JsonSerializer.Serialize(response, new JsonSerializerOptions { WriteIndented = false }));
}

// Required for Microsoft.AspNetCore.Mvc.Testing integration test factory
public partial class Program { }
