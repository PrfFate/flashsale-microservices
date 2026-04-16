namespace OrderService.Options;

public sealed class ConsumerOptions
{
    public const string SectionName = "Consumer";

    public string ExchangeName { get; init; } = "orders.events";
    public string QueueName { get; init; } = "order.created.queue";
    public string RoutingKey { get; init; } = "order.created";
    public string DeadLetterExchange { get; init; } = "orders.events.dlx";
    public string DeadLetterQueue { get; init; } = "order.created.dlq";
    public string DeadLetterRoutingKey { get; init; } = "order.created.dlq";
    public ushort PrefetchCount { get; init; } = 20;
    public string ConsumerName { get; init; } = "order-created-consumer";
}
