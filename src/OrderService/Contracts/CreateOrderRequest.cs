namespace OrderService.Contracts;

public sealed class CreateOrderRequest
{
    public required Guid ProductId { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
}
