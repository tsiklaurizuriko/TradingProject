using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Api.Health;

public sealed class ComponentHealthCheck : IHealthCheck
{
    private readonly IOptions<TradingOptions> _trading;
    private readonly IExchangeEventSignal _events;

    public ComponentHealthCheck(IOptions<TradingOptions> trading, IExchangeEventSignal events)
    {
        _trading = trading;
        _events = events;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var stream = _events.Status;
        var data = new Dictionary<string, object>
        {
            ["marketDataEngine"] = "registered",
            ["botEngine"] = "registered",
            ["executionEngine"] = "registered",
            ["liveTradingEnabled"] = _trading.Value.VenueKind == Application.Trading.TradingVenueKind.Live && _trading.Value.LiveTradingEnabled,
            ["venue"] = _trading.Value.VenueKind.ToString(),
            ["entriesEnabled"] = _trading.Value.EntriesEnabled,
            ["userDataStreamConnected"] = stream.Connected,
            ["userDataStream"] = stream.Message
        };
        if (stream.LastEventAt is { } last)
        {
            data["userDataLastEventAt"] = last;
        }

        return Task.FromResult(HealthCheckResult.Healthy("Trading components are registered.", data));
    }
}
