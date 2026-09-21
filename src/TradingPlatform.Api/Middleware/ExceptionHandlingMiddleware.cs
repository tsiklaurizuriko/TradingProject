using System.Net;
using System.Net.Http;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.Errors;

namespace TradingPlatform.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            var details = ex.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => (object?)g.Select(e => e.ErrorMessage).ToArray());
            await WriteAsync(context, HttpStatusCode.BadRequest, ErrorCodes.ValidationFailed, "Validation failed.", details);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain error {ErrorCode}", ex.Code);
            await WriteAsync(context, MapStatus(ex.Code), ex.Code, ex.Message, ex.Details);
        }
        catch (OperationCanceledException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict");
            await WriteAsync(
                context,
                HttpStatusCode.Conflict,
                ErrorCodes.ReconciliationRequired,
                "This row was updated at the same time. Try Close again.",
                null);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Upstream request failed");
            await WriteAsync(
                context,
                HttpStatusCode.ServiceUnavailable,
                ErrorCodes.ExchangeUnavailable,
                "Market data is temporarily unavailable.",
                null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteAsync(
                context,
                HttpStatusCode.InternalServerError,
                ErrorCodes.InternalError,
                "An unexpected error occurred.",
                null);
        }
    }

    private static HttpStatusCode MapStatus(string code) => code switch
    {
        ErrorCodes.Unauthorized or "TWO_FACTOR_REQUIRED" => HttpStatusCode.Unauthorized,
        ErrorCodes.BotAlreadyRunning or ErrorCodes.DuplicateOrder or ErrorCodes.KillSwitchActive
            or ErrorCodes.ReconciliationRequired => HttpStatusCode.Conflict,
        ErrorCodes.RiskLimitExceeded or ErrorCodes.OrderRejected or ErrorCodes.InsufficientBalance
            or ErrorCodes.InvalidSymbol or ErrorCodes.InvalidQuantity or ErrorCodes.InvalidPrice
            or ErrorCodes.LiveTradingDisabled => HttpStatusCode.UnprocessableEntity,
        ErrorCodes.ExchangeUnavailable or ErrorCodes.ExchangeRateLimit => HttpStatusCode.ServiceUnavailable,
        _ => HttpStatusCode.BadRequest
    };

    private static async Task WriteAsync(
        HttpContext context,
        HttpStatusCode status,
        string code,
        string message,
        IReadOnlyDictionary<string, object?>? details)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        var payload = new
        {
            code,
            message,
            traceId = context.TraceIdentifier,
            details
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
