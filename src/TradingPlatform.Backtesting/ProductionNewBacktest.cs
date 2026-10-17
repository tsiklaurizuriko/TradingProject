using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Backtesting;

public sealed record ProductionNewRun(
    string StrategyId,
    string Evidence,
    string Verdict,
    ReplayResult? InSample,
    ReplayResult? OutOfSample,
    int WalkForwardWindows,
    string Notes);

/// <summary>
/// Isolated replays for the six -new strategies. A synthetic series is not historical evidence.
/// The shared replay fills entries at the next bar open, charges fees and slippage, checks the stop before the target,
/// and does not award the favorable exit when both could have been touched.
/// </summary>
public static class ProductionNewBacktest
{
    public const int MinimumOutOfSampleTrades = 30;
    public const decimal MinimumOutOfSampleProfitFactor = 1.15m;

    public static ReplaySettings Settings(decimal equity = 10_000m) => new(
        Start: DateTimeOffset.MinValue,
        End: DateTimeOffset.MaxValue,
        InitialBalance: equity,
        RiskPercent: 0.35m,
        Leverage: 2m,
        FeePercent: 0.04m,
        SlippagePercent: 0.05m,
        StopLossPercent: 2m,
        TakeProfitPercent: 4m,
        MaxDailyLossPercent: 2m,
        MaxPortfolioRiskPercent: 1.5m,
        HonorSuggestedStops: true,
        PreserveNullTake: true,
        KeepSuggestedTarget: true);

    public static ProductionNewRun Run(
        string strategyId,
        IReadOnlyList<MarketCandle> candles,
        bool historicalUntouchedSample,
        string symbol = "BTCUSDT",
        string timeframe = "1h")
    {
        if (!StrategyTemplateKeys.IsProductionNew(strategyId))
        {
            return new ProductionNewRun(strategyId, "REJECTED", ProductionNewAcceptance.Fail, null, null, 0, "Not a -new strategy id.");
        }

        var closed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
        if (closed.Count < 80)
        {
            return new ProductionNewRun(strategyId, "INSUFFICIENT_EVIDENCE", ProductionNewAcceptance.InsufficientEvidence, null, null, 0, "Fewer than 80 closed candles.");
        }

        var definition = StrategyValidation.Definition(strategyId, timeframe);
        definition.Symbol = symbol;
        var replay = new BacktestReplay(new StrategyEngine());
        var settings = Settings();
        var (insample, _, oos) = StrategyValidation.ChronologicalSplit(closed);
        var inResult = insample.Count > 50 ? replay.Run(definition, insample, settings) : null;
        var oosResult = oos.Count > 30 ? replay.Run(definition, oos, settings) : null;
        var windows = StrategyValidation.WalkForwardWindows(closed.Count, train: Math.Max(40, closed.Count / 5), test: Math.Max(20, closed.Count / 10), step: Math.Max(20, closed.Count / 10));
        var verdict = ProductionNewAcceptance.Judge(historicalUntouchedSample, oosResult);
        var notes = historicalUntouchedSample
            ? "Out-of-sample segment is the final 20% and was not used to choose these default parameters."
            : "SYNTHETIC_OR_UNCONFIRMED_SAMPLE. This run is not evidence of profitability.";
        return new ProductionNewRun(strategyId, ProductionNewDefaults.Evidence, verdict, inResult, oosResult, windows.Count, notes);
    }
}

public static class ProductionNewAcceptance
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
    public const string InsufficientEvidence = "INSUFFICIENT_EVIDENCE";

    public static string Judge(bool historicalUntouchedSample, ReplayResult? outOfSample)
    {
        if (!historicalUntouchedSample || outOfSample is null)
        {
            return InsufficientEvidence;
        }

        if (outOfSample.NumberOfTrades < ProductionNewBacktest.MinimumOutOfSampleTrades)
        {
            return InsufficientEvidence;
        }

        var longCount = outOfSample.Long.Trades;
        var shortCount = outOfSample.Short.Trades;
        if (longCount < 10 || shortCount < 10)
        {
            return InsufficientEvidence;
        }

        if (!outOfSample.ProfitFactor.IsFinite())
        {
            return InsufficientEvidence;
        }

        var expectancy = outOfSample.NumberOfTrades == 0 ? 0m : outOfSample.NetProfit / outOfSample.NumberOfTrades;
        if (expectancy > 0m
            && outOfSample.ProfitFactor >= ProductionNewBacktest.MinimumOutOfSampleProfitFactor
            && outOfSample.MaximumDrawdown < 25m)
        {
            return Pass;
        }

        return Fail;
    }
}

internal static class ProfitFactorFinite
{
    public static bool IsFinite(this decimal value) => value > 0m && value < 99m;
}
