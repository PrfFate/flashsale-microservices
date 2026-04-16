namespace Shared.Contracts.Events;

public sealed record OrderCompletedEvent : IntegrationEvent
{
    public required Guid OrderId { get; init; }
    public DateTime CompletedOnUtc { get; init; } = DateTime.UtcNow;
}
