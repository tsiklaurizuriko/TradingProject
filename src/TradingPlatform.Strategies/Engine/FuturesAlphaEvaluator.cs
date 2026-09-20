using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Phase 3 futures-alpha evaluators. Continuation and contrarian are independent hypotheses.
/// Missing futures values return DATA_UNAVAILABLE, never a fabricated 0.
/// </summary>
public static class FuturesAlphaEvaluator
{
    public static bool Handles(string templateKey) =>
        templateKey is StrategyTemplateKeys.FundingOiReversal
            or StrategyTemplateKeys.FundingBasisRv
            or StrategyTemplateKeys.FundingPriceMomentum
            or StrategyTemplateKeys.OiPriceVolumeRegime
            or StrategyTemplateKeys.FundingExtremeMomentumExhaustion
            or StrategyTemplateKeys.BasisMeanReversion
            or StrategyTemplateKeys.FundingBasisVwap
            or StrategyTemplateKeys.OiBreakoutConfirmation;

    public static StrategySignalDetail Evaluate(
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

        if (p.UseFuturesFilter && MissingRequired(p.TemplateKey, context) is { } missing)
        {
            return Unavailable(candles, i, missing);
        }

        return p.TemplateKey switch
        {
            StrategyTemplateKeys.FundingOiReversal => FundingOiReversal(p, candles, i, context, cache),
            StrategyTemplateKeys.FundingBasisRv => FundingBasisRv(p, candles, i, context, cache),
            StrategyTemplateKeys.FundingPriceMomentum => FundingPriceMomentum(p, candles, i, context, cache),
            StrategyTemplateKeys.OiPriceVolumeRegime => OiPriceVolume(p, candles, i, context, cache),
            StrategyTemplateKeys.FundingExtremeMomentumExhaustion => FundingExhaustion(p, candles, i, context, cache),
            StrategyTemplateKeys.BasisMeanReversion => BasisMeanReversion(p, candles, i, context, cache),
            StrategyTemplateKeys.FundingBasisVwap => FundingBasisVwap(p, candles, i, context, cache),
            StrategyTemplateKeys.OiBreakoutConfirmation => OiBreakout(p, candles, i, context, cache),
            _ => Detail(SignalType.NoAction, "Unknown futures-alpha template.", candles, i, status: "IMPLEMENTATION_ERROR")
        };
    }

    private static StrategySignalDetail FundingOiReversal(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var n = Math.Max(8, p.EntryLookback);
        var atr = cache.Atr(p.AtrPeriod);
        if (atr[i] is not { } atrNow || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "ATR not ready.", candles, i);
        }

        var disp = p.PriceDisplacementAtr <= 0m ? 1.5m : p.PriceDisplacementAtr;
        var move = candles[i].Close - candles[i - n].Close;
        var down = move <= -disp * atrNow;
        var up = move >= disp * atrNow;
        if (!down && !up)
        {
            return Detail(SignalType.NoAction, "Price displacement below ATR threshold.", candles, i);
        }

        if (!Exhaustion(candles, i, n) || !ReversalCandle(candles, i, down))
        {
            return Detail(SignalType.NoAction, "Momentum exhaustion / reversal candle not confirmed.", candles, i);
        }

        if (p.UseFuturesFilter)
        {
            if (context.FundingRate is null || context.OpenInterest is null)
            {
                return Unavailable(candles, i, "Funding or OI series missing.");
            }

            var fundPct = FuturesAlphaFeatures.PercentileAt(context.FundingRate, i, p.FundingLookback);
            var oiPct = FuturesAlphaFeatures.PercentileAt(context.OpenInterest, i, p.OiLookback);
            if (fundPct is null || oiPct is null)
            {
                return Unavailable(candles, i, "Funding or OI observation missing at signal time.");
            }

            var rawTail = p.FundingExtremePercentile <= 0m ? 0.10m : p.FundingExtremePercentile;
            var tail = rawTail > 0.5m ? 1m - rawTail : rawTail;
            var oiExt = p.OiExtremePercentile <= 0m ? 0.90m : p.OiExtremePercentile;
            var fundLow = fundPct <= tail;
            var fundHigh = fundPct >= 1m - tail;
            if (oiPct < oiExt)
            {
                return Detail(SignalType.NoAction, "OI not in extreme percentile.", candles, i);
            }

            if (down && !fundLow)
            {
                return Detail(SignalType.NoAction, "Down displacement without low-tail funding.", candles, i);
            }

            if (up && !fundHigh)
            {
                return Detail(SignalType.NoAction, "Up displacement without high-tail funding.", candles, i);
            }
        }

        return Directed(p, down, "Funding/OI/price displacement hypothesis.", candles, i, atrNow);
    }

    private static StrategySignalDetail FundingBasisRv(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var zEntry = p.ZScoreEntry <= 0m ? 2m : p.ZScoreEntry;
        decimal? fundZ;
        decimal? basisZ;
        if (p.UseFuturesFilter)
        {
            if (context.FundingRate is null || context.NormalizedBasis is null)
            {
                return Unavailable(candles, i, "Funding or basis series missing.");
            }

            fundZ = FuturesAlphaFeatures.ZScoreAt(context.FundingRate, i, p.FundingLookback);
            basisZ = FuturesAlphaFeatures.ZScoreAt(context.NormalizedBasis, i, p.EntryLookback);
            if (fundZ is null || basisZ is null)
            {
                return Unavailable(candles, i, "Funding or basis missing at signal time.");
            }
        }
        else
        {
            var closeZ = cache.CloseZScore(Math.Max(20, p.EntryLookback));
            fundZ = closeZ[i];
            basisZ = closeZ[i];
            if (fundZ is null)
            {
                return Detail(SignalType.NoAction, "Close z-score not ready.", candles, i);
            }
        }

        var extremeNeg = fundZ <= -zEntry && basisZ <= -zEntry;
        var extremePos = fundZ >= zEntry && basisZ >= zEntry;
        if (!extremeNeg && !extremePos)
        {
            return Detail(SignalType.NoAction, "Funding/basis not co-extreme.", candles, i);
        }

        var atr = cache.Atr(p.AtrPeriod);
        return Directed(p, extremeNeg, "Funding+basis dislocation hypothesis (directional). Market-neutral pairs not tested.", candles, i, atr[i]);
    }

    private static StrategySignalDetail FundingPriceMomentum(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var emaFast = cache.Ema(p.EmaFast);
        var emaSlow = cache.Ema(p.EmaSlow);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        var n = Math.Max(8, p.EntryLookback);
        if (emaFast[i] is not { } f || emaSlow[i] is not { } s)
        {
            return Detail(SignalType.NoAction, "EMA not ready.", candles, i);
        }

        var volOk = !p.VolumeFilterEnabled || rel[i] is { } rv && rv >= p.MinimumRelativeVolume;
        if (!volOk)
        {
            return Detail(SignalType.NoAction, "Relative volume filter.", candles, i);
        }

        var pxUp = candles[i].Close > candles[i - n].Close && f > s;
        var pxDown = candles[i].Close < candles[i - n].Close && f < s;
        if (!pxUp && !pxDown)
        {
            return Detail(SignalType.NoAction, "No price/trend momentum.", candles, i);
        }

        if (p.UseFuturesFilter)
        {
            if (context.FundingRate is null)
            {
                return Unavailable(candles, i, "Funding series missing.");
            }

            var fundPct = FuturesAlphaFeatures.PercentileAt(context.FundingRate, i, p.FundingLookback);
            if (fundPct is null)
            {
                return Unavailable(candles, i, "Funding missing at signal time.");
            }

            var rawTail = p.FundingExtremePercentile <= 0m ? 0.10m : p.FundingExtremePercentile;
            var tail = rawTail > 0.5m ? 1m - rawTail : rawTail;
            if (pxUp && fundPct < 1m - tail && fundPct > tail)
            {
                return Detail(SignalType.NoAction, "Funding not extreme with upside momentum.", candles, i);
            }

            if (pxDown && fundPct > tail && fundPct < 1m - tail)
            {
                return Detail(SignalType.NoAction, "Funding not extreme with downside momentum.", candles, i);
            }
        }

        var atr = cache.Atr(p.AtrPeriod);
        return Directed(p, pxDown, "Funding + price momentum + volume + trend.", candles, i, atr[i]);
    }

    private static StrategySignalDetail OiPriceVolume(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var n = Math.Max(8, p.OiLookback);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        var px = FuturesAlphaFeatures.PriceChangeAt(candles, i, n);
        if (px is null || rel[i] is not { } rv)
        {
            return Detail(SignalType.NoAction, "Price/volume not ready.", candles, i);
        }

        var volUp = rv >= p.MinimumRelativeVolume;
        decimal? oiChg = null;
        if (p.UseFuturesFilter)
        {
            if (context.OpenInterest is null)
            {
                return Unavailable(candles, i, "OI series missing.");
            }

            oiChg = FuturesAlphaFeatures.ChangeAt(context.OpenInterest, i, n);
            if (oiChg is null)
            {
                return Unavailable(candles, i, "OI missing at signal time.");
            }
        }

        var priceUp = px > 0m;
        var oiUp = oiChg is { } o && o > 0m;
        var tradeContinuation = volUp && (p.UseFuturesFilter ? oiUp : true);
        if (!tradeContinuation)
        {
            return Detail(SignalType.NoAction, "Volume/OI expansion filter.", candles, i);
        }

        var atr = cache.Atr(p.AtrPeriod);
        return Directed(p, !priceUp, "OI/price/volume regime (expansion states only).", candles, i, atr[i]);
    }

    private static StrategySignalDetail FundingExhaustion(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var n = Math.Max(8, p.EntryLookback);
        var atr = cache.Atr(p.AtrPeriod);
        if (atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "ATR not ready.", candles, i);
        }

        var disp = p.PriceDisplacementAtr <= 0m ? 1.5m : p.PriceDisplacementAtr;
        var move = candles[i].Close - candles[i - n].Close;
        var down = move <= -disp * atrNow;
        var up = move >= disp * atrNow;
        if ((!down && !up) || !Exhaustion(candles, i, n) || !ReversalCandle(candles, i, down))
        {
            return Detail(SignalType.NoAction, "Exhaustion/reversal not confirmed.", candles, i);
        }

        if (p.UseFuturesFilter)
        {
            if (context.FundingRate is null)
            {
                return Unavailable(candles, i, "Funding series missing.");
            }

            var fundPct = FuturesAlphaFeatures.PercentileAt(context.FundingRate, i, p.FundingLookback);
            if (fundPct is null)
            {
                return Unavailable(candles, i, "Funding missing at signal time.");
            }

            var rawTail = p.FundingExtremePercentile <= 0m ? 0.10m : p.FundingExtremePercentile;
            var tail = rawTail > 0.5m ? 1m - rawTail : rawTail;
            if (down && fundPct > tail)
            {
                return Detail(SignalType.NoAction, "Down move without negative-tail funding.", candles, i);
            }

            if (up && fundPct < 1m - tail)
            {
                return Detail(SignalType.NoAction, "Up move without positive-tail funding.", candles, i);
            }
        }

        return Directed(p, down, "Funding extreme + momentum exhaustion.", candles, i, atrNow);
    }

    private static StrategySignalDetail BasisMeanReversion(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var zEntry = p.ZScoreEntry <= 0m ? 2m : p.ZScoreEntry;
        decimal? z;
        if (p.UseFuturesFilter)
        {
            if (context.NormalizedBasis is null)
            {
                return Unavailable(candles, i, "Basis series missing.");
            }

            z = FuturesAlphaFeatures.ZScoreAt(context.NormalizedBasis, i, p.EntryLookback);
            if (z is null)
            {
                return Unavailable(candles, i, "Basis missing at signal time.");
            }
        }
        else
        {
            z = cache.CloseZScore(Math.Max(20, p.EntryLookback))[i];
            if (z is null)
            {
                return Detail(SignalType.NoAction, "Close z-score not ready.", candles, i);
            }
        }

        var extremeNeg = z <= -zEntry;
        var extremePos = z >= zEntry;
        if (!extremeNeg && !extremePos)
        {
            return Detail(SignalType.NoAction, "Z-score inside band.", candles, i);
        }

        var atr = cache.Atr(p.AtrPeriod);
        return Directed(p, extremeNeg, "Basis (or price) z-score hypothesis.", candles, i, atr[i]);
    }

    private static StrategySignalDetail FundingBasisVwap(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var vwap = cache.SessionVwap();
        var atr = cache.Atr(p.AtrPeriod);
        if (vwap[i] is not { } vw || atr[i] is not { } atrNow || atrNow <= 0m)
        {
            return Detail(SignalType.NoAction, "VWAP/ATR not ready.", candles, i);
        }

        var dist = (candles[i].Close - vw) / atrNow;
        var below = dist <= -p.MaxVwapDistanceAtr;
        var above = dist >= p.MaxVwapDistanceAtr;
        if (!below && !above)
        {
            return Detail(SignalType.NoAction, "VWAP deviation too small.", candles, i);
        }

        if (!ReversalCandle(candles, i, below))
        {
            return Detail(SignalType.NoAction, "No reversal candle at VWAP extreme.", candles, i);
        }

        if (p.UseFuturesFilter)
        {
            if (context.FundingRate is null || context.NormalizedBasis is null)
            {
                return Unavailable(candles, i, "Funding or basis missing.");
            }

            var fundPct = FuturesAlphaFeatures.PercentileAt(context.FundingRate, i, p.FundingLookback);
            var basisZ = FuturesAlphaFeatures.ZScoreAt(context.NormalizedBasis, i, p.EntryLookback);
            if (fundPct is null || basisZ is null)
            {
                return Unavailable(candles, i, "Funding or basis missing at signal time.");
            }

            var rawTail = p.FundingExtremePercentile <= 0m ? 0.10m : p.FundingExtremePercentile;
            var tail = rawTail > 0.5m ? 1m - rawTail : rawTail;
            var zEntry = p.ZScoreEntry <= 0m ? 2m : p.ZScoreEntry;
            if (below && (fundPct > tail || basisZ > -zEntry))
            {
                return Detail(SignalType.NoAction, "Below-VWAP without negative funding/basis extreme.", candles, i);
            }

            if (above && (fundPct < 1m - tail || basisZ < zEntry))
            {
                return Detail(SignalType.NoAction, "Above-VWAP without positive funding/basis extreme.", candles, i);
            }
        }

        return Directed(p, below, "Funding + basis + VWAP deviation.", candles, i, atrNow);
    }

    private static StrategySignalDetail OiBreakout(
        StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache)
    {
        var n = Math.Max(10, p.DonchianLength);
        var (hi, lo) = cache.Donchian(n);
        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        if (hi[i] is not { } donHi || lo[i] is not { } donLo || rel[i] is not { } rv)
        {
            return Detail(SignalType.NoAction, "Donchian/volume not ready.", candles, i);
        }

        if (rv < p.BreakoutRelativeVolume)
        {
            return Detail(SignalType.NoAction, "Breakout without volume expansion.", candles, i);
        }

        var brkUp = candles[i].Close > donHi && (i == 0 || candles[i - 1].Close <= (hi[i - 1] ?? donHi));
        var brkDn = candles[i].Close < donLo && (i == 0 || candles[i - 1].Close >= (lo[i - 1] ?? donLo));
        if (!brkUp && !brkDn)
        {
            return Detail(SignalType.NoAction, "No Donchian breakout.", candles, i);
        }

        if (p.UseFuturesFilter)
        {
            if (context.OpenInterest is null)
            {
                return Unavailable(candles, i, "OI series missing.");
            }

            var oiChg = FuturesAlphaFeatures.ChangeAt(context.OpenInterest, i, Math.Max(8, p.OiLookback));
            if (oiChg is null)
            {
                return Unavailable(candles, i, "OI missing at signal time.");
            }

            if (oiChg < p.OiChangeThreshold)
            {
                return Detail(SignalType.NoAction, "Breakout without OI expansion.", candles, i);
            }
        }

        var atr = cache.Atr(p.AtrPeriod);
        return Directed(p, brkDn, "Breakout ± OI confirmation.", candles, i, atr[i]);
    }

    private static StrategySignalDetail Directed(
        StrategyTemplateParams p,
        bool lowSide,
        string why,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal? atr)
    {
        var contrarian = IsContrarian(p);
        var longSignal = contrarian ? lowSide : !lowSide;
        var stop = atr is { } a
            ? (longSignal ? candles[i].Close - a * p.StopAtrMultiplier : candles[i].Close + a * p.StopAtrMultiplier)
            : (decimal?)null;
        var hyp = contrarian ? "contrarian" : "continuation";
        return Detail(
            longSignal ? SignalType.Buy : SignalType.Sell,
            $"{why} hypothesis={hyp} futuresFilter={p.UseFuturesFilter}.",
            candles,
            i,
            stop: stop);
    }

    private static string? MissingRequired(string templateKey, StrategyContext context) => templateKey switch
    {
        StrategyTemplateKeys.FundingOiReversal when Absent(context.FundingRate) || Absent(context.OpenInterest) =>
            "Funding or OI series missing.",
        StrategyTemplateKeys.FundingBasisRv when Absent(context.FundingRate) || Absent(context.NormalizedBasis) =>
            "Funding or basis series missing.",
        StrategyTemplateKeys.FundingPriceMomentum when Absent(context.FundingRate) =>
            "Funding series missing.",
        StrategyTemplateKeys.OiPriceVolumeRegime when Absent(context.OpenInterest) =>
            "OI series missing.",
        StrategyTemplateKeys.FundingExtremeMomentumExhaustion when Absent(context.FundingRate) =>
            "Funding series missing.",
        StrategyTemplateKeys.BasisMeanReversion when Absent(context.NormalizedBasis) =>
            "Basis series missing.",
        StrategyTemplateKeys.FundingBasisVwap when Absent(context.FundingRate) || Absent(context.NormalizedBasis) =>
            "Funding or basis series missing.",
        StrategyTemplateKeys.OiBreakoutConfirmation when Absent(context.OpenInterest) =>
            "OI series missing.",
        _ => null
    };

    private static bool Absent(IReadOnlyList<decimal?>? series) =>
        series is null || series.Count == 0 || series.All(v => v is null);

    private static bool IsContrarian(StrategyTemplateParams p) =>
        LooksContrarian(p.FundingHypothesis) || LooksContrarian(p.OiHypothesis);

    private static bool LooksContrarian(string? hyp) =>
        !string.IsNullOrWhiteSpace(hyp)
        && (hyp.Contains("contrarian", StringComparison.OrdinalIgnoreCase)
            || hyp.Contains("reversal", StringComparison.OrdinalIgnoreCase));

    private static bool Exhaustion(IReadOnlyList<MarketCandle> candles, int i, int n)
    {
        if (i < n * 2)
        {
            return false;
        }

        var d1 = candles[i].Close - candles[i - n].Close;
        var d0 = candles[i - n].Close - candles[i - 2 * n].Close;
        return d1 != 0m && d0 != 0m && Math.Sign(d1) == Math.Sign(d0) && Math.Abs(d1) < Math.Abs(d0);
    }

    private static bool ReversalCandle(IReadOnlyList<MarketCandle> candles, int i, bool afterDown)
    {
        var bar = candles[i];
        return afterDown ? bar.Close > bar.Open : bar.Close < bar.Open;
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
        string status = "RESEARCHING") =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime, stop, null, null, status);
}
