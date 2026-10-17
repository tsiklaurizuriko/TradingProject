using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

public static class MarketRegimeStates
{
    public const string Trending = "TRENDING";
    public const string Ranging = "RANGING";
    public const string HighVolatility = "HIGH_VOLATILITY";
    public const string Uncertain = "UNCERTAIN";
    public const string InsufficientData = "INSUFFICIENT_DATA";
}

/// <summary>
/// Per-symbol regime from completed candles. ADX is strength, not direction.
/// High volatility is a rolling NATR percentile, not one fixed NATR cutoff.
/// A regime label never submits an order.
/// </summary>
public static class MarketRegimeClassifier
{
    public static string Confirmed(
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache,
        StrategyTemplateParams parameters,
        StrategyContext? context = null)
    {
        var own = ConfirmedSeries(candles, index, cache, parameters);
        if (!parameters.RequireBenchmark)
        {
            return own;
        }

        if (context?.BenchmarkCandles is not { Count: > 0 } benchmark)
        {
            return MarketRegimeStates.Uncertain;
        }

        var sliced = ProductionNewMath.SliceAt(benchmark, candles[index].CloseTime);
        if (sliced.Count < 2)
        {
            return MarketRegimeStates.Uncertain;
        }

        var benchmarkCache = new CausalIndicatorCache(sliced);
        var other = ConfirmedSeries(sliced, sliced.Count - 1, benchmarkCache, parameters);
        if (own == MarketRegimeStates.InsufficientData || other == MarketRegimeStates.InsufficientData)
        {
            return MarketRegimeStates.InsufficientData;
        }

        return own == other ? own : MarketRegimeStates.Uncertain;
    }

    public static string Permitted(string regime) => regime switch
    {
        MarketRegimeStates.Trending => "trend-following-new,breakout-volume-new,momentum-relative-strength-new,funding-basis-new",
        MarketRegimeStates.Ranging => "mean-reversion-rsi-bollinger-new,funding-basis-new",
        _ => "none"
    };

    public static bool Allows(string regime, string strategyId) =>
        Permitted(regime).Split(',').Contains(StrategyTemplateKeys.CanonicalId(strategyId), StringComparer.Ordinal);

    private static string ConfirmedSeries(
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache,
        StrategyTemplateParams parameters)
    {
        var confirm = parameters.SwingLength is >= 1 and <= 6 ? parameters.SwingLength : 2;
        if (index < confirm - 1)
        {
            return MarketRegimeStates.InsufficientData;
        }

        var labels = new string[confirm];
        for (var k = 0; k < confirm; k++)
        {
            labels[k] = Raw(candles, index - k, cache, parameters);
        }

        if (labels.All(label => label == MarketRegimeStates.InsufficientData))
        {
            return MarketRegimeStates.InsufficientData;
        }

        return labels.All(label => label == labels[0]) ? labels[0] : MarketRegimeStates.Uncertain;
    }

    private static string Raw(
        IReadOnlyList<MarketCandle> candles,
        int index,
        CausalIndicatorCache cache,
        StrategyTemplateParams parameters)
    {
        if (index < 1 || index >= candles.Count || !candles[index].IsClosed || !ProductionNewMath.BarUsable(candles[index]))
        {
            return MarketRegimeStates.InsufficientData;
        }

        var atr = cache.Atr(parameters.AtrPeriod);
        var adx = cache.Adx(parameters.AdxPeriod);
        if (atr[index] is not { } atrValue || atrValue < 0m || candles[index].Close <= 0m || adx[index] is not { } adxValue)
        {
            return MarketRegimeStates.InsufficientData;
        }

        var natr = cache.AtrPercent(parameters.AtrPeriod);
        var percentile = ProductionNewMath.FullPercentile(natr, index, parameters.VolatilityLookback);
        if (percentile is not { } rank)
        {
            return MarketRegimeStates.InsufficientData;
        }

        var highVol = parameters.FundingExtremePercentile is > 0m and < 1m
            ? parameters.FundingExtremePercentile
            : 0.90m;
        if (rank >= highVol)
        {
            return MarketRegimeStates.HighVolatility;
        }

        var trendMin = parameters.MinimumAdx;
        if (adxValue > trendMin)
        {
            return MarketRegimeStates.Trending;
        }

        var rangeMax = Math.Max(0m, trendMin - 2m);
        return adxValue < rangeMax ? MarketRegimeStates.Ranging : MarketRegimeStates.Uncertain;
    }
}
