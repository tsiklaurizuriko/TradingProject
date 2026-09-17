using System.Diagnostics;

namespace TradingPlatform.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
        }

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        var accessor = context.RequestServices.GetService<TradingPlatform.Application.Abstractions.ICorrelationIdAccessor>();
        accessor?.Set(correlationId);

        using var scope = Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId);
        await _next(context);
    }
}
