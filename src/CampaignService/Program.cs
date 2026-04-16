using CampaignService.Options;
using CampaignService.Repositories;
using CampaignService.Services;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.Configure<CampaignDatabaseOptions>(
    builder.Configuration.GetSection(CampaignDatabaseOptions.SectionName));
builder.Services.Configure<CacheOptions>(
    builder.Configuration.GetSection(CacheOptions.SectionName));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var connectionString = builder.Configuration.GetConnectionString("Mongo")
        ?? throw new InvalidOperationException("ConnectionStrings:Mongo is required.");

    return new MongoClient(connectionString);
});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("ConnectionStrings:Redis is required.");
    options.InstanceName = "campaign-service:";
});

builder.Services.AddScoped<ICampaignReadRepository, MongoCampaignReadRepository>();
builder.Services.AddScoped<ICampaignQueryService, CachedCampaignQueryService>();

var mongoConnectionString = builder.Configuration.GetConnectionString("Mongo");
var redisConnectionString = builder.Configuration.GetConnectionString("Redis");
var mongoDatabaseName = builder.Configuration.GetSection(CampaignDatabaseOptions.SectionName)["DatabaseName"];

builder.Services
    .AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["ready"])
    .AddCheck("mongo-config", () =>
    {
        return string.IsNullOrWhiteSpace(mongoConnectionString) || string.IsNullOrWhiteSpace(mongoDatabaseName)
            ? HealthCheckResult.Unhealthy("Mongo configuration missing")
            : HealthCheckResult.Healthy();
    }, tags: ["ready"])
    .AddCheck("redis-config", _ =>
    {
        return string.IsNullOrWhiteSpace(redisConnectionString)
            ? HealthCheckResult.Unhealthy("ConnectionStrings:Redis missing")
            : HealthCheckResult.Healthy();
    }, tags: ["ready"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { service = "campaign-service", status = "ok" }));

app.MapGet("/api/campaigns", async (ICampaignQueryService queryService, CancellationToken cancellationToken) =>
{
    var campaigns = await queryService.GetCampaignsAsync(cancellationToken);
    return Results.Ok(campaigns);
});

app.MapGet("/api/campaigns/{id:guid}", async (Guid id, ICampaignQueryService queryService, CancellationToken cancellationToken) =>
{
    var campaign = await queryService.GetCampaignByIdAsync(id, cancellationToken);
    return campaign is null ? Results.NotFound() : Results.Ok(campaign);
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();
