using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shared.BuildingBlocks.Errors;

namespace Shared.BuildingBlocks.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        logger.LogError(exception, "Unhandled exception captured by global middleware.");

        var (statusCode, title, code, errors) = exception switch
        {
            ValidationException validationException => (
                StatusCodes.Status400BadRequest,
                "Validation failed",
                ErrorCodes.Validation,
                validationException.Errors.Select(x => new { field = x.PropertyName, message = x.ErrorMessage })),
            ArgumentException => (
                StatusCodes.Status400BadRequest,
                "Invalid request",
                ErrorCodes.BadRequest,
                Enumerable.Empty<object>()),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                "Resource not found",
                ErrorCodes.NotFound,
                Enumerable.Empty<object>()),
            InvalidOperationException => (
                StatusCodes.Status409Conflict,
                "Operation conflict",
                ErrorCodes.Conflict,
                Enumerable.Empty<object>()),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal server error",
                ErrorCodes.Unhandled,
                Enumerable.Empty<object>())
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = new
        {
            type = $"https://httpstatuses.io/{statusCode}",
            title,
            status = statusCode,
            code,
            traceId = context.TraceIdentifier,
            errors
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
