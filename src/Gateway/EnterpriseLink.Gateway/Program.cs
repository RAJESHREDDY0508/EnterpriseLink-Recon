using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Forwarded headers (Gateway sits behind a load balancer in production) ────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Trust all proxies in containerised environments; lock down in production.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ── Rate limiting ─────────────────────────────────────────────────────────────
var rateLimitCfg = builder.Configuration.GetSection("RateLimiting");
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("gateway-global", opt =>
    {
        opt.Window = TimeSpan.FromSeconds(rateLimitCfg.GetValue("GlobalWindowSeconds", 60));
        opt.PermitLimit = rateLimitCfg.GetValue("GlobalPermitLimit", 1000);
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Health checks ─────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── YARP reverse proxy ────────────────────────────────────────────────────────
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();

// Security headers applied to every response
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

app.UseForwardedHeaders();
app.UseRateLimiter();

app.MapHealthChecks("/health");
app.MapReverseProxy().RequireRateLimiting("gateway-global");

app.Run();
