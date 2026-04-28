using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Shared.BuildingBlocks.Middleware;

namespace Shared.BuildingBlocks.Extensions;

public static class CorporateApiExtensions
{
    public static IServiceCollection AddCorporateApiFoundation(this IServiceCollection services)
    {
        services.AddProblemDetails();
        return services;
    }

    public static IServiceCollection AddCorporateValidation(this IServiceCollection services, params Type[] markerTypes)
    {
        foreach (var markerType in markerTypes)
        {
            services.AddValidatorsFromAssemblyContaining(markerType);
        }

        return services;
    }

    public static IApplicationBuilder UseCorporateApiFoundation(this IApplicationBuilder app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        return app;
    }
}
