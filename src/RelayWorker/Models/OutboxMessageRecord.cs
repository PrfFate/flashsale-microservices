namespace RelayWorker.Models;

public sealed class OutboxMessageRecord
{
    public required Guid Id { get; init; }
    public required string EventType { get; init; }
    public required string Payload { get; init; }
    public required Guid CorrelationId { get; init; }
    public required DateTime OccurredOnUtc { get; init; }
}
