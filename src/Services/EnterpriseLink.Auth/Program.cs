using EnterpriseLink.Auth.Configuration;
using EnterpriseLink.Auth.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text.Json;

const string ServiceName = "EnterpriseLink.Auth";

// ── Bootstrap logger ─────────────────────────────────────────────────────────
// Captures startup errors before the full logging pipeline is ready.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Starting {ServiceName}", ServiceName);

    var builder = WebApplication.CreateBuilder(args);

    // ── Structured Logging (Serilog) ──────────────────────────────────────────
    // Reads Serilog config from appsettings.json; enriches every event with service name.
    builder.Host.UseSerilog((ctx, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", ServiceName)
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{Service}] {Message:lj}{NewLine}{Exception}"));

    // ── Entra ID Authentication ───────────────────────────────────────────────
    // Microsoft.Identity.Web validates incoming JWTs against the Entra JWKS endpoint.
    // Validates: signature, issuer, audience, expiry.
    // TenantId = "common" allows tokens from any registered Entra directory.
    builder.Services
        .AddAuthentication()
        .AddMicrosoftIdentityWebApi(
            builder.Configuration.GetSection(EntraIdOptions.SectionName));

    // Strongly-typed Entra ID config with startup validation
    builder.Services
        .AddOptions<EntraIdOptions>()
        .Bind(builder.Configuration.GetSection(EntraIdOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ── Tenant Mapping ────────────────────────────────────────────────────────
    // Singleton: maps Entra tid → internal TenantId. Immutable after startup.
    builder.Services
        .AddSingleton<ITenantMappingService, ConfigurationTenantMappingService>();

    // ── API Surface ───────────────────────────────────────────────────────────
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();

    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "EnterpriseLink Auth Service",
            Version = "v1",
            Description =
                "Entra ID token validation and tenant identity resolution. " +
                "All endpoints require a valid Entra ID Bearer token.",
        });

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "Entra ID JWT from https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
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
        .AddCheck("self", () => HealthCheckResult.Healthy("Auth service is running"));

    // ── Build & Pipeline ──────────────────────────────────────────────────────
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth Service v1"));
    }

    app.UseSerilogRequestLogging();

    // Authentication MUST come before Authorization.
    // Microsoft.Identity.Web populates HttpContext.User from the Bearer token here.
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
