using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Versioned replacements for the fifteen operator strategies. Baseline template keys stay on their existing evaluators.
/// Signals use only closed bars at index i. An opposite setup while a position is open exits and does not flip.
/// </summary>
public static class RefactoredStrategyEvaluator
{
    public static int MaxHoldBars(string? key) => StrategyTemplateKeys.Normalize(key) switch
    {
        StrategyTemplateKeys.ImpulseCatchV2 => 32,
        StrategyTemplateKeys.ClucMay72018V2 or StrategyTemplateKeys.ClucMay72018V2Thirty => 24,
        StrategyTemplateKeys.CombinedBinHClucV2 => 24,
        StrategyTemplateKeys.FlatRangeV2 => 48,
        StrategyTemplateKeys.BollingerReversionV2 => 12,
        _ => 0
    };

    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < 1 || i >= candles.Count)
        {
            return new StrategySignalDetail(SignalType.NoAction, "Not enough closed candles.");
        }

        if (!candles[i].IsClosed)
        {
            return Detail(SignalType.NoAction, "Unclosed candle is not an entry or exit signal.", candles, i);
        }

        return StrategyTemplateKeys.Normalize(p.TemplateKey) switch
        {
            StrategyTemplateKeys.ImpulseCatchV2 => Impulse(p, candles, i, context, cache),
            StrategyTemplateKeys.ZigZagFadeV2 => ZigZag(p, candles, i, context, cache),
            StrategyTemplateKeys.TripleSupertrendV2 => Triple(p, candles, i, context, cache),
            StrategyTemplateKeys.TsMomentumV2 => TsMomentum(p, candles, i, context, cache),
            StrategyTemplateKeys.EmaCrossV2 => EmaCross(p, candles, i, context, cache),
            StrategyTemplateKeys.FAdxSmaV2 => AdxSma(p, candles, i, context, cache),
            StrategyTemplateKeys.BinHv45V2 => BinHv(p, candles, i, context, cache),
            StrategyTemplateKeys.ClucMay72018V2 or StrategyTemplateKeys.ClucMay72018V2Thirty => Cluc(p, candles, i, context, cache),
            StrategyTemplateKeys.CombinedBinHClucV2 => Combined(p, candles, i, context, cache),
            StrategyTemplateKeys.DonchianBreakoutV2FourHour or StrategyTemplateKeys.DonchianBreakoutV2Daily => Donchian(p, candles, i, context, cache),
            StrategyTemplateKeys.SqueezeWatchV2 => Squeeze(p, candles, i, context, cache),
            StrategyTemplateKeys.FlowZoneV2 => Flow(p, candles, i, context, cache),
            StrategyTemplateKeys.FlatRangeV2 => Flat(p, candles, i, context, cache),
            StrategyTemplateKeys.EmaRsiTrendV2 or StrategyTemplateKeys.EmaRsiTrendV2Thirty => EmaRsi(p, candles, i, context, cache),
            StrategyTemplateKeys.BollingerReversionV2 => Bollinger(p, candles, i, context, cache),
            _ => Detail(SignalType.NoAction, "Unknown refactored template.", candles, i)
        };
    }

    private static StrategySignalDetail Impulse(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var window = Math.Max(8, p.EntryLookback);
        var ready = Math.Max(window + 2, Math.Max(p.EmaFast + 2, p.RelativeVolumePeriod + 2));
        if (i < ready)
        {
            return Detail(SignalType.NoAction, "Impulse warmup is incomplete.", candles, i);
        }

        var ema = cache.Ema(p.EmaFast);
        var atr = cache.Atr(p.AtrPeriod);
        if (ema[i] is not { } emaNow || atr[i] is not { } atrNow || atrNow <= 0m || candles[i].Close <= 0m)
        {
            return Detail(SignalType.NoAction, "Impulse indicators are not ready.", candles, i);
        }

        if (context.HasOpenPosition)
        {
            if (!IsLong(context))
            {
                return Detail(SignalType.Exit, "Impulse Catch v2 is long only.", candles, i);
            }

            var trail = candles[i].Close - (2.5m * atrNow);
            if (ReachedR(context, candles[i].Close, 1m) && context.AverageEntryPrice is { } entry)
            {
                trail = Math.Max(trail, entry);
            }

            return Detail(SignalType.Hold, "Impulse long is open. Trail is 2.5 ATR.", candles, i, stop: trail);
        }

        var maxAge = Math.Max(1, p.MaxImpulseAgeBars);
        var oldest = Math.Max(ready, i - maxAge);
        var impulseAt = FindImpulse(candles, atr, p, window, i - 1, oldest);
        if (impulseAt == -2)
        {
            return Detail(SignalType.NoAction, "Impulse volume is missing or not usable. No order.", candles, i);
        }

        if (impulseAt < 0)
        {
            var staleFloor = Math.Max(ready, oldest - maxAge);
            var stale = oldest > ready ? FindImpulse(candles, atr, p, window, oldest - 1, staleFloor) : -1;
            if (stale >= 0)
            {
                return Detail(
                    SignalType.NoAction,
                    $"Impulse setup expired. impulse_index={stale} impulse_age={i - stale} max_age={maxAge} pullback=0 expired=1. No order.",
                    candles,
                    i,
                    snapshot: ImpulseSnapshot(stale, i - stale, pullback: false, expired: true));
            }

            return Detail(SignalType.NoAction, "No confirmed impulse before this bar.", candles, i);
        }

        var age = i - impulseAt;
        var touchedAt = -1;
        decimal swing = decimal.MaxValue;
        for (var k = impulseAt + 1; k < i; k++)
        {
            swing = Math.Min(swing, candles[k].Low);
            if (touchedAt < 0 && ema[k] is { } line && atr[k] is { } atrK && atrK > 0m && candles[k].Low <= line + (0.25m * atrK))
            {
                touchedAt = k;
            }

            if (touchedAt >= 0 && k > touchedAt && candles[k].Close < candles[touchedAt].Low)
            {
                return Detail(
                    SignalType.NoAction,
                    $"Impulse setup invalidated after the pullback. impulse_index={impulseAt} impulse_age={age} pullback=1 expired=0. Close broke the pullback low. No order.",
                    candles,
                    i,
                    snapshot: ImpulseSnapshot(impulseAt, age, pullback: true, expired: false));
            }
        }

        var bar = candles[i];
        if (touchedAt < 0 || emaNow <= 0m || bar.Close <= emaNow || bar.Close <= bar.Open)
        {
            return Detail(
                SignalType.NoAction,
                $"Impulse is waiting for a pullback after the impulse and a bullish reclaim of EMA. impulse_index={impulseAt} impulse_age={age} pullback={(touchedAt >= 0 ? 1 : 0)} expired=0.",
                candles,
                i,
                snapshot: ImpulseSnapshot(impulseAt, age, touchedAt >= 0, expired: false));
        }

        if (swing == decimal.MaxValue)
        {
            return Detail(SignalType.NoAction, "Impulse pullback low is missing.", candles, i, snapshot: ImpulseSnapshot(impulseAt, age, pullback: true, expired: false));
        }

        var structural = swing - (0.3m * atrNow);
        var capped = bar.Close - (p.StopAtrMultiplier * atrNow);
        var stop = structural < bar.Close ? Math.Max(structural, capped) : capped;
        if (stop >= bar.Close || bar.Close - stop <= 0m)
        {
            return Detail(SignalType.NoAction, "Impulse stop distance is not valid.", candles, i, snapshot: ImpulseSnapshot(impulseAt, age, pullback: true, expired: false));
        }

        return Detail(
            SignalType.Buy,
            $"Impulse reclaim after a pullback to EMA. Long only. impulse_index={impulseAt} impulse_age={age} pullback=1 expired=0.",
            candles,
            i,
            stop: stop,
            snapshot: ImpulseSnapshot(impulseAt, age, pullback: true, expired: false));
    }

    private static int FindImpulse(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<decimal?> atr,
        StrategyTemplateParams p,
        int window,
        int from,
        int oldest)
    {
        for (var k = from; k >= oldest; k--)
        {
            if (k - window < 0 || candles[k].Close <= 0m || candles[k - window].Close <= 0m || atr[k] is not { } atrK || atrK <= 0m)
            {
                continue;
            }

            var priorHigh = candles[k - window].Close;
            for (var j = k - window; j < k; j++)
            {
                priorHigh = Math.Max(priorHigh, candles[j].Close);
            }

            if (candles[k].Close <= priorHigh)
            {
                continue;
            }

            var ret = (candles[k].Close - candles[k - window].Close) / candles[k - window].Close;
            var atrPct = atrK / candles[k].Close;
            if (ret <= 0m || ret < p.PriceDisplacementAtr * atrPct)
            {
                continue;
            }

            var volume = PriorMean(candles, k, p.RelativeVolumePeriod, c => c.Volume);
            if (volume is not { } mean || mean <= 0m || !StrategyExecutionRules.VolumeUsable(candles[k].Volume))
            {
                return -2;
            }

            if (candles[k].Volume < mean * p.MinimumRelativeVolume)
            {
                continue;
            }

            return k;
        }

        return -1;
    }

    private static Dictionary<string, decimal?> ImpulseSnapshot(int index, int age, bool pullback, bool expired) =>
        new()
        {
            ["impulseIndex"] = index,
            ["impulseAge"] = age,
            ["pullback"] = pullback ? 1m : 0m,
            ["expired"] = expired ? 1m : 0m
        };

    private static StrategySignalDetail ZigZag(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var length = Math.Max(2, p.SwingLength);
        if (i < Math.Max(length * 2, p.AtrPeriod))
        {
            return Detail(SignalType.NoAction, "ZigZag warmup is incomplete.", candles, i);
        }

        var lows = cache.ConfirmedSwingLow(length);
        var highs = cache.ConfirmedSwingHigh(length);
        var atr = cache.Atr(p.AtrPeriod);
        if (atr[i] is not { } atrNow || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "ZigZag ATR is not ready.", candles, i);
        }

        var longSetup = SweepLong(candles, i, lows[i], atrNow, p.SweepDepthAtr);
        var shortSetup = SweepShort(candles, i, highs[i], atrNow, p.SweepDepthAtr);
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && shortSetup)
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED ZigZag short setup closes the long. No flip on this bar.", candles, i);
            }

            if (!IsLong(context) && longSetup)
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED ZigZag long setup closes the short. No flip on this bar.", candles, i);
            }

            var trail = IsLong(context)
                ? candles[i].Close - (p.AtrStopMultiplier * atrNow)
                : candles[i].Close + (p.AtrStopMultiplier * atrNow);
            if (ReachedR(context, candles[i].Close, 1m) && context.AverageEntryPrice is { } entry)
            {
                trail = IsLong(context) ? Math.Max(trail, entry) : Math.Min(trail, entry);
            }

            return Detail(SignalType.Hold, "ZigZag position is open.", candles, i, stop: trail);
        }

        if (longSetup && shortSetup)
        {
            return Detail(SignalType.NoAction, "ZigZag long and short fired together. No order.", candles, i);
        }

        if (longSetup && lows[i] is { } swingLow)
        {
            var extreme = Math.Min(candles[i - 1].Low, candles[i].Low);
            var stop = extreme - (p.SweepDepthAtr * atrNow);
            var risk = candles[i].Close - stop;
            if (risk <= 0m)
            {
                return Detail(SignalType.NoAction, "ZigZag long stop is not valid.", candles, i);
            }

            var take = candles[i].Close + (1.5m * risk);
            if (!StrategyExecutionRules.PaysRoundTrip(candles[i].Close, take))
            {
                return Detail(SignalType.NoAction, "ZigZag long target does not clear round-trip cost.", candles, i);
            }

            return Detail(SignalType.Buy, "ZigZag sweep reclaimed the confirmed swing low. setup_type=ZIGZAG.", candles, i, stop, take);
        }

        if (shortSetup && highs[i] is { } swingHigh)
        {
            var extreme = Math.Max(candles[i - 1].High, candles[i].High);
            var stop = extreme + (p.SweepDepthAtr * atrNow);
            var risk = stop - candles[i].Close;
            if (risk <= 0m)
            {
                return Detail(SignalType.NoAction, "ZigZag short stop is not valid.", candles, i);
            }

            var take = candles[i].Close - (1.5m * risk);
            if (!StrategyExecutionRules.PaysRoundTrip(candles[i].Close, take))
            {
                return Detail(SignalType.NoAction, "ZigZag short target does not clear round-trip cost.", candles, i);
            }

            return Detail(SignalType.Sell, "ZigZag sweep reclaimed the confirmed swing high. setup_type=ZIGZAG.", candles, i, stop, take);
        }

        return Detail(SignalType.NoAction, "ZigZag sweep is not confirmed.", candles, i);
    }

    private static bool SweepLong(IReadOnlyList<MarketCandle> candles, int i, decimal? swing, decimal atr, decimal depthAtr)
    {
        if (swing is not { } level || i < 1 || !StrategyExecutionRules.VolumeUsable(candles[i].Volume))
        {
            return false;
        }

        var swept = candles[i - 1].Low < level - (depthAtr * atr) && candles[i - 1].Close > level;
        var notBearish = candles[i].Close >= candles[i].Open && candles[i].Close > level;
        return swept && notBearish;
    }

    private static bool SweepShort(IReadOnlyList<MarketCandle> candles, int i, decimal? swing, decimal atr, decimal depthAtr)
    {
        if (swing is not { } level || i < 1 || !StrategyExecutionRules.VolumeUsable(candles[i].Volume))
        {
            return false;
        }

        var swept = candles[i - 1].High > level + (depthAtr * atr) && candles[i - 1].Close < level;
        var notBullish = candles[i].Close <= candles[i].Open && candles[i].Close < level;
        return swept && notBullish;
    }

    private static StrategySignalDetail Triple(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < p.TrendEmaPeriod + 2)
        {
            return Detail(SignalType.NoAction, "Triple Supertrend v2 warmup is incomplete.", candles, i);
        }

        var regime = cache.Ema(p.TrendEmaPeriod);
        var ema = cache.Ema(p.EmaFast);
        var atr = cache.Atr(p.AtrPeriod);
        var trend = cache.SupertrendDirection(p.SupertrendPeriod, p.SupertrendMultiplier);
        if (regime[i] is not { } ema200 || ema[i] is not { } ema20 || atr[i] is not { } atrNow || trend[i] is not { } dir || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "Triple Supertrend v2 indicators are not ready.", candles, i);
        }

        var close = candles[i].Close;
        var longTrend = close > ema200 && dir > 0m;
        var shortTrend = close < ema200 && dir < 0m;
        if (context.HasOpenPosition)
        {
            var flipped = IsLong(context) ? dir < 0m : dir > 0m;
            if (flipped)
            {
                return Detail(SignalType.Exit, "Supertrend direction flipped.", candles, i);
            }

            if ((IsLong(context) && shortTrend) || (!IsLong(context) && longTrend))
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED Supertrend regime opposes the open position.", candles, i);
            }

            var trail = IsLong(context) ? close - (p.AtrStopMultiplier * atrNow) : close + (p.AtrStopMultiplier * atrNow);
            if (ReachedR(context, close, 1m) && context.AverageEntryPrice is { } entry)
            {
                trail = IsLong(context) ? Math.Max(trail, entry) : Math.Min(trail, entry);
            }

            return Detail(SignalType.Hold, "Triple Supertrend v2 position is open.", candles, i, stop: trail);
        }

        var pulled = ema[i - 1] is { } prevEma && (IsBetween(candles[i - 1].Low, candles[i - 1].High, prevEma) || Math.Abs(candles[i - 1].Low - prevEma) <= 0.25m * atrNow);
        if (longTrend && pulled && close > ema20 && candles[i].Close > candles[i].Open)
        {
            var stop = close - (p.StopAtrMultiplier * atrNow);
            return stop < close
                ? Detail(SignalType.Buy, "Supertrend pullback reclaimed EMA20 above EMA200.", candles, i, stop: stop)
                : Detail(SignalType.NoAction, "Triple Supertrend long stop is not valid.", candles, i);
        }

        if (shortTrend && pulled && close < ema20 && candles[i].Close < candles[i].Open)
        {
            var stop = close + (p.StopAtrMultiplier * atrNow);
            return stop > close
                ? Detail(SignalType.Sell, "Supertrend pullback lost EMA20 below EMA200.", candles, i, stop: stop)
                : Detail(SignalType.NoAction, "Triple Supertrend short stop is not valid.", candles, i);
        }

        return Detail(SignalType.NoAction, "Triple Supertrend v2 entry is not matched.", candles, i);
    }

    private static StrategySignalDetail TsMomentum(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (!string.IsNullOrWhiteSpace(context.Symbol)
            && !string.Equals(context.Symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase))
        {
            return Detail(SignalType.NoAction, "Time-series momentum v2 is BTC only.", candles, i);
        }

        if (i < Math.Max(p.TrendEmaPeriod, p.EntryLookback) + 2)
        {
            return Detail(SignalType.NoAction, "Time-series momentum warmup is incomplete.", candles, i);
        }

        var ema = cache.Ema(p.TrendEmaPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        var start = i - p.EntryLookback;
        if (ema[i] is not { } line || atr[i] is not { } atrNow || atrNow <= 0m || candles[start].Close <= 0m)
        {
            return Detail(SignalType.NoAction, "Time-series momentum indicators are not ready.", candles, i);
        }

        var ret = (candles[i].Close - candles[start].Close) / candles[start].Close;
        var on = candles[i].Close > line && ret > 0m;
        if (context.HasOpenPosition)
        {
            if (!IsLong(context))
            {
                return Detail(SignalType.Exit, "Time-series momentum v2 is long only.", candles, i);
            }

            if (!on)
            {
                return Detail(SignalType.Exit, "Close lost EMA or 28-day return is no longer positive.", candles, i);
            }

            return Detail(SignalType.Hold, "Time-series momentum long is open.", candles, i, stop: candles[i].Close - (p.AtrStopMultiplier * atrNow));
        }

        if (!on)
        {
            return Detail(SignalType.NoAction, "EMA and 28-day return are not both positive.", candles, i);
        }

        var stop = candles[i].Close - (p.AtrStopMultiplier * atrNow);
        return stop < candles[i].Close
            ? Detail(SignalType.Buy, "Closed bar is above EMA and the lookback return is positive. Long only.", candles, i, stop: stop)
            : Detail(SignalType.NoAction, "Time-series momentum stop is not valid.", candles, i);
    }

    private static StrategySignalDetail EmaCross(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < p.TrendEmaPeriod + 2)
        {
            return Detail(SignalType.NoAction, "EMA cross warmup is incomplete.", candles, i);
        }

        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var regime = cache.Ema(p.TrendEmaPeriod);
        var adx = cache.Adx(p.AdxPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (fast[i] is not { } emaFast || slow[i] is not { } emaSlow || regime[i] is not { } ema200 || adx[i] is not { } adxNow || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "EMA cross indicators are not ready.", candles, i);
        }

        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            var crossed = IsLong(context) ? emaFast < emaSlow : emaFast > emaSlow;
            if (crossed)
            {
                return Detail(SignalType.Exit, "EMA cross reversed. REVERSAL_DEFERRED until the next closed bar.", candles, i);
            }

            var trail = IsLong(context) ? close - (p.AtrStopMultiplier * atrNow) : close + (p.AtrStopMultiplier * atrNow);
            return Detail(SignalType.Hold, "EMA cross position is open.", candles, i, stop: trail);
        }

        if (adxNow <= p.MinimumAdx)
        {
            return Detail(SignalType.NoAction, "ADX is below the EMA cross threshold.", candles, i);
        }

        var inZone = candles[i - 1].Low <= emaFast && candles[i - 1].Low >= emaSlow;
        if (close > ema200 && emaFast > emaSlow && inZone && close > emaFast && candles[i].Close > candles[i].Open)
        {
            var swing = MinLow(candles, i, p.SwingLength);
            var stop = Math.Max(swing, close - (p.StopAtrMultiplier * atrNow));
            if (stop >= close)
            {
                stop = close - (p.StopAtrMultiplier * atrNow);
            }

            return stop < close
                ? Detail(SignalType.Buy, "EMA20 is above EMA50, price reclaimed EMA20 inside an up regime.", candles, i, stop: stop)
                : Detail(SignalType.NoAction, "EMA cross long stop is not valid.", candles, i);
        }

        var shortZone = candles[i - 1].High >= emaFast && candles[i - 1].High <= emaSlow;
        if (close < ema200 && emaFast < emaSlow && shortZone && close < emaFast && candles[i].Close < candles[i].Open)
        {
            var swing = MaxHigh(candles, i, p.SwingLength);
            var stop = Math.Min(swing, close + (p.StopAtrMultiplier * atrNow));
            if (stop <= close)
            {
                stop = close + (p.StopAtrMultiplier * atrNow);
            }

            return stop > close
                ? Detail(SignalType.Sell, "EMA20 is below EMA50, price lost EMA20 inside a down regime.", candles, i, stop: stop)
                : Detail(SignalType.NoAction, "EMA cross short stop is not valid.", candles, i);
        }

        return Detail(SignalType.NoAction, "EMA cross entry is not matched.", candles, i);
    }

    private static StrategySignalDetail AdxSma(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < p.EmaSlow + p.AdxPeriod)
        {
            return Detail(SignalType.NoAction, "ADX SMA warmup is incomplete.", candles, i);
        }

        var fast = Sma(candles, p.EmaFast);
        var slow = Sma(candles, p.EmaSlow);
        var regime = cache.Ema(p.TrendEmaPeriod);
        var adx = cache.Adx(p.AdxPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (fast[i] is not { } nowFast || fast[i - 1] is not { } prevFast
            || slow[i] is not { } nowSlow || slow[i - 1] is not { } prevSlow
            || regime[i] is not { } ema200 || adx[i] is not { } adxNow || adx[i - 1] is not { } adx1 || adx[i - 2] is not { } adx2
            || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "ADX SMA indicators are not ready.", candles, i);
        }

        var crossUp = prevFast <= prevSlow && nowFast > nowSlow;
        var crossDown = prevFast >= prevSlow && nowFast < nowSlow;
        var rising = adxNow > adx1 && adx1 > adx2;
        var exitAdx = p.MinimumAdx - 2m;
        if (context.HasOpenPosition)
        {
            if ((IsLong(context) && crossDown) || (!IsLong(context) && crossUp))
            {
                return Detail(SignalType.Exit, "Opposite SMA cross closes the position. REVERSAL_DEFERRED.", candles, i);
            }

            if (adxNow < exitAdx && adx1 < exitAdx)
            {
                return Detail(SignalType.Exit, "ADX stayed below the exit threshold for two bars.", candles, i);
            }

            var trail = IsLong(context)
                ? candles[i].Close - (p.AtrStopMultiplier * atrNow)
                : candles[i].Close + (p.AtrStopMultiplier * atrNow);
            if (ReachedR(context, candles[i].Close, 1m) && context.AverageEntryPrice is { } entry)
            {
                trail = IsLong(context) ? Math.Max(trail, entry) : Math.Min(trail, entry);
            }

            return Detail(SignalType.Hold, "ADX SMA position is open.", candles, i, stop: trail);
        }

        if (adxNow <= p.MinimumAdx || !rising)
        {
            return Detail(SignalType.NoAction, "ADX is not rising through the entry threshold.", candles, i);
        }

        if (crossUp && candles[i].Close > ema200)
        {
            var stop = candles[i].Close - (p.StopAtrMultiplier * atrNow);
            return Detail(SignalType.Buy, "SMA cross up with rising ADX above EMA200.", candles, i, stop: stop);
        }

        if (crossDown && candles[i].Close < ema200)
        {
            var stop = candles[i].Close + (p.StopAtrMultiplier * atrNow);
            return Detail(SignalType.Sell, "SMA cross down with rising ADX below EMA200.", candles, i, stop: stop);
        }

        return Detail(SignalType.NoAction, "ADX SMA entry is not matched.", candles, i);
    }

    private static StrategySignalDetail BinHv(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < p.BbPeriod + 2)
        {
            return Detail(SignalType.NoAction, "BinHV45 v2 warmup is incomplete.", candles, i);
        }

        var regime = HtfBias(context, candles[i].CloseTime, p.TrendEmaPeriod);
        if (regime is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: 1h EMA regime is missing. No order.", candles, i);
        }

        var (mid, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var atr = cache.Atr(p.AtrPeriod);
        if (lower[i] is not { } lo || lower[i - 1] is not { } prevLo || upper[i] is not { } up || atr[i] is not { } atrNow || mid[i] is not { } middle)
        {
            return Detail(SignalType.NoAction, "BinHV45 v2 bands are not ready.", candles, i);
        }

        var volume = PriorMean(candles, i, p.RelativeVolumePeriod, c => c.Volume);
        var longSetup = regime == true
            && candles[i - 1].Close < prevLo
            && candles[i].Close > lo
            && candles[i].Close > candles[i - 1].High
            && volume is { } mean && mean > 0m
            && StrategyExecutionRules.VolumeUsable(candles[i].Volume)
            && candles[i].Volume > mean * p.MinimumRelativeVolume;
        var shortSetup = regime == false
            && candles[i - 1].Close > upper[i - 1]
            && candles[i].Close < up
            && candles[i].Close < candles[i - 1].Low
            && volume is { } shortMean && shortMean > 0m
            && StrategyExecutionRules.VolumeUsable(candles[i].Volume)
            && candles[i].Volume > shortMean * p.MinimumRelativeVolume;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && shortSetup)
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED BinHV45 short setup.", candles, i);
            }

            if (!IsLong(context) && longSetup)
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED BinHV45 long setup.", candles, i);
            }

            if (IsLong(context) && candles[i].Close >= middle && InProfit(context, candles[i].Close))
            {
                return Detail(SignalType.Exit, "BinHV45 close reached the middle band in profit.", candles, i);
            }

            if (!IsLong(context) && candles[i].Close <= middle && InProfit(context, candles[i].Close))
            {
                return Detail(SignalType.Exit, "BinHV45 close reached the middle band in profit.", candles, i);
            }

            return Detail(SignalType.Hold, "BinHV45 v2 position is open.", candles, i, stop: AtrStop(context, candles[i].Close, atrNow, p.StopAtrMultiplier));
        }

        if (longSetup)
        {
            return BandEntry(SignalType.Buy, candles, i, candles[i].Low, atrNow, p, "BINHV45");
        }

        if (shortSetup)
        {
            return BandEntry(SignalType.Sell, candles, i, candles[i].High, atrNow, p, "BINHV45");
        }

        return Detail(SignalType.NoAction, "BinHV45 v2 reversal is not confirmed.", candles, i);
    }

    private static StrategySignalDetail Cluc(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < Math.Max(p.BbPeriod, p.RsiPeriod) + 2)
        {
            if (context.HasOpenPosition)
            {
                return TimeExpired(context, candles[i], p)
                    ? Detail(SignalType.Exit, "Cluc v2 time stop.", candles, i)
                    : Detail(SignalType.Hold, "Cluc v2 warmup is incomplete. The open position is not closed for missing bars.", candles, i);
            }

            return Detail(SignalType.NoAction, "Cluc v2 warmup is incomplete.", candles, i);
        }

        var (_, _, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var mid = cache.Bollinger(p.BbPeriod, p.BbStdDev).Mid;
        var rsi = cache.Rsi(p.RsiPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        var lo = lower[i];
        var prevLo = lower[i - 1];
        var rsiNow = rsi[i];
        var rsiPrev = rsi[i - 1];
        var atrNow = atr[i];
        var middle = mid[i];
        var indicatorsReady = lo is not null && prevLo is not null && rsiNow is not null && rsiPrev is not null && atrNow is not null && middle is not null;
        var regime = HtfBias(context, candles[i].CloseTime, p.TrendEmaPeriod);
        if (context.HasOpenPosition)
        {
            if (!IsLong(context))
            {
                return Detail(SignalType.Exit, "Cluc v2 is long only.", candles, i);
            }

            if (TimeExpired(context, candles[i], p))
            {
                return Detail(SignalType.Exit, "Cluc v2 time stop.", candles, i);
            }

            if (indicatorsReady && candles[i].Close >= middle!.Value && InProfit(context, candles[i].Close))
            {
                return Detail(SignalType.Exit, "Cluc v2 middle band reached in profit.", candles, i);
            }

            if (regime == false)
            {
                return Detail(SignalType.Exit, "Cluc v2 higher timeframe turned bearish. Long is closed. This is not a missing-data path.", candles, i);
            }

            if (!indicatorsReady)
            {
                return Detail(SignalType.Hold, "Cluc v2 indicators are not ready. The open position stays under the existing stop.", candles, i);
            }

            var holdReason = regime is null
                ? "Cluc v2 long stays open. Higher-timeframe data is missing and is not treated as bearish."
                : "Cluc v2 long is open.";
            return Detail(SignalType.Hold, holdReason, candles, i, stop: candles[i - 1].Low - (p.SweepDepthAtr * atrNow!.Value));
        }

        if (regime != true)
        {
            return Detail(SignalType.NoAction, regime is null
                ? "DATA_UNAVAILABLE: 1h EMA regime is missing. No order."
                : "Cluc v2 stays flat unless the higher timeframe is bullish.", candles, i);
        }

        if (!indicatorsReady)
        {
            return Detail(SignalType.NoAction, "Cluc v2 indicators are not ready.", candles, i);
        }

        var setup = candles[i - 1].Close < prevLo!.Value
            && candles[i].Close > lo!.Value
            && rsiPrev!.Value < p.RsiOversold
            && rsiNow!.Value > rsiPrev.Value
            && candles[i].Close > candles[i].Open
            && StrategyExecutionRules.VolumeUsable(candles[i].Volume);
        if (!setup)
        {
            return Detail(SignalType.NoAction, "Cluc v2 reversal is not confirmed.", candles, i);
        }

        var stop = candles[i - 1].Low - (p.SweepDepthAtr * atrNow!.Value);
        var risk = candles[i].Close - stop;
        if (risk <= 0m)
        {
            return Detail(SignalType.NoAction, "Cluc v2 stop is not valid.", candles, i);
        }

        var rTarget = candles[i].Close + (1.5m * risk);
        var take = middle!.Value > candles[i].Close && StrategyExecutionRules.RewardMultiple(candles[i].Close, stop, middle.Value, 1.5m)
            ? middle.Value
            : rTarget;
        if (!StrategyExecutionRules.PaysRoundTrip(candles[i].Close, take) || !StrategyExecutionRules.RewardMultiple(candles[i].Close, stop, take, 1.5m))
        {
            return Detail(SignalType.NoAction, "Cluc v2 target does not clear the minimum reward.", candles, i);
        }

        return Detail(SignalType.Buy, "Cluc v2 reclaimed the lower band with RSI turning up. setup_type=CIUC.", candles, i, stop, take);
    }

    private static StrategySignalDetail Combined(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < Math.Max(p.BbPeriod, p.TrendEmaPeriod) + 5)
        {
            return Detail(SignalType.NoAction, "Combined v2 warmup is incomplete.", candles, i);
        }

        var adx = cache.Adx(p.AdxPeriod);
        var slope = cache.EmaSlope(p.TrendEmaPeriod);
        if (adx[i] is not { } adxNow || slope[i] is not { } slopeNow || candles[i].Close <= 0m)
        {
            return Detail(SignalType.NoAction, "Combined v2 regime is not ready.", candles, i);
        }

        var flat = adxNow < p.MinimumAdx && Math.Abs(slopeNow) / candles[i].Close <= p.PriceChangeThreshold;
        var (mid, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var atr = cache.Atr(p.AtrPeriod);
        var rsi = cache.Rsi(p.RsiPeriod);
        if (lower[i] is not { } lo || upper[i] is not { } up || mid[i] is not { } middle || atr[i] is not { } atrNow || rsi[i - 1] is not { } rsiPrev || rsi[i] is not { } rsiNow)
        {
            return Detail(SignalType.NoAction, "Combined v2 indicators are not ready.", candles, i);
        }

        var binhv = candles[i - 1].Close < lower[i - 1] && candles[i].Close > lo && candles[i].Close > candles[i - 1].High && StrategyExecutionRules.VolumeUsable(candles[i].Volume);
        var ciuc = candles[i - 1].Close < lower[i - 1] && candles[i].Close > lo && rsiPrev < p.RsiOversold && rsiNow > rsiPrev && candles[i].Close > candles[i].Open && StrategyExecutionRules.VolumeUsable(candles[i].Volume);
        var shortBinhv = candles[i - 1].Close > upper[i - 1] && candles[i].Close < up && candles[i].Close < candles[i - 1].Low && StrategyExecutionRules.VolumeUsable(candles[i].Volume);
        if (context.HasOpenPosition)
        {
            if (!flat || candles[i].Close > up || candles[i].Close < lo)
            {
                return Detail(SignalType.Exit, "Combined v2 range is no longer valid.", candles, i);
            }

            if (TimeExpired(context, candles[i], p))
            {
                return Detail(SignalType.Exit, "Combined v2 time stop.", candles, i);
            }

            if (IsLong(context) && candles[i].Close >= middle && InProfit(context, candles[i].Close))
            {
                return Detail(SignalType.Exit, "Combined v2 middle band reached in profit.", candles, i);
            }

            if (!IsLong(context) && candles[i].Close <= middle && InProfit(context, candles[i].Close))
            {
                return Detail(SignalType.Exit, "Combined v2 middle band reached in profit.", candles, i);
            }

            if ((IsLong(context) && shortBinhv) || (!IsLong(context) && (binhv || ciuc)))
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED Combined setup opposes the open position.", candles, i);
            }

            return Detail(SignalType.Hold, "Combined v2 position is open. A losing middle-band touch is not an exit.", candles, i);
        }

        if (!flat)
        {
            return Detail(SignalType.NoAction, "Combined v2 requires a flat range regime.", candles, i);
        }

        if (!binhv && !ciuc && !shortBinhv)
        {
            return Detail(SignalType.NoAction, "Combined v2 confirmation is missing.", candles, i);
        }

        var setup = binhv && ciuc ? "BOTH_ONE_POSITION_BINHV45" : binhv ? "BINHV45" : ciuc ? "CIUC" : "BINHV45_SHORT";
        var side = shortBinhv && !binhv && !ciuc ? SignalType.Sell : SignalType.Buy;
        var extreme = side == SignalType.Buy ? candles[i].Low : candles[i].High;
        return BandEntry(side, candles, i, extreme, atrNow, p, setup, middle, 1.3m);
    }

    private static StrategySignalDetail Donchian(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var entryLen = Math.Max(5, p.EntryLookback);
        var exitLen = Math.Max(2, p.ExitLookback);
        if (i < Math.Max(entryLen, p.TrendEmaPeriod) + 2)
        {
            return Detail(SignalType.NoAction, "Donchian v2 warmup is incomplete.", candles, i);
        }

        var (entryHigh, entryLow) = cache.Donchian(entryLen);
        var (exitHigh, exitLow) = cache.Donchian(exitLen);
        var ema = cache.Ema(p.TrendEmaPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (entryHigh[i] is not { } hi || entryLow[i] is not { } lo || ema[i] is not { } line || atr[i] is not { } atrNow || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "Donchian v2 channel is not ready.", candles, i);
        }

        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && exitLow[i] is { } exitLo && close < exitLo)
            {
                return Detail(SignalType.Exit, "Donchian close broke the exit-channel low.", candles, i);
            }

            if (!IsLong(context) && exitHigh[i] is { } exitHi && close > exitHi)
            {
                return Detail(SignalType.Exit, "Donchian close broke the exit-channel high.", candles, i);
            }

            if ((IsLong(context) && close < lo) || (!IsLong(context) && close > hi))
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED Donchian opposite break closes the position.", candles, i);
            }

            var trail = IsLong(context) ? close - (p.AtrStopMultiplier * atrNow) : close + (p.AtrStopMultiplier * atrNow);
            return Detail(SignalType.Hold, "Donchian v2 position is open. ATR stop is the execution stop.", candles, i, stop: trail);
        }

        if (close > hi && close > line)
        {
            var stop = close - (p.StopAtrMultiplier * atrNow);
            return stop < close
                ? Detail(SignalType.Buy, "Donchian close broke the prior channel high with the EMA filter.", candles, i, stop: stop)
                : Detail(SignalType.NoAction, "Donchian long stop is not valid.", candles, i);
        }

        if (close < lo && close < line)
        {
            var stop = close + (p.StopAtrMultiplier * atrNow);
            return stop > close
                ? Detail(SignalType.Sell, "Donchian close broke the prior channel low with the EMA filter.", candles, i, stop: stop)
                : Detail(SignalType.NoAction, "Donchian short stop is not valid.", candles, i);
        }

        return Detail(SignalType.NoAction, "Donchian v2 entry is not matched.", candles, i);
    }

    private static StrategySignalDetail Squeeze(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var funding = context.FundingRate;
        var oi = context.OpenInterest;
        if (funding is null || oi is null || funding.Count != candles.Count || oi.Count != candles.Count)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: funding or open interest is missing. Missing values are not zero.", candles, i);
        }

        if (FundingAge(funding, i) > 2)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: funding is stale by more than two bars. No order.", candles, i);
        }

        if (i < Math.Max(p.FundingLookback, 24) || funding[i] is not { } rate)
        {
            return Detail(SignalType.NoAction, "Squeeze funding history is incomplete.", candles, i);
        }

        var sample = new List<decimal>();
        for (var k = i - p.FundingLookback + 1; k <= i; k++)
        {
            if (funding[k] is { } value)
            {
                sample.Add(value);
            }
        }

        if (sample.Count < 30)
        {
            return Detail(SignalType.NoAction, "Squeeze funding sample is too short for a percentile.", candles, i);
        }

        var high = Percentile(sample, p.FundingExtremePercentile);
        var low = Percentile(sample, 1m - p.FundingExtremePercentile);
        var atr = cache.Atr(p.AtrPeriod);
        if (atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Squeeze ATR is not ready.", candles, i);
        }

        var crowdedLong = rate >= high;
        var crowdedShort = rate <= low;
        var failedUp = FailedBreak(candles, i, longSide: true);
        var failedDown = FailedBreak(candles, i, longSide: false);
        if (context.HasOpenPosition)
        {
            if ((IsLong(context) && crowdedLong && failedUp) || (!IsLong(context) && crowdedShort && failedDown))
            {
                return Detail(SignalType.Exit, "Squeeze funding and a failed break confirm the exit. Funding alone does not.", candles, i);
            }

            return Detail(SignalType.Hold, "Squeeze position is open.", candles, i, stop: AtrStop(context, candles[i].Close, atrNow, p.StopAtrMultiplier));
        }

        var oiOk = OiNotMissing(oi, i, 4) && OiNotMissing(oi, i, 24);
        if (!oiOk)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: open interest window is missing. No order.", candles, i);
        }

        if (crowdedShort && failedDown)
        {
            return BandEntry(SignalType.Buy, candles, i, candles[i].Low, atrNow, p, "SQUEEZE_CROWDED_SHORT");
        }

        if (crowdedLong && failedUp)
        {
            return BandEntry(SignalType.Sell, candles, i, candles[i].High, atrNow, p, "SQUEEZE_CROWDED_LONG");
        }

        return Detail(SignalType.NoAction, "Squeeze needs a funding percentile extreme and a failed break.", candles, i);
    }

    private static StrategySignalDetail Flow(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var regime = HtfBias(context, candles[i].CloseTime, p.TrendEmaPeriod);
        if (regime is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: 1h regime is missing. No order.", candles, i);
        }

        if (i < p.EntryLookback + 2)
        {
            return Detail(SignalType.NoAction, "Flow zone warmup is incomplete.", candles, i);
        }

        var oi = context.OpenInterest;
        if (oi is null || oi.Count != candles.Count || oi[i] is not { } oiNow || oi[i - 1] is not { } oiPrev)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: open interest is missing. No order.", candles, i);
        }

        var share = TakerShare(candles[i]);
        if (share is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: taker buy volume is missing. No order.", candles, i);
        }

        var atr = cache.Atr(p.AtrPeriod);
        if (atr[i] is not { } atrNow || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "Flow zone ATR is not ready.", candles, i);
        }

        var brokenHigh = MaxHigh(candles, i - 2, p.EntryLookback);
        var brokenLow = MinLow(candles, i - 2, p.EntryLookback);
        var brokeUp = candles[i - 1].Close > brokenHigh;
        var brokeDown = candles[i - 1].Close < brokenLow;
        var retestLong = candles[i].Low <= brokenHigh && candles[i].Close > brokenHigh;
        var retestShort = candles[i].High >= brokenLow && candles[i].Close < brokenLow;
        var longConfirm = brokeUp && (retestLong || candles[i].Close > brokenHigh) && candles[i].Close > candles[i].Open && share > 0.60m && oiNow >= oiPrev && regime == true;
        var shortConfirm = brokeDown && (retestShort || candles[i].Close < brokenLow) && candles[i].Close < candles[i].Open && share < 0.40m && oiNow >= oiPrev && regime == false;
        if (context.HasOpenPosition)
        {
            if ((IsLong(context) && shortConfirm) || (!IsLong(context) && longConfirm))
            {
                return Detail(SignalType.Exit, "REVERSAL_DEFERRED Flow zone opposite confirmation.", candles, i);
            }

            var trail = IsLong(context) ? candles[i].Close - (p.AtrStopMultiplier * atrNow) : candles[i].Close + (p.AtrStopMultiplier * atrNow);
            if (ReachedR(context, candles[i].Close, 1m) && context.AverageEntryPrice is { } entry)
            {
                trail = IsLong(context) ? Math.Max(trail, entry) : Math.Min(trail, entry);
            }

            return Detail(SignalType.Hold, "Flow zone position is open.", candles, i, stop: trail);
        }

        if (longConfirm)
        {
            var stop = candles[i].Close - (p.StopAtrMultiplier * atrNow);
            var take = candles[i].Close + (2m * (candles[i].Close - stop));
            return Detail(SignalType.Buy, "Flow breakout confirmed on the next bar with taker buy and open interest.", candles, i, stop, take);
        }

        if (shortConfirm)
        {
            var stop = candles[i].Close + (p.StopAtrMultiplier * atrNow);
            var take = candles[i].Close - (2m * (stop - candles[i].Close));
            return Detail(SignalType.Sell, "Flow breakdown confirmed on the next bar with taker sell and open interest.", candles, i, stop, take);
        }

        return Detail(SignalType.NoAction, "Flow zone confirmation is missing.", candles, i);
    }

    private static StrategySignalDetail Flat(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < p.EntryLookback + p.AdxPeriod)
        {
            return Detail(SignalType.NoAction, "Flat range warmup is incomplete.", candles, i);
        }

        var adx = cache.Adx(p.AdxPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (adx[i] is not { } adxNow || atr[i] is not { } atrNow || !TryRange(candles, i, p.EntryLookback, out var low, out var high))
        {
            return Detail(SignalType.NoAction, "Flat range is not valid.", candles, i);
        }

        var width = (high - low) / candles[i - 1].Close;
        if (adxNow >= p.MinimumAdx || width < 0.008m || width > 0.06m)
        {
            if (context.HasOpenPosition)
            {
                return Detail(SignalType.Exit, "Flat range regime broke.", candles, i);
            }

            return Detail(SignalType.NoAction, "ADX or range width is outside the flat band.", candles, i);
        }

        var close = candles[i].Close;
        if (close < low || close > high)
        {
            return context.HasOpenPosition
                ? Detail(SignalType.Exit, "Close left the prior range.", candles, i)
                : Detail(SignalType.NoAction, "Breakout bar is not a range entry.", candles, i);
        }

        var pos = (close - low) / (high - low);
        if (context.HasOpenPosition)
        {
            if (TimeExpired(context, candles[i], p))
            {
                return Detail(SignalType.Exit, "Flat range maximum hold.", candles, i);
            }

            var mid = (high + low) / 2m;
            if (IsLong(context) && close >= mid && InProfit(context, close))
            {
                return Detail(SignalType.Exit, "Flat range reached the midpoint in profit.", candles, i);
            }

            if (!IsLong(context) && close <= mid && InProfit(context, close))
            {
                return Detail(SignalType.Exit, "Flat range reached the midpoint in profit.", candles, i);
            }

            return Detail(SignalType.Hold, "Flat range position is open.", candles, i);
        }

        if (pos <= 0.15m && candles[i].Close > candles[i].Open)
        {
            var stop = low - (0.25m * atrNow);
            var mid = (high + low) / 2m;
            var far = high;
            var take = StrategyExecutionRules.RewardMultiple(close, stop, far, 1.5m) ? far : mid;
            if (stop >= close || !StrategyExecutionRules.RewardMultiple(close, stop, take, 1.5m))
            {
                return Detail(SignalType.NoAction, "Flat long reward is below 1.5R.", candles, i);
            }

            return Detail(SignalType.Buy, "Rejection in the bottom 15% of the prior range.", candles, i, stop, take);
        }

        if (pos >= 0.85m && candles[i].Close < candles[i].Open)
        {
            var stop = high + (0.25m * atrNow);
            var mid = (high + low) / 2m;
            var far = low;
            var take = StrategyExecutionRules.RewardMultiple(close, stop, far, 1.5m) ? far : mid;
            if (stop <= close || !StrategyExecutionRules.RewardMultiple(close, stop, take, 1.5m))
            {
                return Detail(SignalType.NoAction, "Flat short reward is below 1.5R.", candles, i);
            }

            return Detail(SignalType.Sell, "Rejection in the top 15% of the prior range.", candles, i, stop, take);
        }

        return Detail(SignalType.NoAction, "Close is not a range-edge rejection.", candles, i);
    }

    private static StrategySignalDetail EmaRsi(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var regime = HtfBias(context, candles[i].CloseTime, p.TrendEmaPeriod);
        if (regime is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: higher-timeframe EMA is missing. No order.", candles, i);
        }

        if (i < p.EmaSlow + p.RsiPeriod)
        {
            return Detail(SignalType.NoAction, "EMA RSI warmup is incomplete.", candles, i);
        }

        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var rsi = cache.Rsi(p.RsiPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (fast[i] is not { } emaFast || slow[i] is not { } emaSlow || rsi[i] is not { } rsiNow || rsi[i - 1] is not { } rsiPrev || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "EMA RSI indicators are not ready.", candles, i);
        }

        var median = PriorMedian(candles, i, p.RelativeVolumePeriod, c => c.Volume);
        if (context.HasOpenPosition)
        {
            var crossed = IsLong(context) ? emaFast < emaSlow : emaFast > emaSlow;
            if (crossed)
            {
                return Detail(SignalType.Exit, "Opposite EMA cross. REVERSAL_DEFERRED.", candles, i);
            }

            var trail = IsLong(context) ? candles[i].Close - (p.AtrStopMultiplier * atrNow) : candles[i].Close + (p.AtrStopMultiplier * atrNow);
            return Detail(SignalType.Hold, "EMA RSI position is open.", candles, i, stop: trail);
        }

        if (median is not { } med || med <= 0m || !StrategyExecutionRules.VolumeUsable(candles[i].Volume) || candles[i].Volume < med * p.MinimumRelativeVolume)
        {
            return Detail(SignalType.NoAction, "EMA RSI volume is missing or below the median.", candles, i);
        }

        var longPullback = regime == true && emaFast > emaSlow && rsiPrev >= p.RsiOversold && rsiPrev <= p.RsiMinimum && rsiNow > p.RsiMinimum && candles[i].Close > emaFast;
        var shortPullback = regime == false && emaFast < emaSlow && rsiPrev <= 100m - p.RsiOversold && rsiPrev >= 100m - p.RsiMinimum && rsiNow < 100m - p.RsiMinimum && candles[i].Close < emaFast;
        if (longPullback)
        {
            var stop = Math.Max(MinLow(candles, i, p.SwingLength), candles[i].Close - (p.StopAtrMultiplier * atrNow));
            if (stop >= candles[i].Close)
            {
                stop = candles[i].Close - (p.StopAtrMultiplier * atrNow);
            }

            var take = candles[i].Close + (2m * (candles[i].Close - stop));
            return stop < candles[i].Close
                ? Detail(SignalType.Buy, "EMA RSI pullback reclaimed 50 and EMA20.", candles, i, stop, take)
                : Detail(SignalType.NoAction, "EMA RSI long stop is not valid.", candles, i);
        }

        if (shortPullback)
        {
            var stop = Math.Min(MaxHigh(candles, i, p.SwingLength), candles[i].Close + (p.StopAtrMultiplier * atrNow));
            if (stop <= candles[i].Close)
            {
                stop = candles[i].Close + (p.StopAtrMultiplier * atrNow);
            }

            var take = candles[i].Close - (2m * (stop - candles[i].Close));
            return stop > candles[i].Close
                ? Detail(SignalType.Sell, "EMA RSI pullback lost 50 and EMA20.", candles, i, stop, take)
                : Detail(SignalType.NoAction, "EMA RSI short stop is not valid.", candles, i);
        }

        return Detail(SignalType.NoAction, "EMA RSI entry is not matched.", candles, i);
    }

    private static StrategySignalDetail Bollinger(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < Math.Max(p.BbPeriod, p.TrendEmaPeriod) + 5)
        {
            return Detail(SignalType.NoAction, "Bollinger reversion warmup is incomplete.", candles, i);
        }

        var adx = cache.Adx(p.AdxPeriod);
        var slope = cache.EmaSlope(p.TrendEmaPeriod);
        var (mid, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var rsi = cache.Rsi(p.RsiPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (adx[i] is not { } adxNow || slope[i] is not { } slopeNow || lower[i] is not { } lo || lower[i - 1] is not { } prevLo
            || upper[i] is not { } up || upper[i - 1] is not { } prevUp || mid[i] is not { } middle
            || rsi[i] is not { } rsiNow || rsi[i - 1] is not { } rsiPrev || atr[i] is not { } atrNow || candles[i].Close <= 0m)
        {
            return Detail(SignalType.NoAction, "Bollinger reversion indicators are not ready.", candles, i);
        }

        var flat = adxNow < p.MinimumAdx && Math.Abs(slopeNow) / candles[i].Close <= p.PriceChangeThreshold;
        if (context.HasOpenPosition)
        {
            if (!flat)
            {
                return Detail(SignalType.Exit, "Range regime broke.", candles, i);
            }

            if (TimeExpired(context, candles[i], p))
            {
                return Detail(SignalType.Exit, "Bollinger reversion time stop.", candles, i);
            }

            if (IsLong(context) && candles[i].Close >= middle)
            {
                return Detail(SignalType.Exit, "Close reached the middle band.", candles, i);
            }

            if (!IsLong(context) && candles[i].Close <= middle)
            {
                return Detail(SignalType.Exit, "Close reached the middle band.", candles, i);
            }

            return Detail(SignalType.Hold, "Bollinger reversion position is open.", candles, i);
        }

        if (!flat)
        {
            return Detail(SignalType.NoAction, "Bollinger reversion is blocked outside a flat regime.", candles, i);
        }

        var longSetup = candles[i - 1].Close < prevLo && candles[i].Close > lo && rsiPrev < p.RsiOversold && rsiNow > rsiPrev && candles[i].Close > candles[i].Open;
        var shortSetup = candles[i - 1].Close > prevUp && candles[i].Close < up && rsiPrev > p.RsiOverbought && rsiNow < rsiPrev && candles[i].Close < candles[i].Open;
        if (!longSetup && !shortSetup)
        {
            return Detail(SignalType.NoAction, "Bollinger reversion confirmation is missing.", candles, i);
        }

        var side = longSetup ? SignalType.Buy : SignalType.Sell;
        var extreme = longSetup ? Math.Min(candles[i].Low, candles[i - 1].Low) : Math.Max(candles[i].High, candles[i - 1].High);
        var atrCap = longSetup ? candles[i].Close - (p.StopAtrMultiplier * atrNow) : candles[i].Close + (p.StopAtrMultiplier * atrNow);
        var stop = longSetup ? Math.Max(extreme, atrCap) : Math.Min(extreme, atrCap);
        if ((longSetup && stop >= candles[i].Close) || (!longSetup && stop <= candles[i].Close))
        {
            return Detail(SignalType.NoAction, "Bollinger stop is not valid.", candles, i);
        }

        if (!StrategyExecutionRules.PaysRoundTrip(candles[i].Close, middle))
        {
            return Detail(SignalType.NoAction, "Middle-band target does not clear round-trip cost.", candles, i);
        }

        return Detail(side, longSetup
            ? "Lower band reclaim in a flat regime."
            : "Upper band reclaim in a flat regime.", candles, i, stop, middle);
    }

    private static StrategySignalDetail BandEntry(
        SignalType side,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal extreme,
        decimal atr,
        StrategyTemplateParams p,
        string setup,
        decimal? preferredTake = null,
        decimal minR = 1.5m)
    {
        var close = candles[i].Close;
        var stop = side == SignalType.Buy
            ? Math.Min(extreme, close) - (p.SweepDepthAtr * atr)
            : Math.Max(extreme, close) + (p.SweepDepthAtr * atr);
        var atrStop = side == SignalType.Buy ? close - (p.StopAtrMultiplier * atr) : close + (p.StopAtrMultiplier * atr);
        if (side == SignalType.Buy)
        {
            stop = stop < close ? stop : atrStop;
            if (atrStop < close && close - atrStop < close - stop)
            {
                stop = atrStop;
            }
        }
        else if (stop > close)
        {
            if (atrStop > close && atrStop - close < stop - close)
            {
                stop = atrStop;
            }
        }
        else
        {
            stop = atrStop;
        }

        var risk = Math.Abs(close - stop);
        if (risk <= 0m || (side == SignalType.Buy && stop >= close) || (side == SignalType.Sell && stop <= close))
        {
            return Detail(SignalType.NoAction, "Stop distance is not valid.", candles, i);
        }

        var rTake = side == SignalType.Buy ? close + (minR * risk) : close - (minR * risk);
        var take = preferredTake is { } target && StrategyExecutionRules.RewardMultiple(close, stop, target, minR) ? target : rTake;
        if (!StrategyExecutionRules.PaysRoundTrip(close, take) || !StrategyExecutionRules.RewardMultiple(close, stop, take, minR))
        {
            return Detail(SignalType.NoAction, "Target does not clear cost and minimum R.", candles, i);
        }

        return Detail(side, $"Confirmed setup_type={setup}.", candles, i, stop, take);
    }

    private static bool? HtfBias(StrategyContext context, DateTimeOffset closeTime, int period)
    {
        var htf = context.HigherTimeframeCache;
        if (htf is null || htf.Candles.Count == 0)
        {
            return null;
        }

        var h = AlphaIndicatorSeries.LastCompletedHigherTimeframe(htf.Candles, closeTime);
        if (h < 0)
        {
            return null;
        }

        var ema = htf.Ema(period);
        if (ema[h] is not { } line || htf.Candles[h].Close <= 0m)
        {
            return null;
        }

        if (htf.Candles[h].Close > line)
        {
            return true;
        }

        if (htf.Candles[h].Close < line)
        {
            return false;
        }

        return null;
    }

    private static int FundingAge(IReadOnlyList<decimal?> funding, int i)
    {
        var age = 0;
        for (var k = i; k >= 0 && funding[k] is null; k--)
        {
            age++;
        }

        return age;
    }

    private static bool OiNotMissing(IReadOnlyList<decimal?> oi, int i, int barsBack)
    {
        var prior = i - barsBack;
        return prior >= 0 && oi[i] is > 0m && oi[prior] is > 0m;
    }

    private static bool FailedBreak(IReadOnlyList<MarketCandle> candles, int i, bool longSide)
    {
        if (i < 21)
        {
            return false;
        }

        var levelHigh = MaxHigh(candles, i - 1, 20);
        var levelLow = MinLow(candles, i - 1, 20);
        if (longSide)
        {
            var swept = false;
            for (var k = Math.Max(1, i - 3); k <= i; k++)
            {
                if (candles[k].High > levelHigh)
                {
                    swept = true;
                }
            }

            return swept && candles[i].Close < levelHigh && candles[i].Close < candles[i].Open;
        }

        var sweptDown = false;
        for (var k = Math.Max(1, i - 3); k <= i; k++)
        {
            if (candles[k].Low < levelLow)
            {
                sweptDown = true;
            }
        }

        return sweptDown && candles[i].Close > levelLow && candles[i].Close > candles[i].Open;
    }

    private static decimal? TakerShare(MarketCandle candle)
    {
        var imbalance = TakerFlow.Imbalance(candle);
        return imbalance is { } value ? (value + 1m) / 2m : null;
    }

    private static decimal Percentile(List<decimal> values, decimal p)
    {
        values.Sort();
        var rank = (values.Count - 1) * (double)p;
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi)
        {
            return values[lo];
        }

        var weight = (decimal)(rank - lo);
        return (values[lo] * (1m - weight)) + (values[hi] * weight);
    }

    private static bool TryRange(IReadOnlyList<MarketCandle> candles, int i, int lookback, out decimal low, out decimal high)
    {
        low = decimal.MaxValue;
        high = decimal.MinValue;
        if (i < lookback)
        {
            return false;
        }

        for (var k = i - lookback; k < i; k++)
        {
            low = Math.Min(low, candles[k].Low);
            high = Math.Max(high, candles[k].High);
        }

        return high > low && candles[i - 1].Close > 0m;
    }

    private static bool TimeExpired(StrategyContext context, MarketCandle bar, StrategyTemplateParams p)
    {
        if (context.PositionOpenedAt is not { } opened)
        {
            return false;
        }

        var minutes = p.Timeframe switch
        {
            "1m" => 1,
            "3m" => 3,
            "5m" => 5,
            "15m" => 15,
            "30m" => 30,
            "1h" => 60,
            "4h" => 240,
            "1d" => 1440,
            _ => 0
        };
        var bars = MaxHoldBars(p.TemplateKey);
        return minutes > 0 && bars > 0 && bar.CloseTime >= opened.AddMinutes(minutes * bars);
    }

    private static bool ReachedR(StrategyContext context, decimal close, decimal multiple)
    {
        if (context.AverageEntryPrice is not { } entry || context.ProtectiveStopPrice is not { } stop || entry <= 0m)
        {
            return false;
        }

        var risk = Math.Abs(entry - stop);
        if (risk <= 0m)
        {
            return false;
        }

        var reward = IsLong(context) ? close - entry : entry - close;
        return reward >= risk * multiple;
    }

    private static bool InProfit(StrategyContext context, decimal close)
    {
        if (context.AverageEntryPrice is not { } entry || entry <= 0m)
        {
            return false;
        }

        return IsLong(context) ? close > entry : close < entry;
    }

    private static decimal AtrStop(StrategyContext context, decimal close, decimal atr, decimal multiple) =>
        IsLong(context) ? close - (multiple * atr) : close + (multiple * atr);

    private static bool IsLong(StrategyContext context) => context.PositionSide != PositionSide.Short;

    private static bool IsBetween(decimal low, decimal high, decimal level) => low <= level && high >= level;

    private static decimal MinLow(IReadOnlyList<MarketCandle> candles, int i, int length)
    {
        var start = Math.Max(0, i - Math.Max(2, length) + 1);
        var low = candles[i].Low;
        for (var k = start; k <= i; k++)
        {
            low = Math.Min(low, candles[k].Low);
        }

        return low;
    }

    private static decimal MaxHigh(IReadOnlyList<MarketCandle> candles, int i, int length)
    {
        var start = Math.Max(0, i - Math.Max(2, length) + 1);
        var high = candles[i].High;
        for (var k = start; k <= i; k++)
        {
            high = Math.Max(high, candles[k].High);
        }

        return high;
    }

    private static decimal? PriorMean(IReadOnlyList<MarketCandle> candles, int i, int period, Func<MarketCandle, decimal> select)
    {
        if (i < period)
        {
            return null;
        }

        decimal sum = 0m;
        for (var k = i - period; k < i; k++)
        {
            sum += select(candles[k]);
        }

        return sum / period;
    }

    private static decimal? PriorMedian(IReadOnlyList<MarketCandle> candles, int i, int period, Func<MarketCandle, decimal> select)
    {
        if (i < period)
        {
            return null;
        }

        var values = new decimal[period];
        for (var k = 0; k < period; k++)
        {
            values[k] = select(candles[i - period + k]);
        }

        Array.Sort(values);
        var mid = period / 2;
        return period % 2 == 0 ? (values[mid - 1] + values[mid]) / 2m : values[mid];
    }

    private static decimal?[] Sma(IReadOnlyList<MarketCandle> candles, int period)
    {
        var result = new decimal?[candles.Count];
        decimal sum = 0m;
        for (var i = 0; i < candles.Count; i++)
        {
            sum += candles[i].Close;
            if (i >= period)
            {
                sum -= candles[i - period].Close;
            }

            if (i >= period - 1)
            {
                result[i] = sum / period;
            }
        }

        return result;
    }

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal? stop = null,
        decimal? take = null,
        IReadOnlyDictionary<string, decimal?>? snapshot = null) =>
        new(signal, reason, candles[i].CloseTime, stop, take, snapshot, StrategyExecutionRules.VersionStatus);
}
