using OrderService.Contracts;
using OrderService.Domain;

namespace OrderService.Mapping;

public static class OrderResponseMapper
{
    public static CreateOrderResponse ToPendingResponse(Guid orderId, Guid correlationId)
    {
        return new CreateOrderResponse
        {
            OrderId = orderId,
            Status = OrderStatus.Pending,
            CorrelationId = correlationId
        };
    }
}
