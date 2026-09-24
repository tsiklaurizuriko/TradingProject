using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingPlatform.Application.Abstractions.MarketData;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Trading;

namespace TradingPlatform.Api.Controllers;

[ApiController]
[Route("api/trading")]
[AllowAnonymous]
public sealed class TradingController : ControllerBase
{
    private readonly ITradingQueryService _query;
    private readonly IBotLifecycleService _lifecycle;
    private readonly IBotEngine _engine;
    private readonly IPublicMarketDataClient _market;
    private readonly IMarketScanner _scanner;
    private readonly ITradeEligibility _eligibility;
    private readonly IBacktestService _backtests;
    private readonly IScalpingResearchQuery _scalping;
    private readonly IPriceActionResearchQuery _priceAction;

    public TradingController(
        ITradingQueryService query,
        IBotLifecycleService lifecycle,
        IBotEngine engine,
        IPublicMarketDataClient market,
        IMarketScanner scanner,
        ITradeEligibility eligibility,
        IBacktestService backtests,
        IScalpingResearchQuery scalping,
        IPriceActionResearchQuery priceAction)
    {
        _query = query;
        _lifecycle = lifecycle;
        _engine = engine;
        _market = market;
        _scanner = scanner;
        _eligibility = eligibility;
        _backtests = backtests;
        _scalping = scalping;
        _priceAction = priceAction;
    }

    [HttpGet("overview")]
    public Task<PortfolioDto> Overview(CancellationToken cancellationToken) =>
        _query.GetOverviewAsync(cancellationToken);

    [HttpGet("bots")]
    public Task<IReadOnlyList<BotDto>> Bots(CancellationToken cancellationToken) =>
        _query.GetBotsAsync(cancellationToken);

    [HttpGet("orders")]
    public Task<IReadOnlyList<OrderDto>> Orders(CancellationToken cancellationToken) =>
        _query.GetOrdersAsync(cancellationToken);

    [HttpGet("positions")]
    public Task<IReadOnlyList<PositionDto>> Positions(CancellationToken cancellationToken) =>
        _query.GetPositionsAsync(cancellationToken);

    [HttpGet("trades")]
    public Task<IReadOnlyList<TradeDto>> Trades(CancellationToken cancellationToken) =>
        _query.GetTradesAsync(cancellationToken);

    [HttpGet("performance")]
    public Task<PerformanceDto> Performance([FromQuery] string mode = "Paper", CancellationToken cancellationToken = default) =>
        _query.GetPerformanceAsync(mode, cancellationToken);

    [HttpGet("risk-profile")]
    public Task<RiskProfileDto> RiskProfile([FromQuery] string? mode, CancellationToken cancellationToken) =>
        _query.GetRiskProfileAsync(mode, cancellationToken);

    [HttpGet("markets")]
    public async Task<ActionResult<IReadOnlyList<MarketQuoteDto>>> Markets(CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketScanRow> scan;
        try
        {
            scan = await _scanner.ScanAsync(cancellationToken);
        }
        catch
        {
            scan = [];
        }

        if (scan.Count == 0)
        {
            var universe = await _market.GetPaperUniverseAsync(cancellationToken);
            var quotes = universe
                .Select((s, index) =>
                {
                    var decision = _eligibility.Evaluate(s);
                    return new MarketQuoteDto(
                        s.Symbol,
                        UsdtSpotUniverse.DisplayNameOf(s.Symbol),
                        index + 1,
                        s.LastPrice,
                        s.PriceChangePercent,
                        s.QuoteVolume,
                        DateTimeOffset.UtcNow,
                        s.HighPrice,
                        s.LowPrice,
                        s.Trades24h,
                        0,
                        0,
                        0,
                        0,
                        0,
                        decision.Eligible,
                        decision.Reason,
                        decision.Watchable);
                })
                .ToList();
            return Ok(quotes);
        }

        return Ok(scan.Select(row =>
        {
            var decision = _eligibility.Evaluate(row);
            return new MarketQuoteDto(
                row.Contract.Symbol,
                UsdtSpotUniverse.DisplayNameOf(row.Contract.Symbol),
                row.ScanRank,
                row.LastPrice,
                row.PriceChangePercent,
                row.QuoteVolume24h,
                DateTimeOffset.UtcNow,
                row.High24h,
                row.Low24h,
                row.Trades24h,
                row.ScanScore,
                row.SpreadBps,
                row.FundingRate,
                row.VolatilityPercent,
                row.OpenInterest,
                decision.Eligible,
                decision.Reason,
                decision.Watchable,
                row.Contract.ContractType);
        }).ToList());
    }

    [HttpGet("klines")]
    public async Task<ActionResult<IReadOnlyList<KlineBarDto>>> Klines(
        [FromQuery] string symbol = "BTCUSDT",
        [FromQuery] string interval = "15m",
        [FromQuery] int limit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol) || !TimeframeExtensions.TryParseInterval(interval, out var timeframe))
        {
            return BadRequest(new { message = "Use a supported symbol and interval such as 1m, 5m, 15m, 1h, 4h, or 1d." });
        }

        var candles = await _market.GetClosedKlinesAsync(
            symbol.Trim().ToUpperInvariant(),
            timeframe,
            Math.Clamp(limit, 20, 500),
            cancellationToken);

        return Ok(candles
            .Select(c => new KlineBarDto(
                c.OpenTime.ToUnixTimeSeconds(),
                c.Open,
                c.High,
                c.Low,
                c.Close,
                c.Volume))
            .ToList());
    }

    [HttpPost("bots/sample-paper/start")]
    [HttpPost("bots/top-volume-paper/start")]
    public Task<IReadOnlyList<BotDto>> StartTopVolumePaper(CancellationToken cancellationToken) =>
        _lifecycle.StartTopVolumePaperBotsAsync(UserId(), cancellationToken);

    [HttpPost("bots/start-symbol")]
    public Task<BotDto> StartSymbol([FromBody] StartSymbolRequest request, CancellationToken cancellationToken)
    {
        var mode = Enum.TryParse<TradingMode>(request.Mode, true, out var parsed) ? parsed : TradingMode.Paper;
        return _lifecycle.StartSymbolAsync(UserId(), request.Symbol, mode, request.StrategyId, request.RiskProfileId, cancellationToken);
    }

    [HttpPost("bots/create")]
    public Task<CreateBotsResult> CreateBots([FromBody] CreateBotsRequest request, CancellationToken cancellationToken)
    {
        var mode = Enum.TryParse<TradingMode>(request.Mode, true, out var parsed) ? parsed : TradingMode.Paper;
        return _lifecycle.CreateSymbolBotsAsync(UserId(), mode, request.StrategyId, request.RiskProfileId, request.Symbols ?? [], cancellationToken);
    }

    [HttpPost("bots/start-all")]
    public Task<StartBotsResult> StartAll([FromBody] StartBotsRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TradingMode>(request.Mode, true, out var parsed) || parsed is not (TradingMode.Paper or TradingMode.Live))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        return _lifecycle.StartAllIdleAsync(UserId(), parsed, request.PreferredStrategyId, cancellationToken);
    }

    [HttpPost("bots/stop-all")]
    public Task<StopBotsResult> StopAll([FromBody] StartBotsRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TradingMode>(request.Mode, true, out var parsed) || parsed is not (TradingMode.Paper or TradingMode.Live))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        return _lifecycle.StopAllRunningAsync(UserId(), parsed, cancellationToken);
    }

    [HttpGet("strategies")]
    public Task<IReadOnlyList<StrategyDto>> Strategies([FromQuery] string? mode, CancellationToken cancellationToken) =>
        _query.GetStrategiesAsync(mode, cancellationToken);

    [HttpPut("strategies/{strategyId:guid}/scope")]
    public Task<StrategyDto> UpdateStrategyScope(Guid strategyId, [FromBody] SaveSymbolScopeRequest request, CancellationToken cancellationToken) =>
        _query.UpdateStrategyScopeAsync(strategyId, request.AppliesToAllSymbols, request.Symbols, cancellationToken);

    [HttpPost("strategies")]
    public Task<StrategyDto> CreateStrategy([FromBody] SaveStrategyRequest request, [FromQuery] string? mode, CancellationToken cancellationToken) =>
        _query.CreateStrategyAsync(UserId(), request, mode, cancellationToken);

    [HttpPut("strategies/{strategyId:guid}")]
    public Task<StrategyDto> UpdateStrategy(Guid strategyId, [FromBody] SaveStrategyRequest request, CancellationToken cancellationToken) =>
        _query.UpdateStrategyAsync(strategyId, request, cancellationToken);

    [HttpPut("strategies/{strategyId:guid}/enabled")]
    public Task<StrategyDto> SetStrategyEnabled(Guid strategyId, [FromBody] SetStrategyEnabledRequest request, CancellationToken cancellationToken) =>
        _query.SetStrategyEnabledAsync(strategyId, request.Enabled, cancellationToken);

    [HttpGet("strategies/{strategyId:guid}/preview")]
    public Task<StrategyPreviewDto> PreviewStrategy(
        Guid strategyId,
        [FromQuery] string? symbol,
        [FromQuery] int? limit,
        CancellationToken cancellationToken) =>
        _query.PreviewStrategyAsync(strategyId, symbol, limit, cancellationToken);

    [HttpGet("risk-profiles")]
    public Task<IReadOnlyList<RiskProfileDto>> RiskProfiles([FromQuery] string? mode, CancellationToken cancellationToken) =>
        _query.GetRiskProfilesAsync(mode, cancellationToken);

    [HttpPut("risk-profiles/{riskProfileId:guid}/scope")]
    public Task<RiskProfileDto> UpdateRiskScope(Guid riskProfileId, [FromBody] SaveSymbolScopeRequest request, CancellationToken cancellationToken) =>
        _query.UpdateRiskScopeAsync(riskProfileId, request.AppliesToAllSymbols, request.Symbols, cancellationToken);

    [HttpPost("risk-profiles")]
    public Task<RiskProfileDto> CreateRiskProfile([FromBody] SaveRiskProfileRequest request, CancellationToken cancellationToken) =>
        _query.CreateRiskProfileAsync(request, cancellationToken);

    [HttpPut("risk-profiles/{riskProfileId:guid}")]
    public Task<RiskProfileDto> UpdateRiskProfile(Guid riskProfileId, [FromBody] SaveRiskProfileRequest request, CancellationToken cancellationToken) =>
        _query.UpdateRiskProfileAsync(riskProfileId, request, cancellationToken);

    [HttpPost("risk-profiles/{riskProfileId:guid}/activate")]
    public Task<RiskProfileDto> ActivateRiskProfile(Guid riskProfileId, CancellationToken cancellationToken) =>
        _query.ActivateRiskProfileAsync(riskProfileId, cancellationToken);

    [HttpGet("risk-profiles/preview")]
    public Task<RiskPreviewDto> PreviewRisk(
        [FromQuery] string mode,
        [FromQuery] decimal price,
        CancellationToken cancellationToken) =>
        _query.PreviewRiskAsync(mode, price, cancellationToken);

    [HttpPost("bots/{botId:guid}/start")]
    public Task<BotDto> Start(Guid botId, CancellationToken cancellationToken) =>
        _lifecycle.StartAsync(botId, cancellationToken);

    [HttpPost("bots/{botId:guid}/stop")]
    public Task<BotDto> Stop(Guid botId, CancellationToken cancellationToken) =>
        _lifecycle.StopAsync(botId, cancellationToken);

    [HttpDelete("bots/{botId:guid}")]
    public Task<DeleteBotsResult> Delete(Guid botId, CancellationToken cancellationToken) =>
        _lifecycle.DeleteBotsAsync([botId], null, cancellationToken);

    [HttpPost("bots/delete")]
    public Task<DeleteBotsResult> DeleteMany([FromBody] DeleteBotsRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<TradingMode>(request.Mode, true, out var parsed) || parsed is not (TradingMode.Paper or TradingMode.Live))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        return _lifecycle.DeleteBotsAsync(request.Ids ?? [], parsed, cancellationToken);
    }

    [HttpPost("positions/{positionId:guid}/close")]
    public async Task<IActionResult> ClosePosition(Guid positionId, CancellationToken cancellationToken)
    {
        await _engine.ClosePositionAsync(positionId, cancellationToken);
        return NoContent();
    }

    [HttpPost("emergency-stop")]
    public async Task<IActionResult> EmergencyStop(CancellationToken cancellationToken)
    {
        await _lifecycle.EmergencyStopAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("backtests")]
    public Task<BacktestResultDto> RunBacktest([FromBody] RunBacktestRequest request, CancellationToken cancellationToken) =>
        _backtests.RunAsync(UserId(), request, cancellationToken);

    [HttpGet("price-action/arm")]
    public Task<PriceActionArmDto> PriceActionArm(CancellationToken cancellationToken) =>
        _lifecycle.GetPriceActionArmAsync(cancellationToken);

    [HttpPut("price-action/arm")]
    public Task<PriceActionArmDto> SetPriceActionArm([FromBody] SetPriceActionArmRequest request, CancellationToken cancellationToken) =>
        _lifecycle.SetPriceActionArmAsync(request, cancellationToken);

    [HttpGet("research/scalping")]
    public Task<ScalpingResearchSummaryDto> ScalpingResearch(CancellationToken cancellationToken) =>
        _scalping.GetSummaryAsync(cancellationToken);

    [HttpGet("research/scalping/coverage")]
    public Task<IReadOnlyList<ScalpingCoverageDto>> ScalpingCoverage(CancellationToken cancellationToken) =>
        _scalping.GetCoverageAsync(cancellationToken);

    [HttpGet("research/scalping/runs/{id}")]
    public async Task<ActionResult<ScalpingResearchRunDto>> ScalpingRun(string id, CancellationToken cancellationToken)
    {
        var run = await _scalping.GetRunAsync(id, cancellationToken);
        return run is null ? NotFound() : Ok(run);
    }

    [HttpGet("research/scalping/price-action")]
    public Task<PriceActionResearchSummaryDto> PriceActionResearch(CancellationToken cancellationToken) =>
        _priceAction.GetSummaryAsync(cancellationToken);

    [HttpGet("research/scalping/price-action/occurrences")]
    public Task<IReadOnlyList<PriceActionOccurrenceDto>> PriceActionOccurrences(CancellationToken cancellationToken) =>
        _priceAction.GetOccurrencesAsync(cancellationToken);

    [HttpGet("/api/strategies/cross-sectional-reversal")]
    public CrossSectionalReversalStatusDto CrossSectionalReversal([FromServices] IOptions<TradingOptions> options) =>
        CrossSectionalReversalGate.Describe(options.Value);

    [HttpGet("/api/strategies/cross-sectional-reversal/{strategyId}/status")]
    public ActionResult<CrossSectionalReversalStatusDto> CrossSectionalStatus(string strategyId, [FromServices] IOptions<TradingOptions> options) =>
        KnownCrossSection(strategyId) ? CrossSectionalReversalGate.Describe(options.Value) : NotFound();

    [HttpGet("/api/strategies/cross-sectional-reversal/{strategyId}/ranking")]
    public ActionResult<object> CrossSectionalRanking(string strategyId) =>
        KnownCrossSection(strategyId)
            ? new { strategyId, candidates = Array.Empty<object>(), reason = "INSUFFICIENT_DATA", notice = "No live ranking snapshot. Forward returns are not used." }
            : NotFound();

    [HttpGet("/api/strategies/cross-sectional-reversal/{strategyId}/risk")]
    public ActionResult<object> CrossSectionalRisk(string strategyId, [FromServices] IOptions<TradingOptions> options)
    {
        if (!KnownCrossSection(strategyId))
        {
            return NotFound();
        }

        var flags = options.Value.CrossSectionalReversal ?? new CrossSectionalReversalOptions();
        return new
        {
            maxLongPositions = flags.MaxLongPositions,
            maxShortPositions = flags.MaxShortPositions,
            maxTotalPositions = flags.MaxTotalPositions,
            maxCrossSectionalRiskPercent = flags.MaxCrossSectionalRiskPercent,
            maxPerPositionRiskPercent = flags.MaxPerPositionRiskPercent,
            maxLeverage = flags.MaxLeverage,
            marginMode = "Isolated",
            sizing = "EQUAL_RISK",
            dailyLossOnIsolatedEntries = "NOT_APPLIED_BY_EXISTING_RISK_ENGINE"
        };
    }

    [HttpGet("/api/strategies/cross-sectional-reversal/{strategyId}/rebalance")]
    public ActionResult<object> CrossSectionalRebalance(string strategyId) =>
        KnownCrossSection(strategyId)
            ? new { strategyId, rebalance = (object?)null, reason = "INSUFFICIENT_DATA" }
            : NotFound();

    [HttpPost("/api/strategies/cross-sectional-reversal/{strategyId}/paper/enable")]
    public ActionResult CrossSectionalPaperEnable(string strategyId) =>
        KnownCrossSection(strategyId)
            ? Conflict(new { paper = "OFF", reason = "PAPER = OFF. Production approval is INSUFFICIENT_EVIDENCE." })
            : NotFound();

    [HttpPost("/api/strategies/cross-sectional-reversal/{strategyId}/paper/disable")]
    public ActionResult CrossSectionalPaperDisable(string strategyId, [FromServices] IOptions<TradingOptions> options) =>
        KnownCrossSection(strategyId) ? Ok(CrossSectionalReversalGate.Describe(options.Value)) : NotFound();

    [HttpPost("/api/strategies/cross-sectional-reversal/{strategyId}/live/enable")]
    public ActionResult CrossSectionalLiveEnable(string strategyId, [FromServices] IOptions<TradingOptions> options)
    {
        if (!KnownCrossSection(strategyId))
        {
            return NotFound();
        }

        var block = CrossSectionalRiskPolicy.LiveActivationBlock(options.Value, true, true, true, false, false, false, true);
        return Conflict(new { live = "OFF", reason = block ?? "LIVE = OFF." });
    }

    [HttpPost("/api/strategies/cross-sectional-reversal/{strategyId}/live/disable")]
    public ActionResult CrossSectionalLiveDisable(string strategyId, [FromServices] IOptions<TradingOptions> options) =>
        KnownCrossSection(strategyId) ? Ok(CrossSectionalReversalGate.Describe(options.Value)) : NotFound();

    private static bool KnownCrossSection(string strategyId) =>
        strategyId is "cross_sectional_reversal_return_15m" or "cross_sectional_reversal_return_1h" or "cross_sectional_reversal";

    [HttpGet("research/contextual-price-action")]
    public Task<ContextualPriceActionSummaryDto> ContextualPriceAction(CancellationToken cancellationToken) =>
        _priceAction.GetContextualSummaryAsync(cancellationToken);

    private Guid UserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
