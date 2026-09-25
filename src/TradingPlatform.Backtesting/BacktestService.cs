using System.Text.Json;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Backtesting;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Backtesting;

public sealed class BacktestService : IBacktestService
{
    public const int MaxBars = 6000;
    public const int WarmupBars = 120;

    private readonly ITradingStore _store;
    private readonly IPublicMarketDataClient _market;
    private readonly StrategyDefinitionValidator _validator;
    private readonly BacktestReplay _replay;

    public BacktestService(
        ITradingStore store,
        IPublicMarketDataClient market,
        IStrategyEngine engine,
        StrategyDefinitionValidator validator)
    {
        _store = store;
        _market = market;
        _validator = validator;
        _replay = new BacktestReplay(engine);
    }

    public async Task<BacktestResultDto> RunAsync(Guid userId, RunBacktestRequest request, CancellationToken cancellationToken = default)
    {
        var symbol = (request.Symbol ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(symbol) || !symbol.EndsWith("USDT", StringComparison.Ordinal))
        {
            throw new DomainException(ErrorCodes.InvalidSymbol, "Use a USD-M USDT perpetual such as BTCUSDT.");
        }

        if (!TimeframeExtensions.TryParseInterval(request.Timeframe, out var timeframe))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use a supported interval: 5m, 15m, 1h, or 1d.");
        }

        var from = request.From.ToUniversalTime();
        var to = request.To.ToUniversalTime();
        if (to <= from)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The To date must be after From.");
        }

        if (to > DateTimeOffset.UtcNow)
        {
            to = DateTimeOffset.UtcNow;
        }

        var capital = request.InitialCapital;
        var fees = request.FeesPercent;
        var slippage = request.SlippagePercent;
        if (capital < 100m || capital > 10_000_000m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Initial capital must be between 100 and 10,000,000 USDT.");
        }

        if (fees is < 0m or > 2m || slippage is < 0m or > 2m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Fees and slippage must be between 0 and 2 percent.");
        }

        var version = request.StrategyId is { } id && id != Guid.Empty
            ? await _store.GetLatestStrategyVersionAsync(id, cancellationToken)
            : await _store.GetSampleStrategyVersionAsync(cancellationToken);
        if (version is null)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Save a strategy before running a backtest.");
        }

        var definition = _validator.Parse(version.DefinitionJson);
        var warmup = timeframe.ToDuration() * WarmupBars;
        var fetchStart = from - warmup;
        var candles = await _market.GetClosedKlinesRangeAsync(
            symbol,
            timeframe,
            fetchStart,
            to,
            MaxBars + WarmupBars,
            cancellationToken);
        if (candles.Count < WarmupBars + 10)
        {
            throw new DomainException(
                ErrorCodes.ExchangeUnavailable,
                "Not enough historical candles from Binance for this range. Use a longer window or a higher timeframe.");
        }

        var book = await _store.GetConservativeRiskAsync(cancellationToken);
        var result = _replay.Run(
            definition,
            candles,
            new ReplaySettings(
                from,
                to,
                capital,
                book.RiskPerTradePercent,
                book.MaxLeverage,
                fees,
                slippage,
                book.StopLossPercent,
                book.TakeProfitPercent,
                book.MaxDailyLossPercent,
                book.MaxPortfolioRiskPercent,
                book.MaxSimultaneousPositions,
                book.MaxConsecutiveLosses,
                book.CooldownMinutes,
                book.MinimumLiquidationSafetyBufferPercent,
                HonorSuggestedStops: StrategyTemplateKeys.IsFlatRange(definition.Template) || StrategyTemplateKeys.IsImported(definition.Template),
                MaxHoldBars: StrategyTemplateKeys.IsFlatRange(definition.Template) ? FlatRangeStrategy.MaxHoldHours : 0,
                BookStopsOff: StrategyTemplateKeys.IsImported(definition.Template)));

        var user = await _store.GetUserAsync(userId, cancellationToken)
            ?? await _store.GetFirstAdminAsync(cancellationToken);
        var assumptions = JsonSerializer.Serialize(new
        {
            result.Assumptions,
            result.BarsUsed,
            riskBook = book.Name,
            riskPercent = book.RiskPerTradePercent,
            leverage = book.MaxLeverage,
            feesPercent = fees,
            slippagePercent = slippage,
            stopLossPercent = book.StopLossPercent,
            takeProfitPercent = book.TakeProfitPercent,
            capped = candles.Count >= MaxBars + WarmupBars,
        });

        var backtest = new Backtest
        {
            UserId = user.Id,
            StrategyVersionId = version.Id,
            StrategyVersion = version,
            Symbol = symbol,
            Timeframe = timeframe,
            StartDate = from,
            EndDate = to,
            InitialBalance = capital,
            FeeBps = fees * 100m,
            SlippageBps = slippage * 100m,
            Status = "Completed",
        };
        var run = new BacktestRun
        {
            Backtest = backtest,
            InitialBalance = result.InitialBalance,
            FinalBalance = result.FinalBalance,
            NetProfit = result.NetProfit,
            ReturnPercent = result.ReturnPercent,
            NumberOfTrades = result.NumberOfTrades,
            WinRate = result.WinRate,
            ProfitFactor = result.ProfitFactor,
            AverageWin = result.AverageWin,
            AverageLoss = result.AverageLoss,
            MaximumDrawdown = result.MaximumDrawdown,
            SharpeRatio = result.SharpeRatio,
            FeesPaid = result.FeesPaid,
            LargestWinningTrade = result.LargestWinningTrade,
            LargestLosingTrade = result.LargestLosingTrade,
            AssumptionsJson = assumptions,
        };
        foreach (var trade in result.Trades)
        {
            run.Trades.Add(new BacktestTrade
            {
                BacktestRun = run,
                OpenedAt = trade.OpenedAt,
                ClosedAt = trade.ClosedAt,
                Quantity = trade.Quantity,
                EntryPrice = trade.EntryPrice,
                ExitPrice = trade.ExitPrice,
                PnL = trade.PnL,
                Fees = trade.Fees,
                Reason = trade.Reason,
                Side = trade.Side,
            });
        }

        backtest.Runs.Add(run);
        version.IsImmutable = true;
        version.FirstUsedAt ??= DateTimeOffset.UtcNow;
        await _store.AddBacktestAsync(backtest, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);

        return new BacktestResultDto(
            backtest.Id,
            backtest.Status,
            symbol,
            timeframe.ToBinanceInterval(),
            version.Strategy.Name,
            version.VersionNumber,
            result.WindowStart,
            result.WindowEnd,
            result.InitialBalance,
            result.FinalBalance,
            result.NetProfit,
            result.ReturnPercent,
            result.NumberOfTrades,
            result.WinRate,
            result.ProfitFactor,
            result.AverageWin,
            result.AverageLoss,
            result.MaximumDrawdown,
            result.SharpeRatio,
            result.FeesPaid,
            result.LargestWinningTrade,
            result.LargestLosingTrade,
            result.BarsUsed,
            result.Assumptions,
            result.Equity.Select(p => new BacktestEquityPointDto(p.Time, p.Equity)).ToList(),
            result.Trades.Select(t => new BacktestTradeDto(
                t.OpenedAt,
                t.ClosedAt,
                t.Quantity,
                t.EntryPrice,
                t.ExitPrice,
                t.PnL,
                t.Fees,
                t.Reason,
                t.Side)).ToList());
    }
}
