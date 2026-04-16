namespace OrderService.Contracts;

public sealed class UpsertInventoryRequest
{
    public required Guid ProductId { get; init; }
    public required int AvailableQuantity { get; init; }
}
