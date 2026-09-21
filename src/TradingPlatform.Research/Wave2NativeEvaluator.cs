using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

/// <summary>
/// Wave-2 native hypotheses. Parameters are pre-registered and are not fit on OOS.
/// Indicators use only closed bars at index i (causal cache).
/// </summary>
public static class Wave2NativeEvaluator
{
    public const string SweepReclaim = "liquidity_sweep_reclaim";
    public const string FailedBreakoutVolume = "failed_breakout_volume";
    public const string SqueezeExpansion = "squeeze_expansion";
    public const string VolumeExhaustion = "volume_exhaustion";
    public const string VwapExtension = "vwap_reclaim_extension";
    public const string BosPullback = "structure_bos_pullback";
    public const string DisplacementRetrace = "displacement_retrace";
    public const string RegimeSwitch = "regime_switch_mr_bo";

    public static bool Handles(string? nativeKey) => nativeKey switch
    {
        SweepReclaim or FailedBreakoutVolume or SqueezeExpansion or VolumeExhaustion
            or VwapExtension or BosPullback or DisplacementRetrace or RegimeSwitch => true,
        _ => false
    };

    public static StrategySignalDetail Evaluate(
        ResearchCandidate candidate,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int i,
        StrategyContext context) =>
        HoldOpen(context, candles, i, out var held)
            ? held
            : candidate.NativeKey switch
            {
                SweepReclaim => Sweep(candles, cache, i),
                FailedBreakoutVolume => FailedBo(candles, cache, i),
                SqueezeExpansion => Squeeze(candles, cache, i),
                VolumeExhaustion => Exhaustion(candles, cache, i),
                VwapExtension => VwapExt(candles, cache, i),
                BosPullback => BosPb(candles, cache, i),
                DisplacementRetrace => Displacement(candles, cache, i),
                RegimeSwitch => Regime(candles, cache, i),
                _ => Detail(SignalType.NoAction, "Unknown Wave-2 native key.", candles, i)
            };

    private static StrategySignalDetail Sweep(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        const int swingN = 3;
        var swingLo = cache.ConfirmedSwingLow(swingN);
        var swingHi = cache.ConfirmedSwingHigh(swingN);
        var atr = cache.Atr(14);
        var rel = cache.RelativeVolume(20);
        if (swingLo[i] is not { } lo || swingHi[i] is not { } hi || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Sweep swings/ATR not ready.", candles, i);
        }

        var bar = candles[i];
        var volOk = rel[i] is { } rv && rv >= 1.2m;
        if (volOk && bar.Low < lo && bar.Close > lo && bar.Close > bar.Open)
        {
            return Directed(SignalType.Buy, "Liquidity sweep of confirmed swing low, close reclaimed inside, volume.", candles, i, bar.Low - atrNow * 0.10m);
        }

        if (volOk && bar.High > hi && bar.Close < hi && bar.Close < bar.Open)
        {
            return Directed(SignalType.Sell, "Liquidity sweep of confirmed swing high, close reclaimed inside, volume.", candles, i, bar.High + atrNow * 0.10m);
        }

        return Detail(SignalType.NoAction, "No sweep-reclaim.", candles, i);
    }

    private static StrategySignalDetail FailedBo(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        if (i < 2)
        {
            return Detail(SignalType.NoAction, "Need prior breakout bar.", candles, i);
        }

        var (dcHigh, dcLow) = cache.Donchian(20);
        var rel = cache.RelativeVolume(20);
        var atr = cache.Atr(14);
        if (dcHigh[i - 1] is not { } prevHigh || dcLow[i - 1] is not { } prevLow || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Donchian/ATR not ready.", candles, i);
        }

        var prev = candles[i - 1];
        var bar = candles[i];
        var breakVol = rel[i - 1] is { } rvBreak && rvBreak >= 1.2m;
        var failVol = rel[i] is { } rvFail && rvFail < 1.0m;
        if (!breakVol || !failVol)
        {
            return Detail(SignalType.NoAction, "Break/fail volume pattern not matched.", candles, i);
        }

        if (prev.Close > prevHigh && bar.Close < prevHigh)
        {
            return Directed(SignalType.Sell, "Upside breakout failed back inside with volume die-off.", candles, i, Math.Max(prev.High, bar.High) + atrNow * 0.10m);
        }

        if (prev.Close < prevLow && bar.Close > prevLow)
        {
            return Directed(SignalType.Buy, "Downside breakout failed back inside with volume die-off.", candles, i, Math.Min(prev.Low, bar.Low) - atrNow * 0.10m);
        }

        return Detail(SignalType.NoAction, "No failed-breakout reclaim.", candles, i);
    }

    private static StrategySignalDetail Squeeze(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        if (i < 2)
        {
            return Detail(SignalType.NoAction, "Need prior compression bar.", candles, i);
        }

        var atrPct = cache.AtrPercentile(14, 50);
        var atr = cache.Atr(14);
        var (dcHigh, dcLow) = cache.Donchian(20);
        var rel = cache.RelativeVolume(20);
        if (atrPct[i - 1] is not { } prevPct || atr[i] is not { } atrNow || atr[i - 1] is not { } prevAtr
            || dcHigh[i] is not { } res || dcLow[i] is not { } sup)
        {
            return Detail(SignalType.NoAction, "Squeeze ATR/Donchian not ready.", candles, i);
        }

        var compressed = prevPct <= 0.25m;
        var expanding = atrNow > prevAtr;
        var volOk = rel[i] is { } rv && rv >= 1.2m;
        var bar = candles[i];
        if (compressed && expanding && volOk && bar.Close > res && candles[i - 1].Close <= (dcHigh[i - 1] ?? res))
        {
            return Directed(SignalType.Buy, "ATR compression then expansion broke Donchian high with volume.", candles, i, bar.Low - atrNow * 0.10m);
        }

        if (compressed && expanding && volOk && bar.Close < sup && candles[i - 1].Close >= (dcLow[i - 1] ?? sup))
        {
            return Directed(SignalType.Sell, "ATR compression then expansion broke Donchian low with volume.", candles, i, bar.High + atrNow * 0.10m);
        }

        return Detail(SignalType.NoAction, "No squeeze-expansion break.", candles, i);
    }

    private static StrategySignalDetail Exhaustion(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        if (i < 3)
        {
            return Detail(SignalType.NoAction, "Need 3 prior bars.", candles, i);
        }

        var rel = cache.RelativeVolume(20);
        var atr = cache.Atr(14);
        if (rel[i] is not { } rv || rv < 2.0m || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "No exhaustion volume spike.", candles, i);
        }

        var bar = candles[i];
        var range = bar.High - bar.Low;
        if (range <= 0m)
        {
            return Detail(SignalType.NoAction, "Zero-range bar.", candles, i);
        }

        var body = Math.Abs(bar.Close - bar.Open);
        if (body / range > 0.35m)
        {
            return Detail(SignalType.NoAction, "Body too large for exhaustion.", candles, i);
        }

        var downRun = candles[i - 1].Close < candles[i - 2].Close && candles[i - 2].Close < candles[i - 3].Close;
        var upRun = candles[i - 1].Close > candles[i - 2].Close && candles[i - 2].Close > candles[i - 3].Close;
        var closeUpper = bar.Close >= bar.Low + range * 0.50m;
        var closeLower = bar.Close <= bar.High - range * 0.50m;
        if (downRun && closeUpper)
        {
            return Directed(SignalType.Buy, "Sell-off then high-volume small-body exhaustion.", candles, i, bar.Low - atrNow * 0.10m);
        }

        if (upRun && closeLower)
        {
            return Directed(SignalType.Sell, "Rally then high-volume small-body exhaustion.", candles, i, bar.High + atrNow * 0.10m);
        }

        return Detail(SignalType.NoAction, "Exhaustion location not matched.", candles, i);
    }

    private static StrategySignalDetail VwapExt(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        var vwap = cache.SessionVwap();
        var atr = cache.Atr(14);
        var rel = cache.RelativeVolume(20);
        if (vwap[i] is not { } vw || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "VWAP/ATR not ready.", candles, i);
        }

        var bar = candles[i];
        var prev = candles[i - 1].Close;
        var volOk = rel[i] is { } rv && rv >= 1.0m;
        if (volOk && prev <= vw - atrNow * 0.75m && bar.Close > vw)
        {
            return Directed(SignalType.Buy, "Close reclaimed session VWAP after ≥0.75 ATR extension.", candles, i, Math.Min(bar.Low, prev) - atrNow * 0.10m, vw + atrNow * 0.50m);
        }

        if (volOk && prev >= vw + atrNow * 0.75m && bar.Close < vw)
        {
            return Directed(SignalType.Sell, "Close lost session VWAP after ≥0.75 ATR extension.", candles, i, Math.Max(bar.High, prev) + atrNow * 0.10m, vw - atrNow * 0.50m);
        }

        return Detail(SignalType.NoAction, "No VWAP extension reclaim.", candles, i);
    }

    private static StrategySignalDetail BosPb(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        const int swingN = 3;
        const int lookback = 8;
        if (i < lookback + 1)
        {
            return Detail(SignalType.NoAction, "Need BOS lookback.", candles, i);
        }

        var swingLo = cache.ConfirmedSwingLow(swingN);
        var swingHi = cache.ConfirmedSwingHigh(swingN);
        var atr = cache.Atr(14);
        if (atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "ATR not ready.", candles, i);
        }

        var bar = candles[i];
        var bosUp = false;
        var bosDn = false;
        decimal? brokenHigh = null;
        decimal? brokenLow = null;
        for (var j = i - lookback; j < i; j++)
        {
            if (j < 1)
            {
                continue;
            }

            if (swingHi[j] is { } sh && candles[j - 1].Close <= sh && candles[j].Close > sh)
            {
                bosUp = true;
                brokenHigh = sh;
            }

            if (swingLo[j] is { } sl && candles[j - 1].Close >= sl && candles[j].Close < sl)
            {
                bosDn = true;
                brokenLow = sl;
            }
        }

        if (bosUp && brokenHigh is { } bh && bar.Low <= bh + atrNow * 0.15m && bar.Close > bh && bar.Close > bar.Open)
        {
            return Directed(SignalType.Buy, "Bullish BOS then pullback hold of broken swing.", candles, i, bar.Low - atrNow * 0.10m);
        }

        if (bosDn && brokenLow is { } bl && bar.High >= bl - atrNow * 0.15m && bar.Close < bl && bar.Close < bar.Open)
        {
            return Directed(SignalType.Sell, "Bearish BOS then pullback hold of broken swing.", candles, i, bar.High + atrNow * 0.10m);
        }

        return Detail(SignalType.NoAction, "No BOS pullback hold.", candles, i);
    }

    private static StrategySignalDetail Displacement(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        const int lookback = 6;
        if (i < lookback)
        {
            return Detail(SignalType.NoAction, "Need displacement lookback.", candles, i);
        }

        var atr = cache.Atr(14);
        if (atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "ATR not ready.", candles, i);
        }

        var bar = candles[i];
        for (var j = i - lookback; j < i; j++)
        {
            if (atr[j] is not { } a || a <= 0m)
            {
                continue;
            }

            var disp = candles[j];
            var range = disp.High - disp.Low;
            if (range < a * 1.5m)
            {
                continue;
            }

            var mid = disp.Low + range * 0.50m;
            var up = disp.Close > disp.Open;
            if (up && bar.Low <= mid && bar.Close > mid && bar.Close > bar.Open)
            {
                return Directed(SignalType.Buy, "Retrace into bullish displacement midpoint.", candles, i, disp.Low - atrNow * 0.10m, disp.High + atrNow * 0.50m);
            }

            if (!up && bar.High >= mid && bar.Close < mid && bar.Close < bar.Open)
            {
                return Directed(SignalType.Sell, "Retrace into bearish displacement midpoint.", candles, i, disp.High + atrNow * 0.10m, disp.Low - atrNow * 0.50m);
            }
        }

        return Detail(SignalType.NoAction, "No displacement retrace.", candles, i);
    }

    private static StrategySignalDetail Regime(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        var atrPct = cache.AtrPercentile(14, 50);
        var (dcHigh, dcLow) = cache.Donchian(20);
        var rel = cache.RelativeVolume(20);
        var atr = cache.Atr(14);
        if (atrPct[i] is not { } pct || dcHigh[i] is not { } res || dcLow[i] is not { } sup || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Regime ATR/Donchian not ready.", candles, i);
        }

        var bar = candles[i];
        var volOk = rel[i] is { } rv && rv >= 1.2m;
        if (pct <= 0.35m)
        {
            if (bar.Low <= sup && bar.Close > sup && bar.Close > bar.Open)
            {
                return Directed(SignalType.Buy, "Low-vol regime: Donchian low rejection (fade).", candles, i, bar.Low - atrNow * 0.10m);
            }

            if (bar.High >= res && bar.Close < res && bar.Close < bar.Open)
            {
                return Directed(SignalType.Sell, "Low-vol regime: Donchian high rejection (fade).", candles, i, bar.High + atrNow * 0.10m);
            }
        }
        else if (pct >= 0.65m && volOk)
        {
            if (bar.Close > res && i > 0 && candles[i - 1].Close <= (dcHigh[i - 1] ?? res))
            {
                return Directed(SignalType.Buy, "High-vol regime: Donchian break up with volume.", candles, i, bar.Low - atrNow * 0.10m);
            }

            if (bar.Close < sup && i > 0 && candles[i - 1].Close >= (dcLow[i - 1] ?? sup))
            {
                return Directed(SignalType.Sell, "High-vol regime: Donchian break down with volume.", candles, i, bar.High + atrNow * 0.10m);
            }
        }

        return Detail(SignalType.NoAction, "Mid-vol regime or no Donchian event.", candles, i);
    }

    private static bool HoldOpen(StrategyContext context, IReadOnlyList<MarketCandle> candles, int i, out StrategySignalDetail held)
    {
        if (!context.HasOpenPosition)
        {
            held = Detail(SignalType.NoAction, "", candles, i);
            return false;
        }

        held = Detail(SignalType.Hold, "Position open; Isolated book owns stop/take-profit.", candles, i);
        return true;
    }

    private static StrategySignalDetail Directed(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal stop,
        decimal? tp = null)
    {
        var close = candles[i].Close;
        var dist = Math.Abs(close - stop);
        if (dist <= 0m)
        {
            return Detail(SignalType.NoAction, "Zero stop distance.", candles, i);
        }

        var resolvedTp = tp ?? (signal == SignalType.Buy ? close + dist * 2m : close - dist * 2m);
        return Detail(signal, reason, candles, i, stop, resolvedTp);
    }

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal? stop = null,
        decimal? tp = null) =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime, stop, tp);
}
