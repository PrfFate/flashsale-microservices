namespace Shared.Contracts.Events;

public sealed record OrderRejectedEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public required string Reason { get; init; }
}
