using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Npgsql;
using OrderService.Contracts;
using OrderService.Domain;
using OrderService.Infrastructure;
using OrderService.Options;
using OrderService.Services;
using Shared.Contracts.Events;
using Testcontainers.PostgreSql;

namespace ArchitectureProof.Tests.Integration;

public sealed class OrderFlowIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase("ordersdb")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _connectionString = _postgres.GetConnectionString();
        await DatabaseInitializer.InitializeAsync(_connectionString, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task RaceCondition_500ParallelRequests_CompletesOnlyStockLimit()
    {
        await ResetTablesAsync();

        var productId = Guid.NewGuid();
        var stockLimit = 100;
        var requestCount = 500;

        var inventoryService = new InventoryBootstrapService(_connectionString);
        await inventoryService.UpsertAsync(new UpsertInventoryRequest
        {
            ProductId = productId,
            AvailableQuantity = stockLimit
        }, CancellationToken.None);

        var orderCommandService = new OrderCommandService(_connectionString);
        var createTasks = Enumerable.Range(0, requestCount)
            .Select(_ => orderCommandService.CreateAsync(new CreateOrderRequest
            {
                ProductId = productId,
                Quantity = 1,
                UnitPrice = 199.99m
            }, CancellationToken.None));

        await Task.WhenAll(createTasks);

        var createdEvents = await GetOutboxEventsAsync<OrderCreatedEvent>("OrderCreatedEvent");
        Assert.Equal(requestCount, createdEvents.Count);

        var processor = new OrderCreatedMessageProcessor(_connectionString, new ConsumerOptions());
        var processTasks = createdEvents
            .Select((evt, index) => processor.ProcessAsync($"race-msg-{index}", evt, CancellationToken.None));

        await Task.WhenAll(processTasks);

        var completedCount = await GetOrderCountByStatusAsync(OrderStatus.Completed);
        var rejectedCount = await GetOrderCountByStatusAsync(OrderStatus.Rejected);
        var finalStock = await GetInventoryQuantityAsync(productId);

        Assert.Equal(stockLimit, completedCount);
        Assert.Equal(requestCount - stockLimit, rejectedCount);
        Assert.Equal(0, finalStock);
    }

    [Fact]
    public async Task Idempotency_SameMessageProcessedTwice_AppliesOnlyOnce()
    {
        await ResetTablesAsync();

        var productId = Guid.NewGuid();

        var inventoryService = new InventoryBootstrapService(_connectionString);
        await inventoryService.UpsertAsync(new UpsertInventoryRequest
        {
            ProductId = productId,
            AvailableQuantity = 5
        }, CancellationToken.None);

        var orderCommandService = new OrderCommandService(_connectionString);
        var createResult = await orderCommandService.CreateAsync(new CreateOrderRequest
        {
            ProductId = productId,
            Quantity = 1,
            UnitPrice = 50m
        }, CancellationToken.None);

        Assert.True(createResult.Success);
        Assert.NotNull(createResult.Response);

        var createdEvent = (await GetOutboxEventsAsync<OrderCreatedEvent>("OrderCreatedEvent")).Single();

        var processor = new OrderCreatedMessageProcessor(_connectionString, new ConsumerOptions());
        const string messageId = "idempotency-same-message";

        await processor.ProcessAsync(messageId, createdEvent, CancellationToken.None);
        await processor.ProcessAsync(messageId, createdEvent, CancellationToken.None);

        var processedCount = await GetProcessedMessageCountAsync(messageId, "order-created-consumer");
        var finalStock = await GetInventoryQuantityAsync(productId);
        var orderStatus = await GetOrderStatusAsync(createResult.Response!.OrderId);

        Assert.Equal(1, processedCount);
        Assert.Equal(4, finalStock);
        Assert.Equal(OrderStatus.Completed, orderStatus);
    }

    private async Task ResetTablesAsync()
    {
        const string sql = """
            DELETE FROM processed_messages;
            DELETE FROM outbox_messages;
            DELETE FROM orders;
            DELETE FROM inventory;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<TEvent>> GetOutboxEventsAsync<TEvent>(string eventType)
    {
        const string sql = """
            SELECT payload::text
            FROM outbox_messages
            WHERE event_type = @event_type
            ORDER BY occurred_on_utc;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("event_type", eventType);

        var events = new List<TEvent>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var payload = reader.GetString(0);
            var evt = JsonSerializer.Deserialize<TEvent>(payload);
            if (evt is not null)
            {
                events.Add(evt);
            }
        }

        return events;
    }

    private async Task<int> GetOrderCountByStatusAsync(string status)
    {
        const string sql = "SELECT COUNT(*) FROM orders WHERE status = @status;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("status", status);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<int> GetInventoryQuantityAsync(Guid productId)
    {
        const string sql = "SELECT available_quantity FROM inventory WHERE product_id = @product_id;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("product_id", productId);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<int> GetProcessedMessageCountAsync(string messageId, string consumerName)
    {
        const string sql = """
            SELECT COUNT(*)
            FROM processed_messages
            WHERE message_id = @message_id
              AND consumer_name = @consumer_name;
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("consumer_name", consumerName);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task<string> GetOrderStatusAsync(Guid orderId)
    {
        const string sql = "SELECT status FROM orders WHERE id = @id;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", orderId);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToString(result)!;
    }
}
