namespace OrderService.Contracts;

public sealed class CreateOrderResponse
{
    public required Guid OrderId { get; init; }
    public required string Status { get; init; }
    public required Guid CorrelationId { get; init; }
}
