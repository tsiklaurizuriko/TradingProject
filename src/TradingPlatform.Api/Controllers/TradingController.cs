using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;

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
    private readonly IBacktestService _backtests;

    public TradingController(
        ITradingQueryService query,
        IBotLifecycleService lifecycle,
        IBotEngine engine,
        IPublicMarketDataClient market,
        IBacktestService backtests)
    {
        _query = query;
        _lifecycle = lifecycle;
        _engine = engine;
        _market = market;
        _backtests = backtests;
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
    public Task<RiskProfileDto> RiskProfile(CancellationToken cancellationToken) =>
        _query.GetRiskProfileAsync(cancellationToken);

    [HttpGet("markets")]
    public async Task<ActionResult<IReadOnlyList<MarketQuoteDto>>> Markets(CancellationToken cancellationToken)
    {
        var universe = await _market.GetPaperUniverseAsync(cancellationToken);
        var quotes = universe
            .Select((s, index) => new MarketQuoteDto(
                s.Symbol,
                UsdtSpotUniverse.DisplayNameOf(s.Symbol),
                index + 1,
                s.LastPrice,
                s.PriceChangePercent,
                s.QuoteVolume,
                DateTimeOffset.UtcNow,
                s.HighPrice,
                s.LowPrice,
                s.Trades24h))
            .ToList();
        return Ok(quotes);
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

        return _lifecycle.StartAllIdleAsync(UserId(), parsed, cancellationToken);
    }

    [HttpGet("strategies")]
    public Task<IReadOnlyList<StrategyDto>> Strategies(CancellationToken cancellationToken) =>
        _query.GetStrategiesAsync(cancellationToken);

    [HttpPut("strategies/{strategyId:guid}/scope")]
    public Task<StrategyDto> UpdateStrategyScope(Guid strategyId, [FromBody] SaveSymbolScopeRequest request, CancellationToken cancellationToken) =>
        _query.UpdateStrategyScopeAsync(strategyId, request.AppliesToAllSymbols, request.Symbols, cancellationToken);

    [HttpPost("strategies")]
    public Task<StrategyDto> CreateStrategy([FromBody] SaveStrategyRequest request, CancellationToken cancellationToken) =>
        _query.CreateStrategyAsync(UserId(), request, cancellationToken);

    [HttpPut("strategies/{strategyId:guid}")]
    public Task<StrategyDto> UpdateStrategy(Guid strategyId, [FromBody] SaveStrategyRequest request, CancellationToken cancellationToken) =>
        _query.UpdateStrategyAsync(strategyId, request, cancellationToken);

    [HttpGet("risk-profiles")]
    public Task<IReadOnlyList<RiskProfileDto>> RiskProfiles(CancellationToken cancellationToken) =>
        _query.GetRiskProfilesAsync(cancellationToken);

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

    private Guid UserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }
}
