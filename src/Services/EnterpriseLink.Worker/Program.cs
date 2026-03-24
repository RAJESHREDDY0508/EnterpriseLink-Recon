using EnterpriseLink.Worker;

// Worker uses WebApplication so it can expose /health over HTTP
// while the BackgroundService runs the actual processing loop.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHostedService<Worker>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
