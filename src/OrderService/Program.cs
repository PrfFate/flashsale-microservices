using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OrderService.Contracts;
using OrderService.Infrastructure;
using OrderService.Options;
using OrderService.Services;
using OrderService.Workers;

var builder = WebApplication.CreateBuilder(args);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

var consumerOptions = builder.Configuration
    .GetSection(ConsumerOptions.SectionName)
    .Get<ConsumerOptions>()
    ?? new ConsumerOptions();

builder.Services.AddSingleton(consumerOptions);
builder.Services.AddSingleton<IOrderCommandService>(_ => new OrderCommandService(postgresConnectionString));
builder.Services.AddSingleton(_ => new InventoryBootstrapService(postgresConnectionString));
builder.Services.AddSingleton(_ => new OrderCreatedMessageProcessor(postgresConnectionString, consumerOptions));
builder.Services.AddHostedService<OrderCreatedConsumerWorker>();

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

await DatabaseInitializer.InitializeAsync(postgresConnectionString, app.Lifetime.ApplicationStopping);

app.MapGet("/", () => Results.Ok(new { service = "order-service", status = "ok" }));

app.MapPost("/api/orders", async (CreateOrderRequest request, IOrderCommandService orderCommandService, CancellationToken cancellationToken) =>
{
    var result = await orderCommandService.CreateAsync(request, cancellationToken);

    if (!result.Success)
    {
        return Results.Conflict(new { error = result.Error });
    }

    return Results.Accepted($"/api/orders/{result.Response!.OrderId}", result.Response);
});

app.MapPost("/api/inventory", async (UpsertInventoryRequest request, InventoryBootstrapService inventoryBootstrapService, CancellationToken cancellationToken) =>
{
    if (request.AvailableQuantity < 0)
    {
        return Results.BadRequest(new { error = "AvailableQuantity cannot be negative." });
    }

    await inventoryBootstrapService.UpsertAsync(request, cancellationToken);
    return Results.Ok();
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
