using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradingPlatform.Api.Health;

public sealed class ComponentHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>
        {
            ["marketDataEngine"] = "registered",
            ["botEngine"] = "registered",
            ["executionEngine"] = "registered",
            ["liveTradingEnabled"] = false
        };

        return Task.FromResult(HealthCheckResult.Healthy("Trading components are registered.", data));
    }
}
