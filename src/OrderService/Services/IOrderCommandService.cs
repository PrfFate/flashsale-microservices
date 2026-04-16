using OrderService.Contracts;

namespace OrderService.Services;

public interface IOrderCommandService
{
    Task<(bool Success, string? Error, CreateOrderResponse? Response)> CreateAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken = default);
}
