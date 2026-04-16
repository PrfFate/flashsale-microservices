using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RelayWorker;
using RelayWorker.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RelayOptions>(
    builder.Configuration.GetSection(RelayOptions.SectionName));

var relayOptions = builder.Configuration
    .GetSection(RelayOptions.SectionName)
    .Get<RelayOptions>()
    ?? new RelayOptions();

builder.Services.AddSingleton(relayOptions);
builder.Services.AddHostedService<Worker>();

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
var rabbitMqConnectionString = builder.Configuration.GetConnectionString("RabbitMq");

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"])
    .AddCheck("postgres-config", () =>
    {
        return string.IsNullOrWhiteSpace(postgresConnectionString)
            ? HealthCheckResult.Unhealthy("ConnectionStrings:Postgres missing")
            : HealthCheckResult.Healthy();
    }, tags: ["ready"])
    .AddCheck("rabbit-config", () =>
    {
        return string.IsNullOrWhiteSpace(rabbitMqConnectionString)
            ? HealthCheckResult.Unhealthy("ConnectionStrings:RabbitMq missing")
            : HealthCheckResult.Healthy();
    }, tags: ["ready"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "relay-worker", status = "ok" }));
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
