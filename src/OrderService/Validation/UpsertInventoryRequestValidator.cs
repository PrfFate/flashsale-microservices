using FluentValidation;
using OrderService.Contracts;

namespace OrderService.Validation;

public sealed class UpsertInventoryRequestValidator : AbstractValidator<UpsertInventoryRequest>
{
    public UpsertInventoryRequestValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty();

        RuleFor(x => x.AvailableQuantity)
            .GreaterThanOrEqualTo(0);
    }
}
