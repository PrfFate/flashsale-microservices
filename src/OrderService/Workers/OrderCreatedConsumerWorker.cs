using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using OrderService.Options;
using OrderService.Services;
using Shared.Contracts.Events;

namespace OrderService.Workers;

public sealed class OrderCreatedConsumerWorker(
    ILogger<OrderCreatedConsumerWorker> logger,
    IConfiguration configuration,
    ConsumerOptions consumerOptions,
    OrderCreatedMessageProcessor processor) : BackgroundService
{
    private IConnection? _connection;
    private IModel? _channel;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbitMqConnectionString = configuration.GetConnectionString("RabbitMq")
            ?? throw new InvalidOperationException("ConnectionStrings:RabbitMq is required.");

        var factory = new ConnectionFactory
        {
            Uri = new Uri(rabbitMqConnectionString),
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();

        DeclareTopology(_channel, consumerOptions);
        _channel.BasicQos(0, consumerOptions.PrefetchCount, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (_, ea) =>
        {
            await HandleMessageAsync(ea, stoppingToken);
        };

        _channel.BasicConsume(
            queue: consumerOptions.QueueName,
            autoAck: false,
            consumer: consumer);

        logger.LogInformation("OrderCreated consumer started. Queue: {QueueName}", consumerOptions.QueueName);
        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _channel?.Close();
        _connection?.Close();
        _channel?.Dispose();
        _connection?.Dispose();

        return base.StopAsync(cancellationToken);
    }

    private async Task HandleMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        if (_channel is null)
        {
            return;
        }

        var messageId = ea.BasicProperties.MessageId;
        if (string.IsNullOrWhiteSpace(messageId))
        {
            messageId = ea.DeliveryTag.ToString();
        }

        try
        {
            var payload = Encoding.UTF8.GetString(ea.Body.ToArray());
            var integrationEvent = JsonSerializer.Deserialize<OrderCreatedEvent>(payload);

            if (integrationEvent is null)
            {
                throw new InvalidOperationException("OrderCreatedEvent payload is invalid.");
            }

            await processor.ProcessAsync(messageId, integrationEvent, cancellationToken);
            _channel.BasicAck(ea.DeliveryTag, false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OrderCreated processing failed. MessageId: {MessageId}", messageId);

            if (ea.Redelivered)
            {
                _channel.BasicReject(ea.DeliveryTag, false);
            }
            else
            {
                _channel.BasicNack(ea.DeliveryTag, false, true);
            }
        }
    }

    private static void DeclareTopology(IModel channel, ConsumerOptions options)
    {
        channel.ExchangeDeclare(options.ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
        channel.ExchangeDeclare(options.DeadLetterExchange, ExchangeType.Topic, durable: true, autoDelete: false);

        channel.QueueDeclare(
            queue: options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        channel.QueueBind(
            queue: options.DeadLetterQueue,
            exchange: options.DeadLetterExchange,
            routingKey: options.DeadLetterRoutingKey);

        var args = new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = options.DeadLetterExchange,
            ["x-dead-letter-routing-key"] = options.DeadLetterRoutingKey
        };

        channel.QueueDeclare(
            queue: options.QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args);

        channel.QueueBind(
            queue: options.QueueName,
            exchange: options.ExchangeName,
            routingKey: options.RoutingKey);
    }
}
