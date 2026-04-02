using EnterpriseLink.Ingestion.Configuration;
using EnterpriseLink.Ingestion.Extensions;
using EnterpriseLink.Ingestion.Models;
using EnterpriseLink.Ingestion.Validation;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json;

const string ServiceName = "EnterpriseLink.Ingestion";

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

    // ── Ingestion options (strongly-typed config with startup validation) ──────
    builder.Services
        .AddOptions<IngestionOptions>()
        .Bind(builder.Configuration.GetSection(IngestionOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ── Kestrel: set max request size from config ─────────────────────────────
    // This enforces the limit at the TCP/connection layer before any controller
    // code runs. Using long.MaxValue here and letting the controller use
    // [DisableRequestSizeLimit] so the Kestrel limit (from config) is authoritative.
    builder.WebHost.ConfigureKestrel((ctx, options) =>
    {
        var ingestionConfig = ctx.Configuration
            .GetSection(IngestionOptions.SectionName)
            .Get<IngestionOptions>() ?? new IngestionOptions();

        options.Limits.MaxRequestBodySize = ingestionConfig.MaxFileSizeBytes;
    });

    // ── Form pipeline: buffer large files to disk instead of memory ───────────
    // Files < MemoryBufferThresholdBytes stay in memory; larger files are spooled
    // to a temp file. The controller reads from the spool stream line-by-line
    // (streaming) — the full file is never loaded into a string or byte[].
    builder.Services.Configure<FormOptions>(formOptions =>
    {
        var ingestionConfig = builder.Configuration
            .GetSection(IngestionOptions.SectionName)
            .Get<IngestionOptions>() ?? new IngestionOptions();

        formOptions.MultipartBodyLengthLimit = ingestionConfig.MaxFileSizeBytes;
        formOptions.MemoryBufferThreshold = ingestionConfig.MemoryBufferThresholdBytes;
    });

    // ── Entra ID Authentication ───────────────────────────────────────────────
    // Validates incoming Bearer tokens issued by the Auth service.
    // The Auth service performs tenant mapping; this service reads the resulting
    // "tenant_id" claim to scope all uploaded data correctly.
    builder.Services
        .AddAuthentication()
        .AddMicrosoftIdentityWebApi(
            builder.Configuration.GetSection("AzureAd"));

    // ── File Storage ──────────────────────────────────────────────────────────
    // Provider selected from FileStorage:Provider config ("local" default).
    // Swap to "azureblob" in future without touching controller code.
    builder.Services.AddFileStorage(builder.Configuration);

    // ── FluentValidation ──────────────────────────────────────────────────────
    // Registers all validators in this assembly. FileUploadRequestValidator
    // is injected into IngestionController via IValidator<FileUploadRequest>.
    builder.Services.AddValidatorsFromAssemblyContaining<FileUploadRequestValidator>();

    // ── API Surface ───────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "EnterpriseLink Ingestion Service",
            Version = "v1",
            Description =
                "High-volume CSV ingestion API. Accepts streaming multipart uploads " +
                "and dispatches processing to the Worker service via RabbitMQ.",
        });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "Entra ID JWT obtained from POST /api/auth/token/exchange.",
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer",
                    },
                },
                Array.Empty<string>()
            },
        });

        var xmlFile = $"{typeof(Program).Assembly.GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
        if (File.Exists(xmlPath))
            c.IncludeXmlComments(xmlPath);
    });

    // ── Health Checks ─────────────────────────────────────────────────────────
    builder.Services.AddHealthChecks()
        .AddCheck("self", () => HealthCheckResult.Healthy("Ingestion service is running"));

    // ── Build & Pipeline ──────────────────────────────────────────────────────
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Ingestion Service v1"));
    }

    app.UseSerilogRequestLogging();

    // Authentication MUST come before Authorization.
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

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
