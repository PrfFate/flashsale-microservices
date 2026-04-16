namespace Shared.Contracts.Events;

public sealed record OrderCreatedEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
}
