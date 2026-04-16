using System.Data;
using System.Text.Json;
using Npgsql;
using OrderService.Contracts;
using OrderService.Domain;
using Shared.Contracts.Events;

namespace OrderService.Services;

public sealed class OrderCommandService(string connectionString) : IOrderCommandService
{
    public async Task<(bool Success, string? Error, CreateOrderResponse? Response)> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return (false, "Quantity must be greater than zero.", null);
        }

        if (request.UnitPrice < 0)
        {
            return (false, "UnitPrice cannot be negative.", null);
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var hasStock = await HasSufficientStockAsync(connection, transaction, request.ProductId, request.Quantity, cancellationToken);
        if (!hasStock)
        {
            await transaction.RollbackAsync(cancellationToken);
            return (false, "Insufficient stock.", null);
        }

        var orderId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await InsertOrderPendingAsync(connection, transaction, orderId, correlationId, request, now, cancellationToken);

        var integrationEvent = new OrderCreatedEvent
        {
            OrderId = orderId,
            ProductId = request.ProductId,
            Quantity = request.Quantity,
            UnitPrice = request.UnitPrice,
            CorrelationId = correlationId,
            OccurredOnUtc = now
        };

        await InsertOutboxAsync(connection, transaction, integrationEvent, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return (true, null, new CreateOrderResponse
        {
            OrderId = orderId,
            Status = OrderStatus.Pending,
            CorrelationId = correlationId
        });
    }

    private static async Task<bool> HasSufficientStockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT available_quantity
            FROM inventory
            WHERE product_id = @product_id
            FOR SHARE;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("product_id", productId);

        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        if (scalar is null || scalar == DBNull.Value)
        {
            return false;
        }

        var available = Convert.ToInt32(scalar);
        return available >= quantity;
    }

    private static async Task InsertOrderPendingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid orderId,
        Guid correlationId,
        CreateOrderRequest request,
        DateTime now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO orders (
                id,
                product_id,
                quantity,
                unit_price,
                status,
                correlation_id,
                created_on_utc,
                updated_on_utc
            )
            VALUES (
                @id,
                @product_id,
                @quantity,
                @unit_price,
                @status,
                @correlation_id,
                @created_on_utc,
                @updated_on_utc
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", orderId);
        command.Parameters.AddWithValue("product_id", request.ProductId);
        command.Parameters.AddWithValue("quantity", request.Quantity);
        command.Parameters.AddWithValue("unit_price", request.UnitPrice);
        command.Parameters.AddWithValue("status", OrderStatus.Pending);
        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("created_on_utc", now);
        command.Parameters.AddWithValue("updated_on_utc", now);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOutboxAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        OrderCreatedEvent integrationEvent,
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
        command.Parameters.AddWithValue("event_type", nameof(OrderCreatedEvent));
        command.Parameters.AddWithValue("payload", JsonSerializer.Serialize(integrationEvent));
        command.Parameters.AddWithValue("correlation_id", integrationEvent.CorrelationId);
        command.Parameters.AddWithValue("occurred_on_utc", integrationEvent.OccurredOnUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
