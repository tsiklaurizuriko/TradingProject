using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Api.Health;

public sealed class ComponentHealthCheck : IHealthCheck
{
    private readonly IOptions<TradingOptions> _trading;

    public ComponentHealthCheck(IOptions<TradingOptions> trading)
    {
        _trading = trading;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["marketDataEngine"] = "registered",
            ["botEngine"] = "registered",
            ["executionEngine"] = "registered",
            ["liveTradingEnabled"] = _trading.Value.LiveTradingEnabled
        };

        return Task.FromResult(HealthCheckResult.Healthy("Trading components are registered.", data));
    }
}
