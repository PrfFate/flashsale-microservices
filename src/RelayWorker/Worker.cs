using System.Text;
using Npgsql;
using RabbitMQ.Client;
using RelayWorker.Models;
using RelayWorker.Options;

namespace RelayWorker;

public sealed class Worker(
    ILogger<Worker> logger,
    IConfiguration configuration,
    RelayOptions relayOptions) : BackgroundService
{
    private readonly string _postgresConnectionString =
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");

    private readonly string _rabbitMqConnectionString =
        configuration.GetConnectionString("RabbitMq")
        ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is required.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Relay worker started. Poll interval: {IntervalSeconds}s, batch size: {BatchSize}",
            relayOptions.PollIntervalSeconds,
            relayOptions.BatchSize);

        var delay = TimeSpan.FromSeconds(Math.Max(1, relayOptions.PollIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RelayOutboxBatchAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox relay cycle failed.");
            }

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task RelayOutboxBatchAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_postgresConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var batch = await ReadPendingOutboxMessagesAsync(connection, transaction, relayOptions.BatchSize, cancellationToken);

        if (batch.Count == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var factory = new ConnectionFactory
        {
            Uri = new Uri(_rabbitMqConnectionString),
            DispatchConsumersAsync = true
        };

        using var mqConnection = factory.CreateConnection();
        using var channel = mqConnection.CreateModel();

        channel.ExchangeDeclare(
            exchange: relayOptions.ExchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null);

        foreach (var message in batch)
        {
            try
            {
                Publish(channel, message, relayOptions.ExchangeName);
                await MarkAsProcessedAsync(connection, transaction, message.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to publish outbox message {MessageId}", message.Id);
                await MarkAsFailedAsync(connection, transaction, message.Id, ex.Message, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static void Publish(IModel channel, OutboxMessageRecord message, string exchangeName)
    {
        var props = channel.CreateBasicProperties();
        props.Persistent = true;
        props.MessageId = message.Id.ToString();
        props.CorrelationId = message.CorrelationId.ToString();
        props.Type = message.EventType;
        props.Timestamp = new AmqpTimestamp(new DateTimeOffset(message.OccurredOnUtc).ToUnixTimeSeconds());

        var body = Encoding.UTF8.GetBytes(message.Payload);

        channel.BasicPublish(
            exchange: exchangeName,
            routingKey: ToRoutingKey(message.EventType),
            mandatory: false,
            basicProperties: props,
            body: body);
    }

    private static string ToRoutingKey(string eventType)
    {
        return eventType switch
        {
            "OrderCreatedEvent" => "order.created",
            "OrderCompletedEvent" => "order.completed",
            "OrderRejectedEvent" => "order.rejected",
            _ => "order.unknown"
        };
    }

    private static async Task<List<OutboxMessageRecord>> ReadPendingOutboxMessagesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        int batchSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT id, event_type, payload::text, correlation_id, occurred_on_utc
            FROM outbox_messages
            WHERE processed_at_utc IS NULL
            ORDER BY occurred_on_utc
            FOR UPDATE SKIP LOCKED
            LIMIT @batch_size;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("batch_size", Math.Max(1, batchSize));

        var messages = new List<OutboxMessageRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(new OutboxMessageRecord
            {
                Id = reader.GetGuid(0),
                EventType = reader.GetString(1),
                Payload = reader.GetString(2),
                CorrelationId = reader.GetGuid(3),
                OccurredOnUtc = reader.GetDateTime(4)
            });
        }

        return messages;
    }

    private static async Task MarkAsProcessedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_messages
            SET processed_at_utc = @processed_at_utc,
                last_error = NULL
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("processed_at_utc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkAsFailedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid messageId,
        string error,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE outbox_messages
            SET error_count = error_count + 1,
                last_error = @last_error
            WHERE id = @id;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", messageId);
        command.Parameters.AddWithValue("last_error", Truncate(error, 2000));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}
