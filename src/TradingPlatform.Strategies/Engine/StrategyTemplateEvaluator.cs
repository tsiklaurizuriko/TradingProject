using System.Runtime.CompilerServices;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

internal static class StrategyTemplateEvaluator
{
    private static readonly ConditionalWeakTable<StrategyDefinition, StrategyTemplateParams> ParsedCache = new();

    private static StrategyTemplateParams Parsed(StrategyDefinition definition) =>
        ParsedCache.GetValue(definition, static d => StrategyTemplates.Validate(FromDefinition(d)));

    public static SignalType Evaluate(
        StrategyDefinition definition,
        StrategyContext context,
        IndicatorRegistry _indicators,
        out string reason)
    {
        StrategyTemplateParams parsed;
        try
        {
            parsed = Parsed(definition);
        }
        catch (DomainException ex)
        {
            reason = ex.Message;
            return SignalType.NoAction;
        }

        var candles = context.ClosedCandles;
        var i = candles.Count - 1;
        if (i < 1)
        {
            reason = "Not enough closed candles.";
            return SignalType.NoAction;
        }

        var cache = new CausalIndicatorCache(candles);
        var raw = EvaluateTemplate(parsed, candles, i, context, cache);
        if (raw.Signal is SignalType.Buy or SignalType.Sell
            && !StrategyTemplateKeys.IsFlatRange(parsed.TemplateKey)
            && !StrategyTemplateKeys.IsImported(parsed.TemplateKey)
            && !StrategyTemplateKeys.IsRefactored(parsed.TemplateKey)
            && !PassesQuality(parsed.Quality, candles, i, cache))
        {
            reason = "Quality filter skipped this bar (volume or ATR%).";
            return SignalType.NoAction;
        }

        if (raw.Signal == SignalType.Buy && !StrategySides.AllowsLong(parsed.AllowedSide))
        {
            reason = "LONG signals are disabled on this strategy.";
            return context.HasOpenPosition ? SignalType.Exit : SignalType.NoAction;
        }

        if (raw.Signal == SignalType.Sell && !StrategySides.AllowsShort(parsed.AllowedSide))
        {
            reason = "SHORT signals are disabled on this strategy.";
            return context.HasOpenPosition ? SignalType.Exit : SignalType.NoAction;
        }

        reason = raw.Reason;
        return raw.Signal;
    }

    private static StrategyTemplateParams FromDefinition(StrategyDefinition definition)
    {
        var p = definition.Params;
        var q = definition.Quality;
        return new StrategyTemplateParams(
            StrategyTemplateKeys.Normalize(definition.Template),
            StrategySides.Normalize(definition.AllowedSide),
            string.IsNullOrWhiteSpace(definition.Timeframe) ? "5m" : definition.Timeframe,
            Positive(p?.EmaFast, 20),
            Positive(p?.EmaSlow, 50),
            Positive(p?.RsiPeriod, 14),
            p?.RsiMinimum ?? 50m,
            p?.RsiLongMax ?? 68m,
            p?.RsiOversold ?? 30m,
            p?.RsiOverbought ?? 70m,
            Positive(p?.MacdFast, 12),
            Positive(p?.MacdSlow, 26),
            Positive(p?.MacdSignal, 9),
            Positive(p?.BbPeriod, 20),
            (p?.BbStdDev ?? 0m) > 0m ? p!.BbStdDev : 2m,
            Positive(p?.DonchianLength, 20),
            q is null
                ? new StrategyQualityParams()
                : new StrategyQualityParams(
                    q.RequireVolume,
                    Positive(q.VolumeLookback, 20),
                    q.MinAtrPercent,
                    q.MaxAtrPercent),
            Positive(p?.EntryLookback, 20),
            Positive(p?.ExitLookback, 10),
            Positive(p?.AtrPeriod, 14),
            (p?.AtrStopMultiplier ?? 0m) > 0m ? p!.AtrStopMultiplier : 2m,
            Positive(p?.TrendEmaPeriod, 50),
            p?.VolumeFilterEnabled ?? true,
            Positive(p?.RelativeVolumePeriod, 20),
            p?.MinimumRelativeVolume ?? 1m,
            (p?.MaxVwapDistanceAtr ?? 0m) > 0m ? p!.MaxVwapDistanceAtr : 0.75m,
            (p?.StopAtrMultiplier ?? 0m) > 0m ? p!.StopAtrMultiplier : 1.5m,
            Positive(p?.VolatilityLookback, 100),
            p?.CompressionPercentile is > 0m and <= 1m ? p.CompressionPercentile : 0.20m,
            Positive(p?.AtrExpansionLookback, 20),
            p?.BreakoutRelativeVolume ?? 1.2m,
            Positive(p?.SupertrendPeriod, 10),
            (p?.SupertrendMultiplier ?? 0m) > 0m ? p!.SupertrendMultiplier : 3m,
            Positive(p?.AdxPeriod, 14),
            p?.MinimumAdx ?? 20m,
            Positive(p?.OiLookback, 20),
            p?.OiChangeThreshold ?? 0.02m,
            p?.PriceChangeThreshold ?? 0.01m,
            string.IsNullOrWhiteSpace(p?.OiHypothesis) ? "continuation" : p.OiHypothesis,
            Positive(p?.FundingLookback, 24),
            p?.FundingExtremePercentile ?? 0.90m,
            string.IsNullOrWhiteSpace(p?.FundingHypothesis) ? "continuation" : p.FundingHypothesis,
            p?.ZScoreEntry ?? 2m,
            p?.ValueAreaPercent ?? 0.70m,
            p?.SweepDepthAtr ?? 0.15m,
            p?.SwingLength ?? 3,
            p?.UseFuturesFilter ?? true,
            (p?.PriceDisplacementAtr ?? 0m) > 0m ? p!.PriceDisplacementAtr : 1.5m,
            p?.OiExtremePercentile is > 0m and <= 1m ? p.OiExtremePercentile : 0.90m);
    }

    public static SignalType EvaluateAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        var detail = EvaluateDetailAt(definition, context, cache, index);
        reason = detail.Reason;
        return detail.Signal;
    }

    public static StrategySignalDetail EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index)
    {
        StrategyTemplateParams parsed;
        try
        {
            parsed = Parsed(definition);
        }
        catch (DomainException ex)
        {
            return new StrategySignalDetail(SignalType.NoAction, ex.Message);
        }

        var candles = cache.Candles;
        if (index < 1 || index >= candles.Count)
        {
            return new StrategySignalDetail(SignalType.NoAction, "Not enough closed candles.");
        }

        var raw = EvaluateTemplate(parsed, candles, index, context, cache);
        if (raw.Signal is SignalType.Buy or SignalType.Sell
            && !StrategyTemplateKeys.IsFlatRange(parsed.TemplateKey)
            && !StrategyTemplateKeys.IsImported(parsed.TemplateKey)
            && !StrategyTemplateKeys.IsRefactored(parsed.TemplateKey)
            && !PassesQuality(parsed.Quality, candles, index, cache))
        {
            return new StrategySignalDetail(SignalType.NoAction, "Quality filter skipped this bar (volume or ATR%).");
        }

        if (raw.Signal == SignalType.Buy && !StrategySides.AllowsLong(parsed.AllowedSide))
        {
            return new StrategySignalDetail(
                context.HasOpenPosition ? SignalType.Exit : SignalType.NoAction,
                "LONG signals are disabled on this strategy.");
        }

        if (raw.Signal == SignalType.Sell && !StrategySides.AllowsShort(parsed.AllowedSide))
        {
            return new StrategySignalDetail(
                context.HasOpenPosition ? SignalType.Exit : SignalType.NoAction,
                "SHORT signals are disabled on this strategy.");
        }

        return raw;
    }

    private static StrategySignalDetail EvaluateTemplate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (StrategyTemplateKeys.IsObsoleteAlias(p.TemplateKey))
        {
            return new StrategySignalDetail(
                SignalType.NoAction,
                $"Obsolete strategy id '{StrategyTemplateKeys.Normalize(p.TemplateKey)}'. Canonical id is '{StrategyTemplateKeys.CanonicalId(p.TemplateKey)}'. The retired implementation is not executed.",
                candles[i].CloseTime);
        }

        return StrategyTemplateKeys.IsRefactored(p.TemplateKey)
            ? RefactoredStrategyEvaluator.Evaluate(p, candles, i, context, cache)
            : StrategyTemplateKeys.IsImported(p.TemplateKey)
            ? ImportedRuleEvaluator.Evaluate(p, candles, i, context, cache)
            : StrategyTemplateKeys.IsCrossSectionalReversal(p.TemplateKey)
            ? new StrategySignalDetail(
                SignalType.NoAction,
                "Cross-sectional reversal ranks the contemporaneous universe. A single-symbol preview does not emit an order.",
                candles[i].CloseTime,
                Status: "RESEARCHING")
            : StrategyTemplateKeys.IsNearMiss(p.TemplateKey)
            ? new StrategySignalDetail(
                SignalType.NoAction,
                "NEAR_MISS uses the frozen contextual book on the last closed 5m bar. This preview path does not invent an EMA signal.",
                candles[i].CloseTime,
                Status: NearMissAudit.Status)
            : StrategyTemplateKeys.IsResearch(p.TemplateKey)
                || p.TemplateKey == StrategyTemplateKeys.FlowZone
                || p.TemplateKey == StrategyTemplateKeys.SqueezeWatch
                || p.TemplateKey == StrategyTemplateKeys.ImpulseCatch
            ? AdvancedStrategyEvaluator.Evaluate(p, candles, i, context, cache)
            : Wrap(p.TemplateKey switch
            {
                StrategyTemplateKeys.MacdTrend => Macd(p, candles, i, context, cache),
                StrategyTemplateKeys.RsiPullback => RsiPullback(p, candles, i, context, cache),
                StrategyTemplateKeys.BollingerReversion => Bollinger(p, candles, i, context, cache),
                StrategyTemplateKeys.DonchianBreakout => Donchian(p, candles, i, context, cache),
                _ => EmaRsi(p, candles, i, context, cache)
            }, candles, i);
    }

    private static StrategySignalDetail Wrap(
        (SignalType Signal, string Reason) tuple,
        IReadOnlyList<MarketCandle> candles,
        int i) =>
        new(tuple.Signal, tuple.Reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime);

    private static (SignalType Signal, string Reason) EmaRsi(
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
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && CrossesBelow(fast, slow, i))
            {
                return (SignalType.Exit, "EMA fast crossed below slow.");
            }

            if (!IsLong(context) && CrossesAbove(fast, slow, i))
            {
                return (SignalType.Exit, "EMA fast crossed above slow.");
            }

            return (SignalType.Hold, "Position open; EMA exit not triggered.");
        }

        if (CrossesAbove(fast, slow, i) && close > (slow[i] ?? 0m) && InRange(rsi[i], p.RsiMinimum, p.RsiLongMax))
        {
            return (SignalType.Buy, "EMA cross up, close above slow EMA, RSI in band.");
        }

        if (CrossesBelow(fast, slow, i)
            && close < (slow[i] ?? decimal.MaxValue)
            && InRange(rsi[i], 100m - p.RsiLongMax, 100m - p.RsiMinimum))
        {
            return (SignalType.Sell, "EMA cross down, close below slow EMA, RSI in band.");
        }

        return (SignalType.NoAction, "EMA RSI entry not matched.");
    }

    private static (SignalType Signal, string Reason) Macd(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (macd, signal, hist) = cache.Macd(p.MacdFast, p.MacdSlow, p.MacdSignal);
        var slow = cache.Ema(p.EmaSlow);
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && CrossesBelow(macd, signal, i))
            {
                return (SignalType.Exit, "MACD crossed below the signal line.");
            }

            if (!IsLong(context) && CrossesAbove(macd, signal, i))
            {
                return (SignalType.Exit, "MACD crossed above the signal line.");
            }

            return (SignalType.Hold, "Position open; MACD exit not triggered.");
        }

        if (CrossesAbove(macd, signal, i) && (hist[i] ?? 0m) > 0m && close > (slow[i] ?? 0m))
        {
            return (SignalType.Buy, "MACD crossed above signal with positive histogram.");
        }

        if (CrossesBelow(macd, signal, i) && (hist[i] ?? 0m) < 0m && close < (slow[i] ?? decimal.MaxValue))
        {
            return (SignalType.Sell, "MACD crossed below signal with negative histogram.");
        }

        return (SignalType.NoAction, "MACD entry not matched.");
    }

    private static (SignalType Signal, string Reason) RsiPullback(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var rsi = cache.Rsi(p.RsiPeriod);
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && (CrossesBelowValue(rsi, 50m, i) || CrossesBelow(fast, slow, i)))
            {
                return (SignalType.Exit, "RSI recrossed 50 or EMA reversed.");
            }

            if (!IsLong(context) && (CrossesAboveValue(rsi, 50m, i) || CrossesAbove(fast, slow, i)))
            {
                return (SignalType.Exit, "RSI recrossed 50 or EMA reversed.");
            }

            return (SignalType.Hold, "Position open; RSI exit not triggered.");
        }

        if (close > (slow[i] ?? 0m) && CrossesAboveValue(rsi, p.RsiOversold, i))
        {
            return (SignalType.Buy, "Uptrend RSI crossed up through oversold.");
        }

        if (close < (slow[i] ?? decimal.MaxValue) && CrossesBelowValue(rsi, p.RsiOverbought, i))
        {
            return (SignalType.Sell, "Downtrend RSI crossed down through overbought.");
        }

        return (SignalType.NoAction, "RSI pullback entry not matched.");
    }

    private static (SignalType Signal, string Reason) Bollinger(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (mid, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var slow = cache.Ema(p.EmaSlow);
        var prev = candles[i - 1].Close;
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (mid[i] is { } band)
            {
                if (IsLong(context) && close >= band)
                {
                    return (SignalType.Exit, "Close reached the Bollinger mid band.");
                }

                if (!IsLong(context) && close <= band)
                {
                    return (SignalType.Exit, "Close reached the Bollinger mid band.");
                }
            }

            return (SignalType.Hold, "Position open; mid-band exit not triggered.");
        }

        if (lower[i] is { } lo && prev < lo && close >= lo && close > (slow[i] ?? 0m))
        {
            return (SignalType.Buy, "Close returned inside the lower Bollinger band.");
        }

        if (upper[i] is { } up && prev > up && close <= up && close < (slow[i] ?? decimal.MaxValue))
        {
            return (SignalType.Sell, "Close returned inside the upper Bollinger band.");
        }

        return (SignalType.NoAction, "Bollinger entry not matched.");
    }

    private static (SignalType Signal, string Reason) Donchian(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (high, low) = cache.Donchian(p.DonchianLength);
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && ((low[i] is { } lo && close < lo) || CrossesBelow(fast, slow, i)))
            {
                return (SignalType.Exit, "Close broke the Donchian low or EMA reversed.");
            }

            if (!IsLong(context) && ((high[i] is { } hi && close > hi) || CrossesAbove(fast, slow, i)))
            {
                return (SignalType.Exit, "Close broke the Donchian high or EMA reversed.");
            }

            return (SignalType.Hold, "Position open; Donchian exit not triggered.");
        }

        if (BreaksAbove(high, candles, i))
        {
            return (SignalType.Buy, "Close broke the Donchian N-bar high.");
        }

        if (BreaksBelow(low, candles, i))
        {
            return (SignalType.Sell, "Close broke the Donchian N-bar low.");
        }

        return (SignalType.NoAction, "Donchian entry not matched.");
    }

    private static bool PassesQuality(
        StrategyQualityParams? quality,
        IReadOnlyList<MarketCandle> candles,
        int i,
        CausalIndicatorCache cache)
    {
        var q = quality ?? new StrategyQualityParams();
        if (q.RequireVolume)
        {
            var lookback = Math.Max(2, q.VolumeLookback);
            var avg = cache.AvgVolume(lookback);
            if (avg[i] is not { } mean || candles[i].Volume <= mean)
            {
                return false;
            }
        }

        if (q.MinAtrPercent > 0m || q.MaxAtrPercent > 0m)
        {
            var atrPct = cache.AtrPercent(14);
            if (atrPct[i] is not { } pct)
            {
                return false;
            }

            if (q.MinAtrPercent > 0m && pct < q.MinAtrPercent)
            {
                return false;
            }

            if (q.MaxAtrPercent > 0m && pct > q.MaxAtrPercent)
            {
                return false;
            }
        }

        return true;
    }

    private static bool BreaksAbove(IReadOnlyList<decimal?> channelHigh, IReadOnlyList<MarketCandle> candles, int i)
    {
        if (i < 1 || channelHigh[i] is not { } high)
        {
            return false;
        }

        var previousInside = channelHigh[i - 1] is not { } previousHigh || candles[i - 1].Close <= previousHigh;
        return previousInside && candles[i].Close > high;
    }

    private static bool BreaksBelow(IReadOnlyList<decimal?> channelLow, IReadOnlyList<MarketCandle> candles, int i)
    {
        if (i < 1 || channelLow[i] is not { } low)
        {
            return false;
        }

        var previousInside = channelLow[i - 1] is not { } previousLow || candles[i - 1].Close >= previousLow;
        return previousInside && candles[i].Close < low;
    }

    private static bool CrossesAbove(IReadOnlyList<decimal?> left, IReadOnlyList<decimal?> right, int i)
    {
        if (i < 1 || left[i] is not { } l || right[i] is not { } r || left[i - 1] is not { } lp || right[Math.Min(i - 1, right.Count - 1)] is not { } rp)
        {
            return false;
        }

        return lp <= rp && l > r;
    }

    private static bool CrossesBelow(IReadOnlyList<decimal?> left, IReadOnlyList<decimal?> right, int i)
    {
        if (i < 1 || left[i] is not { } l || right[i] is not { } r || left[i - 1] is not { } lp || right[Math.Min(i - 1, right.Count - 1)] is not { } rp)
        {
            return false;
        }

        return lp >= rp && l < r;
    }

    private static bool CrossesAboveValue(IReadOnlyList<decimal?> left, decimal right, int i)
    {
        if (i < 1 || left[i] is not { } l || left[i - 1] is not { } lp)
        {
            return false;
        }

        return lp <= right && l > right;
    }

    private static bool CrossesBelowValue(IReadOnlyList<decimal?> left, decimal right, int i)
    {
        if (i < 1 || left[i] is not { } l || left[i - 1] is not { } lp)
        {
            return false;
        }

        return lp >= right && l < right;
    }

    private static bool InRange(decimal? value, decimal min, decimal max) =>
        value is { } v && v >= min && v <= max;

    private static bool IsLong(StrategyContext context) =>
        !context.HasOpenPosition || context.PositionSide != PositionSide.Short;

    private static int Positive(int? value, int fallback) =>
        value is > 0 ? value.Value : fallback;
}
