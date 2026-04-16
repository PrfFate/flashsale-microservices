using System.Text.Json;
using Npgsql;
using OrderService.Domain;
using OrderService.Options;
using Shared.Contracts.Events;

namespace OrderService.Services;

public sealed class OrderCreatedMessageProcessor(
    string postgresConnectionString,
    ConsumerOptions consumerOptions)
{
    public async Task ProcessAsync(string messageId, OrderCreatedEvent integrationEvent, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (await IsAlreadyProcessedAsync(connection, transaction, messageId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var now = DateTime.UtcNow;

        var orderExists = await OrderExistsAsync(connection, transaction, integrationEvent.OrderId, cancellationToken);
        if (!orderExists)
        {
            await InsertProcessedMessageAsync(connection, transaction, messageId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var inventory = await GetInventoryAsync(connection, transaction, integrationEvent.ProductId, cancellationToken);

        if (inventory is null || inventory.Value.AvailableQuantity < integrationEvent.Quantity)
        {
            await RejectOrderAsync(connection, transaction, integrationEvent.OrderId, now, cancellationToken);
            await InsertOutboxEventAsync(
                connection,
                transaction,
                new OrderRejectedEvent
                {
                    OrderId = integrationEvent.OrderId,
                    Reason = "Insufficient stock",
                    CorrelationId = integrationEvent.CorrelationId,
                    OccurredOnUtc = now
                },
                cancellationToken);

            await InsertProcessedMessageAsync(connection, transaction, messageId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var stockUpdated = await TryDecreaseStockAsync(
            connection,
            transaction,
            integrationEvent.ProductId,
            integrationEvent.Quantity,
            inventory.Value.Version,
            now,
            cancellationToken);

        if (!stockUpdated)
        {
            await RejectOrderAsync(connection, transaction, integrationEvent.OrderId, now, cancellationToken);
            await InsertOutboxEventAsync(
                connection,
                transaction,
                new OrderRejectedEvent
                {
                    OrderId = integrationEvent.OrderId,
                    Reason = "Inventory optimistic concurrency conflict",
                    CorrelationId = integrationEvent.CorrelationId,
                    OccurredOnUtc = now
                },
                cancellationToken);

            await InsertProcessedMessageAsync(connection, transaction, messageId, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await CompleteOrderAsync(connection, transaction, integrationEvent.OrderId, now, cancellationToken);

        await InsertOutboxEventAsync(
            connection,
            transaction,
            new OrderCompletedEvent
            {
                OrderId = integrationEvent.OrderId,
                CorrelationId = integrationEvent.CorrelationId,
                OccurredOnUtc = now,
                CompletedOnUtc = now
            },
            cancellationToken);

        await InsertProcessedMessageAsync(connection, transaction, messageId, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task<bool> IsAlreadyProcessedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string messageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM processed_messages
            WHERE message_id = @message_id
              AND consumer_name = @consumer_name;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("consumer_name", consumerOptions.ConsumerName);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null;
    }

    private static async Task<bool> OrderExistsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT 1
            FROM orders
            WHERE id = @id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", orderId);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is not null;
    }

    private static async Task<(int AvailableQuantity, long Version)?> GetInventoryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT available_quantity, version
            FROM inventory
            WHERE product_id = @product_id
            FOR UPDATE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var availableQuantity = reader.GetInt32(0);
        var version = reader.GetInt64(1);
        return (availableQuantity, version);
    }

    private static async Task<bool> TryDecreaseStockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid productId,
        int quantity,
        long expectedVersion,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE inventory
            SET available_quantity = available_quantity - @quantity,
                version = version + 1,
                updated_on_utc = @updated_on_utc
            WHERE product_id = @product_id
              AND version = @expected_version
              AND available_quantity >= @quantity;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("quantity", quantity);
        command.Parameters.AddWithValue("updated_on_utc", now);
        command.Parameters.AddWithValue("product_id", productId);
        command.Parameters.AddWithValue("expected_version", expectedVersion);

        var rows = await command.ExecuteNonQueryAsync(cancellationToken);
        return rows == 1;
    }

    private static async Task CompleteOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await UpdateOrderStatusAsync(connection, transaction, orderId, OrderStatus.Completed, now, cancellationToken);
    }

    private static async Task RejectOrderAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await UpdateOrderStatusAsync(connection, transaction, orderId, OrderStatus.Rejected, now, cancellationToken);
    }

    private static async Task UpdateOrderStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        string status,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE orders
            SET status = @status,
                updated_on_utc = @updated_on_utc
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", orderId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("updated_on_utc", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task InsertProcessedMessageAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string messageId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO processed_messages (message_id, consumer_name, processed_on_utc)
            VALUES (@message_id, @consumer_name, @processed_on_utc);
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("message_id", messageId);
        command.Parameters.AddWithValue("consumer_name", consumerOptions.ConsumerName);
        command.Parameters.AddWithValue("processed_on_utc", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOutboxEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO outbox_messages (
                id,
                event_type,
                payload,
                correlation_id,
                occurred_on_utc,
                processed_at_utc,
                error_count,
                last_error
            )
            VALUES (
                @id,
                @event_type,
                @payload::jsonb,
                @correlation_id,
                @occurred_on_utc,
                NULL,
                0,
                NULL
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", integrationEvent.EventId);
        command.Parameters.AddWithValue("event_type", integrationEvent.GetType().Name);
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType()));
        command.Parameters.AddWithValue("correlation_id", integrationEvent.CorrelationId);
        command.Parameters.AddWithValue("occurred_on_utc", integrationEvent.OccurredOnUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
