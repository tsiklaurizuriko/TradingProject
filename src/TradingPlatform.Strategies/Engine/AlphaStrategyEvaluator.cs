using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

public static class AlphaStrategyEvaluator
{
    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        p.TemplateKey switch
        {
            StrategyTemplateKeys.VpVwapReversion => VpVwap(p, candles, i, context, cache),
            StrategyTemplateKeys.LiqSweepReversal => SweepReversal(p, candles, i, context, cache),
            StrategyTemplateKeys.LiqSweepContinuation => SweepContinuation(p, candles, i, context, cache),
            _ when FuturesAlphaEvaluator.Handles(p.TemplateKey) => FuturesAlphaEvaluator.Evaluate(p, candles, i, context, cache),
            StrategyTemplateKeys.TakerFlowMomentum => TakerFlow(p, candles, i, context, cache),
            StrategyTemplateKeys.VwapDeviationReversion => VwapDeviation(p, candles, i, context, cache),
            StrategyTemplateKeys.VwapBreakoutVolume => VwapBreakout(p, candles, i, context, cache),
            StrategyTemplateKeys.FailedBreakoutReversal => FailedBreakout(p, candles, i, context, cache),
            StrategyTemplateKeys.VolSqueezeStructure => SqueezeBreak(p, candles, i, context, cache),
            StrategyTemplateKeys.MarketStructureTrend => StructureTrend(p, candles, i, context, cache),
            StrategyTemplateKeys.MarketStructurePullback => StructurePullback(p, candles, i, context, cache),
            StrategyTemplateKeys.AtrNormalizedMomentum => AtrMomentum(p, candles, i, context, cache),
            StrategyTemplateKeys.MtfTrendStructure => Mtf(p, candles, i, context, cache),
            StrategyTemplateKeys.ZscoreMeanReversion => ZScore(p, candles, i, context, cache),
            StrategyTemplateKeys.CryptoPairsArb => Unavailable(candles, i, "Causal pair universe and hedge-ratio windows are not wired into single-book replay."),
            StrategyTemplateKeys.XsRelativeStrength => Unavailable(candles, i, "Cross-sectional ranks require a timestamp-aligned universe snapshot; not fabricated."),
            StrategyTemplateKeys.RegimeStrategyRouter => Detail(
                SignalType.NoAction,
                "Regime router is deferred until independent candidates are validated. Routing rules will not be fit on OOS.",
                candles,
                i,
                status: "RESEARCHING"),
            _ => Detail(SignalType.NoAction, "Unknown alpha template.", candles, i, status: "IMPLEMENTATION_ERROR")
        };

    private static StrategySignalDetail VpVwap(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var window = Math.Max(16, p.EntryLookback * 2);
        var (poc, vah, val) = cache.VolumeProfile(window, p.ValueAreaPercent);
        var vwap = cache.SessionVwap();
        var atr = cache.Atr(p.AtrPeriod);
        var adx = cache.Adx(p.AdxPeriod);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (val[i] is not { } valNow || vah[i] is not { } vahNow || poc[i] is not { } pocNow
            || vwap[i] is not { } vw || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Volume profile not ready.", candles, i);
        }

        if (adx[i] is { } adxNow && adxNow >= p.MinimumAdx)
        {
            return Detail(SignalType.NoAction, "VAL/VAH reversion skipped in strong-trend ADX regime.", candles, i);
        }

        var bar = candles[i];
        var volOk = !p.VolumeFilterEnabled || rel[i] is { } rv && rv >= p.MinimumRelativeVolume;
        if (bar.Low < valNow && bar.Close > valNow && bar.Close < vw && volOk)
        {
            return Detail(SignalType.Buy, "VAL rejection reclaim toward VWAP/POC.", candles, i, stop: bar.Low - atrNow * p.SweepDepthAtr, tp: pocNow);
        }

        if (bar.High > vahNow && bar.Close < vahNow && bar.Close > vw && volOk)
        {
            return Detail(SignalType.Sell, "VAH rejection reclaim toward VWAP/POC.", candles, i, stop: bar.High + atrNow * p.SweepDepthAtr, tp: pocNow);
        }

        return Detail(SignalType.NoAction, "No VAL/VAH rejection.", candles, i);
    }

    private static StrategySignalDetail SweepReversal(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var n = p.SwingLength;
        var swingLo = cache.ConfirmedSwingLow(n);
        var swingHi = cache.ConfirmedSwingHigh(n);
        var atr = cache.Atr(p.AtrPeriod);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (i < n * 2 + 1 || swingLo[i] is not { } lo || swingHi[i] is not { } hi || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Causal swing not confirmed yet.", candles, i);
        }

        var bar = candles[i];
        var depth = p.SweepDepthAtr * atrNow;
        var volOk = !p.VolumeFilterEnabled || rel[i] is { } rv && rv >= p.MinimumRelativeVolume;
        if (bar.Low <= lo - depth && bar.Close > lo && volOk)
        {
            return Detail(SignalType.Buy, "Liquidity sweep of confirmed swing low reversed.", candles, i, stop: bar.Low - atrNow * 0.25m);
        }

        if (bar.High >= hi + depth && bar.Close < hi && volOk)
        {
            return Detail(SignalType.Sell, "Liquidity sweep of confirmed swing high reversed.", candles, i, stop: bar.High + atrNow * 0.25m);
        }

        return Detail(SignalType.NoAction, "No causal liquidity-sweep reversal.", candles, i);
    }

    private static StrategySignalDetail SweepContinuation(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var n = p.SwingLength;
        var swingLo = cache.ConfirmedSwingLow(n);
        var swingHi = cache.ConfirmedSwingHigh(n);
        var ema = cache.Ema(p.EmaSlow);
        var atr = cache.Atr(p.AtrPeriod);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (swingLo[i] is not { } lo || swingHi[i] is not { } hi || ema[i] is not { } trend || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Causal swing not confirmed yet.", candles, i);
        }

        var bar = candles[i];
        var volOk = rel[i] is { } rv && rv >= Math.Max(p.BreakoutRelativeVolume, p.MinimumRelativeVolume);
        if (bar.High > hi && bar.Close > hi && bar.Close > trend && volOk)
        {
            return Detail(SignalType.Buy, "Sweep of confirmed high held as breakout continuation.", candles, i, stop: hi - atrNow * p.StopAtrMultiplier);
        }

        if (bar.Low < lo && bar.Close < lo && bar.Close < trend && volOk)
        {
            return Detail(SignalType.Sell, "Sweep of confirmed low held as breakdown continuation.", candles, i, stop: lo + atrNow * p.StopAtrMultiplier);
        }

        return Detail(SignalType.NoAction, "No sweep-and-hold continuation.", candles, i);
    }

    private static StrategySignalDetail TakerFlow(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (!AlphaIndicatorSeries.HasTakerData(candles))
        {
            return Unavailable(candles, i, "Taker buy volume is missing on this series (old cache or not parsed).");
        }

        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var imb = cache.TakerImbalance();
        var ema = cache.Ema(p.EmaSlow);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (imb[i] is not { } flow || ema[i] is not { } trend)
        {
            return Detail(SignalType.NoAction, "Taker imbalance not ready.", candles, i);
        }

        var volOk = rel[i] is { } rv && rv >= p.MinimumRelativeVolume;
        var persist = i > 0 && imb[i - 1] is { } prev && Math.Sign(prev) == Math.Sign(flow);
        if (flow > 0.15m && persist && candles[i].Close > trend && volOk)
        {
            return Detail(SignalType.Buy, "Persistent positive taker imbalance with price confirmation.", candles, i);
        }

        if (flow < -0.15m && persist && candles[i].Close < trend && volOk)
        {
            return Detail(SignalType.Sell, "Persistent negative taker imbalance with price confirmation.", candles, i);
        }

        return Detail(SignalType.NoAction, "Taker flow did not persist with price.", candles, i);
    }

    private static StrategySignalDetail VwapDeviation(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var vwap = cache.SessionVwap();
        var atr = cache.Atr(p.AtrPeriod);
        var adx = cache.Adx(p.AdxPeriod);
        if (vwap[i] is not { } vw || atr[i] is not { } atrNow || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "VWAP/ATR not ready.", candles, i);
        }

        if (adx[i] is { } adxNow && adxNow >= p.MinimumAdx)
        {
            return Detail(SignalType.NoAction, "VWAP deviation skipped in strong-trend ADX regime.", candles, i);
        }

        var dist = Math.Abs(candles[i].Close - vw) / atrNow;
        var bar = candles[i];
        if (dist < p.MaxVwapDistanceAtr)
        {
            return Detail(SignalType.NoAction, "VWAP distance below ATR threshold.", candles, i);
        }

        if (bar.Close < vw && bar.Close > bar.Open)
        {
            return Detail(SignalType.Buy, "Extreme negative VWAP deviation with rejection.", candles, i, stop: bar.Low - atrNow * p.SweepDepthAtr);
        }

        if (bar.Close > vw && bar.Close < bar.Open)
        {
            return Detail(SignalType.Sell, "Extreme positive VWAP deviation with rejection.", candles, i, stop: bar.High + atrNow * p.SweepDepthAtr);
        }

        return Detail(SignalType.NoAction, "VWAP deviation without rejection.", candles, i);
    }

    private static StrategySignalDetail VwapBreakout(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var vwap = cache.SessionVwap();
        var slope = cache.EmaSlope(p.EmaFast);
        var (dcHigh, dcLow) = cache.Donchian(p.EntryLookback);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (vwap[i] is not { } vw || slope[i] is not { } sl || dcHigh[i] is not { } res || dcLow[i] is not { } sup)
        {
            return Detail(SignalType.NoAction, "VWAP breakout indicators not ready.", candles, i);
        }

        var volOk = rel[i] is { } rv && rv >= p.BreakoutRelativeVolume;
        var close = candles[i].Close;
        var longTrans = i > 0 && candles[i - 1].Close <= (dcHigh[i - 1] ?? res);
        var shortTrans = i > 0 && candles[i - 1].Close >= (dcLow[i - 1] ?? sup);
        if (close > vw && sl > 0m && close > res && volOk && longTrans)
        {
            return Detail(SignalType.Buy, "VWAP-aligned break of local resistance with volume.", candles, i);
        }

        if (close < vw && sl < 0m && close < sup && volOk && shortTrans)
        {
            return Detail(SignalType.Sell, "VWAP-aligned break of local support with volume.", candles, i);
        }

        return Detail(SignalType.NoAction, "No VWAP breakout transition.", candles, i);
    }

    private static StrategySignalDetail FailedBreakout(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        if (i < 2)
        {
            return Detail(SignalType.NoAction, "Need prior breakout bar.", candles, i);
        }

        var (dcHigh, dcLow) = cache.Donchian(p.EntryLookback);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (dcHigh[i - 1] is not { } prevHigh || dcLow[i - 1] is not { } prevLow)
        {
            return Detail(SignalType.NoAction, "Donchian not ready.", candles, i);
        }

        var prev = candles[i - 1];
        var bar = candles[i];
        var volOk = !p.VolumeFilterEnabled || rel[i] is { } rv && rv >= p.MinimumRelativeVolume;
        if (prev.Close > prevHigh && bar.Close < prevHigh && volOk)
        {
            return Detail(SignalType.Sell, "Failed breakout: close back inside prior resistance.", candles, i, stop: prev.High);
        }

        if (prev.Close < prevLow && bar.Close > prevLow && volOk)
        {
            return Detail(SignalType.Buy, "Failed breakdown: close back inside prior support.", candles, i, stop: prev.Low);
        }

        return Detail(SignalType.NoAction, "No failed breakout reclaim.", candles, i);
    }

    private static StrategySignalDetail SqueezeBreak(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var bbwPct = cache.BollingerWidthPercentile(p.BbPeriod, p.BbStdDev, p.VolatilityLookback);
        var (_, kcUpper, kcLower) = cache.Keltner(p.BbPeriod, 1.5m);
        var (_, bbUpper, bbLower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var atr = cache.Atr(p.AtrPeriod);
        var atrSma = cache.AtrSma(p.AtrPeriod, p.AtrExpansionLookback);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        var (dcHigh, dcLow) = cache.Donchian(p.EntryLookback);
        if (bbwPct[i] is not { } pct || atr[i] is not { } atrNow || atrSma[i] is not { } atrMean
            || dcHigh[i] is not { } res || dcLow[i] is not { } sup
            || bbUpper[i] is not { } bu || bbLower[i] is not { } bl
            || kcUpper[i] is not { } ku || kcLower[i] is not { } kl)
        {
            return Detail(SignalType.NoAction, "Squeeze indicators not ready.", candles, i);
        }

        var compressed = pct <= p.CompressionPercentile || (bu <= ku && bl >= kl);
        var expanding = atrNow > atrMean;
        var volOk = rel[i] is { } rv && rv >= p.BreakoutRelativeVolume;
        var transUp = i > 0 && candles[i - 1].Close <= (dcHigh[i - 1] ?? res);
        var transDn = i > 0 && candles[i - 1].Close >= (dcLow[i - 1] ?? sup);
        if (compressed && expanding && volOk && candles[i].Close > res && transUp)
        {
            return Detail(SignalType.Buy, "Squeeze expansion broke structure up with volume.", candles, i, stop: candles[i].Close - atrNow * p.StopAtrMultiplier);
        }

        if (compressed && expanding && volOk && candles[i].Close < sup && transDn)
        {
            return Detail(SignalType.Sell, "Squeeze expansion broke structure down with volume.", candles, i, stop: candles[i].Close + atrNow * p.StopAtrMultiplier);
        }

        return Detail(SignalType.NoAction, "No squeeze-to-break transition.", candles, i);
    }

    private static StrategySignalDetail StructureTrend(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var n = p.SwingLength;
        var bias = cache.StructureBias(n);
        var hi = cache.ConfirmedSwingHigh(n);
        var lo = cache.ConfirmedSwingLow(n);
        if (bias[i] is not { } b || hi[i] is not { } lastHi || lo[i] is not { } lastLo)
        {
            return Detail(SignalType.NoAction, "Causal market structure not formed.", candles, i);
        }

        var close = candles[i].Close;
        var transUp = i > 0 && candles[i - 1].Close <= (hi[i - 1] ?? lastHi);
        var transDn = i > 0 && candles[i - 1].Close >= (lo[i - 1] ?? lastLo);
        if (b > 0m && close > lastHi && transUp)
        {
            return Detail(SignalType.Buy, "Bullish HH/HL structure confirmed a new higher high.", candles, i, stop: lastLo);
        }

        if (b < 0m && close < lastLo && transDn)
        {
            return Detail(SignalType.Sell, "Bearish LH/LL structure confirmed a new lower low.", candles, i, stop: lastHi);
        }

        return Detail(SignalType.NoAction, "No new structure continuation.", candles, i);
    }

    private static StrategySignalDetail StructurePullback(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var n = p.SwingLength;
        var bias = cache.StructureBias(n);
        var ema = cache.Ema(p.EmaFast);
        var vwap = cache.SessionVwap();
        var atr = cache.Atr(p.AtrPeriod);
        var lo = cache.ConfirmedSwingLow(n);
        var hi = cache.ConfirmedSwingHigh(n);
        if (bias[i] is not { } b || ema[i] is not { } e || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Structure pullback indicators not ready.", candles, i);
        }

        var bar = candles[i];
        var nearEma = Math.Abs(bar.Close - e) <= atrNow * 0.75m;
        var nearVwap = vwap[i] is { } vw && Math.Abs(bar.Close - vw) <= atrNow * 0.75m;
        if (b > 0m && (nearEma || nearVwap) && bar.Close > bar.Open && (lo[i] is not { } lastLo || bar.Low >= lastLo))
        {
            return Detail(SignalType.Buy, "Bullish structure pullback to EMA/VWAP with confirmation.", candles, i, stop: bar.Low - atrNow * p.SweepDepthAtr);
        }

        if (b < 0m && (nearEma || nearVwap) && bar.Close < bar.Open && (hi[i] is not { } lastHi || bar.High <= lastHi))
        {
            return Detail(SignalType.Sell, "Bearish structure pullback to EMA/VWAP with confirmation.", candles, i, stop: bar.High + atrNow * p.SweepDepthAtr);
        }

        return Detail(SignalType.NoAction, "No structure pullback confirmation.", candles, i);
    }

    private static StrategySignalDetail AtrMomentum(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var n = Math.Max(5, p.EntryLookback);
        var atr = cache.Atr(p.AtrPeriod);
        var ema = cache.Ema(p.TrendEmaPeriod);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (i < n || atr[i] is not { } atrNow || atrNow <= 0m || ema[i] is not { } trend)
        {
            return Detail(SignalType.NoAction, "ATR momentum not ready.", candles, i);
        }

        var mom = (candles[i].Close - candles[i - n].Close) / atrNow;
        var volOk = !p.VolumeFilterEnabled || rel[i] is { } rv && rv >= p.MinimumRelativeVolume;
        var trans = i > n && atr[i - 1] is { } prevAtr && prevAtr > 0m
            && Math.Abs((candles[i - 1].Close - candles[i - 1 - n].Close) / prevAtr) < 1m;
        if (mom >= 1m && candles[i].Close > trend && volOk && trans)
        {
            return Detail(SignalType.Buy, "ATR-normalized momentum turned positive with trend.", candles, i, stop: candles[i].Close - atrNow * p.StopAtrMultiplier);
        }

        if (mom <= -1m && candles[i].Close < trend && volOk && trans)
        {
            return Detail(SignalType.Sell, "ATR-normalized momentum turned negative with trend.", candles, i, stop: candles[i].Close + atrNow * p.StopAtrMultiplier);
        }

        return Detail(SignalType.NoAction, "ATR momentum below threshold or already persistent.", candles, i);
    }

    private static StrategySignalDetail Mtf(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var htf = context.HigherTimeframeCache;
        if (htf is null || htf.Candles.Count == 0)
        {
            return Detail(SignalType.NoAction, "HTF series not provided; only completed higher-timeframe candles may be used.", candles, i);
        }

        var h = AlphaIndicatorSeries.LastCompletedHigherTimeframe(htf.Candles, candles[i].CloseTime);
        if (h < 0)
        {
            return Detail(SignalType.NoAction, "No completed HTF candle at or before this close.", candles, i);
        }

        var htfEma20 = htf.Ema(20);
        var htfEma50 = htf.Ema(50);
        if (htfEma20[h] is not { } e20 || htfEma50[h] is not { } e50)
        {
            return Detail(SignalType.NoAction, "HTF EMAs not ready.", candles, i);
        }

        var htfBull = htf.Candles[h].Close > e20 && e20 > e50;
        var htfBear = htf.Candles[h].Close < e20 && e20 < e50;
        var ltfBias = cache.StructureBias(p.SwingLength);
        var vwap = cache.SessionVwap();
        var ema = cache.Ema(p.EmaFast);
        var atr = cache.Atr(p.AtrPeriod);
        if (ltfBias[i] is not { } bias || ema[i] is not { } e || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "LTF structure not ready.", candles, i);
        }

        var bar = candles[i];
        var near = Math.Abs(bar.Close - e) <= atrNow || (vwap[i] is { } vw && Math.Abs(bar.Close - vw) <= atrNow);
        if (htfBull && bias >= 0m && near && bar.Close > bar.Open)
        {
            return Detail(SignalType.Buy, "HTF bullish (last completed) with LTF pullback confirmation.", candles, i, stop: bar.Low - atrNow * p.SweepDepthAtr);
        }

        if (htfBear && bias <= 0m && near && bar.Close < bar.Open)
        {
            return Detail(SignalType.Sell, "HTF bearish (last completed) with LTF pullback confirmation.", candles, i, stop: bar.High + atrNow * p.SweepDepthAtr);
        }

        return Detail(SignalType.NoAction, "HTF/LTF alignment not matched.", candles, i);
    }

    private static StrategySignalDetail ZScore(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (HoldOpen(context, candles, i, out var held))
        {
            return held;
        }

        var z = cache.CloseZScore(Math.Max(20, p.EntryLookback));
        var adx = cache.Adx(p.AdxPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        if (z[i] is not { } zNow || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Z-score not ready.", candles, i);
        }

        if (adx[i] is { } adxNow && adxNow >= p.MinimumAdx)
        {
            return Detail(SignalType.NoAction, "Z-score mean reversion skipped in strong-trend ADX regime.", candles, i);
        }

        var entry = p.ZScoreEntry <= 0m ? 2m : p.ZScoreEntry;
        if (zNow <= -entry)
        {
            return Detail(SignalType.Buy, "Close Z-score below entry threshold (range regime).", candles, i, stop: candles[i].Close - atrNow * p.StopAtrMultiplier);
        }

        if (zNow >= entry)
        {
            return Detail(SignalType.Sell, "Close Z-score above entry threshold (range regime).", candles, i, stop: candles[i].Close + atrNow * p.StopAtrMultiplier);
        }

        return Detail(SignalType.NoAction, "Z-score inside band.", candles, i);
    }

    private static bool HoldOpen(StrategyContext context, IReadOnlyList<MarketCandle> candles, int i, out StrategySignalDetail held)
    {
        if (!context.HasOpenPosition)
        {
            held = Detail(SignalType.NoAction, "", candles, i);
            return false;
        }

        held = Detail(SignalType.Hold, "Position open; Risk Engine owns stop/take-profit.", candles, i);
        return true;
    }

    private static StrategySignalDetail Unavailable(IReadOnlyList<MarketCandle> candles, int i, string why) =>
        Detail(SignalType.NoAction, "DATA_UNAVAILABLE: " + why, candles, i, status: "DATA_UNAVAILABLE");

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal? stop = null,
        decimal? tp = null,
        string status = "RESEARCHING") =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime, stop, tp, null, status);
}
