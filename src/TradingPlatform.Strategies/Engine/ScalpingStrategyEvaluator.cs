using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Thin scalping hypotheses. Most keys remap to existing Advanced/Alpha evaluators.
/// Stochastic and session-filter paths are the only new logic.
/// LIVE Isolated still ignores strategy Exit; Risk Engine owns SL/TP.
/// </summary>
public static class ScalpingStrategyEvaluator
{
    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        p.TemplateKey switch
        {
            StrategyTemplateKeys.ScalpEmaMomentum => EmaMomentum(p, candles, i, context, cache),
            StrategyTemplateKeys.ScalpVwapReclaim =>
                AdvancedStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.VwapPullbackTrend }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpVwapReversion =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.VwapDeviationReversion }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpVwapBreakout =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.VwapBreakoutVolume }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpBreakoutRetest =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.FailedBreakoutReversal }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpLiqSweep =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.LiqSweepReversal }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpRsiPullback => RsiPullback(p, candles, i, context, cache),
            StrategyTemplateKeys.ScalpRsiReversion => RsiReversion(p, candles, i, context, cache),
            StrategyTemplateKeys.ScalpMacdMicro => MacdMicro(p, candles, i, context, cache),
            StrategyTemplateKeys.ScalpBbReversion => BbReversion(p, candles, i, context, cache),
            StrategyTemplateKeys.ScalpBbSqueeze =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.VolSqueezeStructure }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpAtrBreakout =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.AtrNormalizedMomentum }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpAdxTrend =>
                AdvancedStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.SupertrendEmaTrend }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpRvolMomentum =>
                AdvancedStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.VolSpikeEmaTrend }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpMarketStructure =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.MarketStructureTrend }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpStochMomentum => StochMomentum(p, candles, i, context),
            StrategyTemplateKeys.ScalpMtf =>
                AlphaStrategyEvaluator.Evaluate(p with { TemplateKey = StrategyTemplateKeys.MtfTrendStructure }, candles, i, context, cache),
            StrategyTemplateKeys.ScalpSession => SessionFilter(p, candles, i, context, cache),
            StrategyTemplateKeys.ScalpTakerFlow =>
                Unavailable(candles, i, "Taker buy volume series not joined. Missing = DATA_UNAVAILABLE, not 0 imbalance."),
            StrategyTemplateKeys.ScalpPriceOi =>
                Unavailable(candles, i, "Open interest series not joined. Missing = DATA_UNAVAILABLE."),
            StrategyTemplateKeys.ScalpFundingOi =>
                Unavailable(candles, i, "Funding + OI series not joined. Missing = DATA_UNAVAILABLE."),
            StrategyTemplateKeys.ScalpBasis =>
                Unavailable(candles, i, "Mark/index basis series not joined. Missing = DATA_UNAVAILABLE."),
            _ => Detail(SignalType.NoAction, "Unknown scalping template.", candles, i, status: "IMPLEMENTATION_ERROR")
        };

    private static StrategySignalDetail EmaMomentum(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var rsi = cache.Rsi(p.RsiPeriod);
        var close = candles[i].Close;
        if (Hold(context, candles, i, out var held))
        {
            return held;
        }

        if (CrossesAbove(fast, slow, i) && close > (slow[i] ?? 0m) && InRange(rsi[i], p.RsiMinimum, p.RsiLongMax))
        {
            return Detail(SignalType.Buy, "Scalp EMA fast crossed above slow with RSI in band.", candles, i);
        }

        if (CrossesBelow(fast, slow, i) && close < (slow[i] ?? decimal.MaxValue) && InRange(rsi[i], 100m - p.RsiLongMax, 100m - p.RsiMinimum))
        {
            return Detail(SignalType.Sell, "Scalp EMA fast crossed below slow with RSI in band.", candles, i);
        }

        return Detail(SignalType.NoAction, "Scalp EMA momentum not matched.", candles, i);
    }

    private static StrategySignalDetail RsiPullback(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var rsi = cache.Rsi(p.RsiPeriod);
        var slow = cache.Ema(p.EmaSlow);
        var close = candles[i].Close;
        if (Hold(context, candles, i, out var held))
        {
            return held;
        }

        if (close > (slow[i] ?? 0m) && CrossesAboveValue(rsi, p.RsiOversold, i))
        {
            return Detail(SignalType.Buy, "Scalp RSI pullback crossed up through oversold above slow EMA.", candles, i);
        }

        if (close < (slow[i] ?? decimal.MaxValue) && CrossesBelowValue(rsi, p.RsiOverbought, i))
        {
            return Detail(SignalType.Sell, "Scalp RSI pullback crossed down through overbought below slow EMA.", candles, i);
        }

        return Detail(SignalType.NoAction, "Scalp RSI pullback not matched.", candles, i);
    }

    private static StrategySignalDetail RsiReversion(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var rsi = cache.Rsi(p.RsiPeriod);
        var adx = cache.Adx(p.AdxPeriod);
        if (Hold(context, candles, i, out var held))
        {
            return held;
        }

        if (adx[i] is { } adxNow && adxNow >= p.MinimumAdx)
        {
            return Detail(SignalType.NoAction, "Scalp RSI reversion skipped in strong ADX.", candles, i);
        }

        if (CrossesBelowValue(rsi, p.RsiOverbought, i))
        {
            return Detail(SignalType.Sell, "Scalp RSI faded an overbought extreme outside strong trend.", candles, i);
        }

        if (CrossesAboveValue(rsi, p.RsiOversold, i))
        {
            return Detail(SignalType.Buy, "Scalp RSI faded an oversold extreme outside strong trend.", candles, i);
        }

        return Detail(SignalType.NoAction, "Scalp RSI reversion not matched.", candles, i);
    }

    private static StrategySignalDetail MacdMicro(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (macd, signal, hist) = cache.Macd(p.MacdFast, p.MacdSlow, p.MacdSignal);
        var slow = cache.Ema(p.EmaSlow);
        var close = candles[i].Close;
        if (Hold(context, candles, i, out var held))
        {
            return held;
        }

        if (CrossesAbove(macd, signal, i) && (hist[i] ?? 0m) > 0m && close > (slow[i] ?? 0m))
        {
            return Detail(SignalType.Buy, "Scalp MACD histogram flipped up with slow EMA side.", candles, i);
        }

        if (CrossesBelow(macd, signal, i) && (hist[i] ?? 0m) < 0m && close < (slow[i] ?? decimal.MaxValue))
        {
            return Detail(SignalType.Sell, "Scalp MACD histogram flipped down with slow EMA side.", candles, i);
        }

        return Detail(SignalType.NoAction, "Scalp MACD micro not matched.", candles, i);
    }

    private static StrategySignalDetail BbReversion(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (_, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var slow = cache.Ema(p.EmaSlow);
        var prev = candles[i - 1].Close;
        var close = candles[i].Close;
        if (Hold(context, candles, i, out var held))
        {
            return held;
        }

        if (lower[i] is { } lo && prev < lo && close >= lo && close > (slow[i] ?? 0m))
        {
            return Detail(SignalType.Buy, "Scalp close returned inside the lower Bollinger band.", candles, i);
        }

        if (upper[i] is { } up && prev > up && close <= up && close < (slow[i] ?? decimal.MaxValue))
        {
            return Detail(SignalType.Sell, "Scalp close returned inside the upper Bollinger band.", candles, i);
        }

        return Detail(SignalType.NoAction, "Scalp Bollinger reversion not matched.", candles, i);
    }

    private static StrategySignalDetail StochMomentum(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context)
    {
        var k = ScalpingIndicatorSeries.StochasticK(candles, Math.Max(5, p.RsiPeriod));
        var d = ScalpingIndicatorSeries.StochasticD(k, 3);
        if (Hold(context, candles, i, out var held))
        {
            return held;
        }

        if (CrossesAbove(k, d, i) && (k[i] ?? 100m) < 25m)
        {
            return Detail(SignalType.Buy, "Stochastic %K crossed above %D from an oversold extreme.", candles, i);
        }

        if (CrossesBelow(k, d, i) && (k[i] ?? 0m) > 75m)
        {
            return Detail(SignalType.Sell, "Stochastic %K crossed below %D from an overbought extreme.", candles, i);
        }

        return Detail(SignalType.NoAction, "Stochastic momentum not matched.", candles, i);
    }

    private static StrategySignalDetail SessionFilter(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (sessionHigh, sessionLow) = ScalpingIndicatorSeries.SessionHighLow(candles);
        var ema = EmaMomentum(p, candles, i, context, cache);
        if (ema.Signal is not (SignalType.Buy or SignalType.Sell))
        {
            return ema;
        }

        if (sessionHigh[i] is not { } hi || sessionLow[i] is not { } lo || hi <= lo)
        {
            return Detail(SignalType.NoAction, "Session high/low not ready.", candles, i);
        }

        var close = candles[i].Close;
        var mid = (hi + lo) / 2m;
        if (ema.Signal == SignalType.Buy && close < mid)
        {
            return Detail(SignalType.NoAction, "Session filter skipped long below UTC session midpoint.", candles, i);
        }

        if (ema.Signal == SignalType.Sell && close > mid)
        {
            return Detail(SignalType.NoAction, "Session filter skipped short above UTC session midpoint.", candles, i);
        }

        return Detail(ema.Signal, $"{ema.Reason} Session hour {ScalpingIndicatorSeries.UtcHour(candles[i])} UTC used as a filter, not a hardcoded session pick.", candles, i);
    }

    private static bool Hold(StrategyContext context, IReadOnlyList<MarketCandle> candles, int i, out StrategySignalDetail held)
    {
        if (!context.HasOpenPosition)
        {
            held = Detail(SignalType.NoAction, "", candles, i);
            return false;
        }

        held = Detail(SignalType.Hold, "Position open; Isolated book owns SL/TP.", candles, i);
        return true;
    }

    private static bool InRange(decimal? value, decimal min, decimal max) =>
        value is { } v && v >= min && v <= max;

    private static bool CrossesAbove(IReadOnlyList<decimal?> left, IReadOnlyList<decimal?> right, int i)
    {
        if (i < 1 || left[i] is not { } l || right[i] is not { } r || left[i - 1] is not { } lp || right[i - 1] is not { } rp)
        {
            return false;
        }

        return lp <= rp && l > r;
    }

    private static bool CrossesBelow(IReadOnlyList<decimal?> left, IReadOnlyList<decimal?> right, int i)
    {
        if (i < 1 || left[i] is not { } l || right[i] is not { } r || left[i - 1] is not { } lp || right[i - 1] is not { } rp)
        {
            return false;
        }

        return lp >= rp && l < r;
    }

    private static bool CrossesAboveValue(IReadOnlyList<decimal?> series, decimal level, int i)
    {
        if (i < 1 || series[i] is not { } now || series[i - 1] is not { } prev)
        {
            return false;
        }

        return prev <= level && now > level;
    }

    private static bool CrossesBelowValue(IReadOnlyList<decimal?> series, decimal level, int i)
    {
        if (i < 1 || series[i] is not { } now || series[i - 1] is not { } prev)
        {
            return false;
        }

        return prev >= level && now < level;
    }

    private static StrategySignalDetail Unavailable(IReadOnlyList<MarketCandle> candles, int i, string why) =>
        Detail(SignalType.NoAction, "DATA_UNAVAILABLE: " + why, candles, i, status: "DATA_UNAVAILABLE");

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        string status = "RESEARCHING") =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime, Status: status);
}
