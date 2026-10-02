using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Trading;

namespace TradingPlatform.Api.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController : ControllerBase
{
    private readonly HealthCheckService _healthCheckService;
    private readonly IOptions<TradingOptions> _trading;
    private readonly ReconciliationState _reconciliation;
    private readonly ILiveAccountCache _live;

    public SystemController(
        HealthCheckService healthCheckService,
        IOptions<TradingOptions> trading,
        ReconciliationState reconciliation,
        ILiveAccountCache live)
    {
        _healthCheckService = healthCheckService;
        _trading = trading;
        _reconciliation = reconciliation;
        _live = live;
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var report = await _healthCheckService.CheckHealthAsync(cancellationToken);
        var options = _trading.Value;
        var live = _live.Current;
        var maxAge = TimeSpan.FromSeconds(Math.Max(1, options.ReconciliationMaxAgeSeconds));
        var databaseReady = report.Entries.Values.All(entry => entry.Status != HealthStatus.Unhealthy);
        var exchangeReady = live.HasKeys && live.FuturesBookFresh;
        var reconciliationReady = _reconciliation.IsFresh(DateTimeOffset.UtcNow, maxAge);
        var riskValid = true;
        var gateOpen = options.LiveTradingEnabled
            && !options.KillSwitchEnabled
            && reconciliationReady
            && _reconciliation.BlockReason is null
            && exchangeReady;
        var blockedReason = gateOpen
            ? null
            : !options.LiveTradingEnabled
                ? LiveEntryGate.BlockedMessage
                : options.KillSwitchEnabled
                    ? "Kill switch is active."
                    : _reconciliation.BlockReason
                        ?? (reconciliationReady ? null : "Exchange reconciliation is missing or stale.");
        return Ok(new
        {
            status = report.Status.ToString(),
            applicationStarted = true,
            databaseReady,
            exchangeReady,
            reconciliationReady,
            riskConfigurationValid = riskValid,
            liveEntryGateOpen = gateOpen,
            unresolvedOrderCount = _reconciliation.UnresolvedOrderCount,
            blockedReason,
            liveTradingEnabled = options.LiveTradingEnabled,
            supportedMode = "Live",
            defaultMode = "Live",
            entries = report.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), e.Value.Description })
        });
    }
}
