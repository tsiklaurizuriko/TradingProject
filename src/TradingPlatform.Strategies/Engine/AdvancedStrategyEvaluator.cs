using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

public static class AdvancedStrategyEvaluator
{
    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        p.TemplateKey switch
        {
            StrategyTemplateKeys.TurtleTsm => Turtle(p, candles, i, context, cache),
            StrategyTemplateKeys.VwapPullbackTrend => VwapPullback(p, candles, i, context, cache),
            StrategyTemplateKeys.VolatilityBreakout => VolatilityBreakout(p, candles, i, context, cache),
            StrategyTemplateKeys.SupertrendEmaTrend => SupertrendEma(p, candles, i, context, cache),
            StrategyTemplateKeys.OiPriceMomentum => OiMomentum(p, candles, i, context, cache),
            StrategyTemplateKeys.FundingOiRegime => FundingRegime(p, candles, i, context, cache),
            StrategyTemplateKeys.VolSpikeEmaTrend => VolSpikeEma(p, candles, i, context, cache),
            StrategyTemplateKeys.Bb202Break => BbBreak(p, candles, i, context, cache),
            _ => AlphaStrategyEvaluator.Evaluate(p, candles, i, context, cache)
        };

    private static StrategySignalDetail Turtle(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var entryN = Math.Max(5, p.EntryLookback);
        var exitN = Math.Max(2, p.ExitLookback);
        var (entryHigh, entryLow) = cache.Donchian(entryN);
        var (exitHigh, exitLow) = cache.Donchian(exitN);
        var trend = cache.Ema(p.TrendEmaPeriod);
        var atr = cache.Atr(p.AtrPeriod);
        var relVol = cache.RelativeVolume(p.RelativeVolumePeriod);
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && ((exitLow[i] is { } lo && close < lo)))
            {
                return Detail(SignalType.Exit, "Close broke the Turtle exit lookback low.", candles, i, SnapshotOf(close, trend, atr, relVol));
            }

            if (!IsLong(context) && exitHigh[i] is { } hi && close > hi)
            {
                return Detail(SignalType.Exit, "Close broke the Turtle exit lookback high.", candles, i, SnapshotOf(close, trend, atr, relVol));
            }

            return Detail(SignalType.Hold, "Position open; Turtle exit not triggered.", candles, i, SnapshotOf(close, trend, atr, relVol));
        }

        if (entryHigh[i] is not { } prevHigh || entryLow[i] is not { } prevLow || trend[i] is not { } ema || atr[i] is not { } atrNow)
        {
            return Detail(SignalType.NoAction, "Turtle indicators not ready.", candles, i);
        }

        if (p.VolumeFilterEnabled && (relVol[i] is not { } rv || rv < p.MinimumRelativeVolume))
        {
            return Detail(SignalType.NoAction, "Relative volume filter skipped this Turtle bar.", candles, i, SnapshotOf(close, trend, atr, relVol));
        }

        var longBreak = BreaksAbove(entryHigh, candles, i) && close > ema;
        var shortBreak = BreaksBelow(entryLow, candles, i) && close < ema;
        if (longBreak)
        {
            return Detail(
                SignalType.Buy,
                "New Turtle breakout above prior-N high with EMA confirmation.",
                candles,
                i,
                SnapshotOf(close, trend, atr, relVol, prevHigh, prevLow),
                close - atrNow * p.AtrStopMultiplier);
        }

        if (shortBreak)
        {
            return Detail(
                SignalType.Sell,
                "New Turtle breakout below prior-N low with EMA confirmation.",
                candles,
                i,
                SnapshotOf(close, trend, atr, relVol, prevHigh, prevLow),
                close + atrNow * p.AtrStopMultiplier);
        }

        return Detail(SignalType.NoAction, "Turtle entry not matched.", candles, i, SnapshotOf(close, trend, atr, relVol, prevHigh, prevLow));
    }

    private static StrategySignalDetail VwapPullback(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var vwap = cache.SessionVwap();
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var atr = cache.Atr(p.AtrPeriod);
        var rsi = cache.Rsi(p.RsiPeriod);
        var relVol = cache.RelativeVolume(p.RelativeVolumePeriod);
        var close = candles[i].Close;
        var open = candles[i].Open;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && ((slow[i] is { } s && close < s) || (fast[i] is { } f && slow[i] is { } sl && f < sl)))
            {
                return Detail(SignalType.Exit, "VWAP pullback invalidated (close below slow EMA or EMA cross down).", candles, i);
            }

            if (!IsLong(context) && ((slow[i] is { } s2 && close > s2) || (fast[i] is { } f2 && slow[i] is { } sl2 && f2 > sl2)))
            {
                return Detail(SignalType.Exit, "VWAP pullback invalidated (close above slow EMA or EMA cross up).", candles, i);
            }

            return Detail(SignalType.Hold, "Position open; VWAP pullback exit not triggered.", candles, i);
        }

        if (vwap[i] is not { } vw
            || fast[i] is not { } emaFast
            || slow[i] is not { } emaSlow
            || atr[i] is not { } atrNow
            || rsi[i] is not { } rsiNow)
        {
            return Detail(SignalType.NoAction, "VWAP pullback indicators not ready.", candles, i);
        }

        if (p.VolumeFilterEnabled && (relVol[i] is not { } rv || rv < p.MinimumRelativeVolume))
        {
            return Detail(SignalType.NoAction, "Relative volume filter skipped this VWAP bar.", candles, i);
        }

        var dist = Math.Abs(close - vw) / atrNow;
        var nearVwap = dist <= p.MaxVwapDistanceAtr;
        var nearFast = Math.Abs(close - emaFast) / atrNow <= p.MaxVwapDistanceAtr;
        var pullbackZone = nearVwap || nearFast;
        var longTrend = close > emaSlow && emaFast > emaSlow;
        var shortTrend = close < emaSlow && emaFast < emaSlow;
        var longConfirm = close > open && rsiNow > 50m && close >= vw;
        var shortConfirm = close < open && rsiNow < 50m && close <= vw;
        var longSetup = longTrend && pullbackZone && longConfirm && close > emaSlow;
        var shortSetup = shortTrend && pullbackZone && shortConfirm && close < emaSlow;
        var prevLong = i > 1 && WasVwapSetup(p, candles, i - 1, cache, longSide: true);
        var prevShort = i > 1 && WasVwapSetup(p, candles, i - 1, cache, longSide: false);
        if (longSetup && !prevLong)
        {
            var swing = SwingLow(candles, i, 5);
            var stop = Math.Min(swing, close - atrNow * p.StopAtrMultiplier);
            return Detail(
                SignalType.Buy,
                "Trend + VWAP/EMA pullback + bullish confirmation.",
                candles,
                i,
                SnapshotOf(close, slow, atr, relVol, vw, rsiNow),
                stop);
        }

        if (shortSetup && !prevShort)
        {
            var swing = SwingHigh(candles, i, 5);
            var stop = Math.Max(swing, close + atrNow * p.StopAtrMultiplier);
            return Detail(
                SignalType.Sell,
                "Trend + VWAP/EMA pullback + bearish confirmation.",
                candles,
                i,
                SnapshotOf(close, slow, atr, relVol, vw, rsiNow),
                stop);
        }

        return Detail(SignalType.NoAction, "VWAP pullback requires trend + pullback + confirmation.", candles, i);
    }

    private static bool WasVwapSetup(StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, CausalIndicatorCache cache, bool longSide)
    {
        var vwap = cache.SessionVwap();
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var atr = cache.Atr(p.AtrPeriod);
        var rsi = cache.Rsi(p.RsiPeriod);
        if (vwap[i] is not { } vw || fast[i] is not { } emaFast || slow[i] is not { } emaSlow || atr[i] is not { } atrNow || rsi[i] is not { } rsiNow)
        {
            return false;
        }

        var close = candles[i].Close;
        var dist = Math.Abs(close - vw) / atrNow;
        var pullbackZone = dist <= p.MaxVwapDistanceAtr || Math.Abs(close - emaFast) / atrNow <= p.MaxVwapDistanceAtr;
        if (longSide)
        {
            return close > emaSlow && emaFast > emaSlow && pullbackZone && close > candles[i].Open && rsiNow > 50m && close >= vw;
        }

        return close < emaSlow && emaFast < emaSlow && pullbackZone && close < candles[i].Open && rsiNow < 50m && close <= vw;
    }

    private static StrategySignalDetail VolatilityBreakout(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (mid, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var widthPct = cache.BollingerWidthPercentile(p.BbPeriod, p.BbStdDev, p.VolatilityLookback);
        var atr = cache.Atr(p.AtrPeriod);
        var atrSma = cache.AtrSma(p.AtrPeriod, p.AtrExpansionLookback);
        var relVol = cache.RelativeVolume(p.RelativeVolumePeriod);
        var slow = cache.Ema(p.EmaSlow);
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            if (mid[i] is { } band)
            {
                if (IsLong(context) && close <= band)
                {
                    return Detail(SignalType.Exit, "Volatility breakout close returned to the Bollinger mid.", candles, i);
                }

                if (!IsLong(context) && close >= band)
                {
                    return Detail(SignalType.Exit, "Volatility breakout close returned to the Bollinger mid.", candles, i);
                }
            }

            return Detail(SignalType.Hold, "Position open; volatility breakout exit not triggered.", candles, i);
        }

        if (i < 1
            || widthPct[i - 1] is not { } prevWidth
            || upper[i] is not { } up
            || lower[i] is not { } lo
            || atr[i] is not { } atrNow
            || atr[i - 1] is not { } atrPrev
            || slow[i] is not { } ema50
            || relVol[i] is not { } rv)
        {
            return Detail(SignalType.NoAction, "Volatility breakout indicators not ready.", candles, i);
        }

        var compressed = prevWidth <= p.CompressionPercentile;
        var expanding = atrNow > atrPrev && (atrSma[i] is not { } sma || atrNow > sma);
        var volumeOk = rv >= p.BreakoutRelativeVolume;
        var newLong = compressed
            && expanding
            && volumeOk
            && close > up
            && close > ema50
            && candles[i - 1].Close <= (upper[i - 1] ?? up);
        var newShort = compressed
            && expanding
            && volumeOk
            && close < lo
            && close < ema50
            && candles[i - 1].Close >= (lower[i - 1] ?? lo);
        if (newLong)
        {
            return Detail(
                SignalType.Buy,
                "Compression → expansion breakout above the upper band.",
                candles,
                i,
                SnapshotOf(close, slow, atr, relVol, up, prevWidth),
                close - atrNow * 1.5m);
        }

        if (newShort)
        {
            return Detail(
                SignalType.Sell,
                "Compression → expansion breakout below the lower band.",
                candles,
                i,
                SnapshotOf(close, slow, atr, relVol, lo, prevWidth),
                close + atrNow * 1.5m);
        }

        return Detail(SignalType.NoAction, "Volatility breakout requires a new compression→expansion transition.", candles, i);
    }

    private static StrategySignalDetail SupertrendEma(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var dir = cache.SupertrendDirection(p.SupertrendPeriod, p.SupertrendMultiplier);
        var line = cache.SupertrendLine(p.SupertrendPeriod, p.SupertrendMultiplier);
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var adx = cache.Adx(p.AdxPeriod);
        var close = candles[i].Close;
        if (context.HasOpenPosition)
        {
            var flipped = i >= 1 && dir[i] is { } nowDir && dir[i - 1] is { } prevDir && nowDir != prevDir;
            if (IsLong(context) && (flipped && dir[i] < 0m || CrossesBelow(fast, slow, i)))
            {
                return Detail(SignalType.Exit, "Supertrend turned bearish or EMA20 crossed below EMA50.", candles, i, stop: line[i]);
            }

            if (!IsLong(context) && (flipped && dir[i] > 0m || CrossesAbove(fast, slow, i)))
            {
                return Detail(SignalType.Exit, "Supertrend turned bullish or EMA20 crossed above EMA50.", candles, i, stop: line[i]);
            }

            return Detail(SignalType.Hold, "Position open; Supertrend exit not triggered.", candles, i, stop: line[i]);
        }

        if (i < 1
            || dir[i] is not { } now
            || dir[i - 1] is not { } prev
            || fast[i] is not { } emaFast
            || slow[i] is not { } emaSlow
            || adx[i] is not { } adxNow)
        {
            return Detail(SignalType.NoAction, "Supertrend/EMA/ADX not ready.", candles, i);
        }

        if (adxNow < p.MinimumAdx)
        {
            return Detail(SignalType.NoAction, "ADX below minimum; Supertrend entry skipped.", candles, i);
        }

        var bullTurn = prev <= 0m && now > 0m;
        var bearTurn = prev >= 0m && now < 0m;
        if (bullTurn && emaFast > emaSlow && close > emaSlow)
        {
            return Detail(
                SignalType.Buy,
                "Supertrend turned bullish with EMA structure and ADX confirmation.",
                candles,
                i,
                new Dictionary<string, decimal?> { ["close"] = close, ["adx"] = adxNow, ["st"] = now },
                line[i]);
        }

        if (bearTurn && emaFast < emaSlow && close < emaSlow)
        {
            return Detail(
                SignalType.Sell,
                "Supertrend turned bearish with EMA structure and ADX confirmation.",
                candles,
                i,
                new Dictionary<string, decimal?> { ["close"] = close, ["adx"] = adxNow, ["st"] = now },
                line[i]);
        }

        return Detail(SignalType.NoAction, "Supertrend entry requires a direction transition.", candles, i);
    }

    private static StrategySignalDetail OiMomentum(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (context.OpenInterest is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: historical open interest is not timestamp-aligned.", candles, i, status: "DATA_UNAVAILABLE");
        }

        var oi = context.OpenInterest;
        if (oi.Count <= i || oi[i] is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: open interest missing at this closed candle.", candles, i, status: "DATA_UNAVAILABLE");
        }

        var n = Math.Max(2, p.OiLookback);
        var oiChange = AlignedMarketSeries.Change(oi, i, n);
        var priceChange = candles[i - n].Close == 0m ? null : (decimal?)((candles[i].Close - candles[i - n].Close) / candles[i - n].Close);
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var relVol = cache.RelativeVolume(p.RelativeVolumePeriod);
        var reversal = string.Equals(p.OiHypothesis, "reversal", StringComparison.OrdinalIgnoreCase);
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && CrossesBelow(fast, slow, i))
            {
                return Detail(SignalType.Exit, "OI momentum EMA reversed.", candles, i);
            }

            if (!IsLong(context) && CrossesAbove(fast, slow, i))
            {
                return Detail(SignalType.Exit, "OI momentum EMA reversed.", candles, i);
            }

            return Detail(SignalType.Hold, "Position open; OI momentum exit not triggered.", candles, i);
        }

        if (oiChange is not { } dOi || priceChange is not { } dPx || fast[i] is not { } emaFast || slow[i] is not { } emaSlow || relVol[i] is not { } rv)
        {
            return Detail(SignalType.NoAction, "OI momentum indicators not ready.", candles, i);
        }

        if (rv < p.MinimumRelativeVolume)
        {
            return Detail(SignalType.NoAction, "OI momentum volume confirmation missing.", candles, i);
        }

        var thP = p.PriceChangeThreshold;
        var thO = p.OiChangeThreshold;
        bool longOk;
        bool shortOk;
        if (reversal)
        {
            longOk = dPx < -thP && dOi > thO && emaFast < emaSlow && candles[i].Close > candles[i].Open;
            shortOk = dPx > thP && dOi > thO && emaFast > emaSlow && candles[i].Close < candles[i].Open;
        }
        else
        {
            longOk = dPx > thP && dOi > thO && emaFast > emaSlow && candles[i].Close > emaSlow;
            shortOk = dPx < -thP && dOi > thO && emaFast < emaSlow && candles[i].Close < emaSlow;
        }

        var prevLong = i > n && PriorOiMatch(p, candles, i - 1, context, cache, longSide: true);
        var prevShort = i > n && PriorOiMatch(p, candles, i - 1, context, cache, longSide: false);
        if (longOk && !prevLong)
        {
            return Detail(SignalType.Buy, reversal ? "OI reversal hypothesis LONG." : "OI continuation hypothesis LONG.", candles, i);
        }

        if (shortOk && !prevShort)
        {
            return Detail(SignalType.Sell, reversal ? "OI reversal hypothesis SHORT." : "OI continuation hypothesis SHORT.", candles, i);
        }

        return Detail(SignalType.NoAction, "OI price-momentum state not matched.", candles, i);
    }

    private static bool PriorOiMatch(StrategyTemplateParams p, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context, CausalIndicatorCache cache, bool longSide)
    {
        if (context.OpenInterest is null || i < p.OiLookback)
        {
            return false;
        }

        var n = Math.Max(2, p.OiLookback);
        var oiChange = AlignedMarketSeries.Change(context.OpenInterest, i, n);
        var priceChange = candles[i - n].Close == 0m ? null : (decimal?)((candles[i].Close - candles[i - n].Close) / candles[i - n].Close);
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        if (oiChange is not { } dOi || priceChange is not { } dPx || fast[i] is not { } emaFast || slow[i] is not { } emaSlow)
        {
            return false;
        }

        var reversal = string.Equals(p.OiHypothesis, "reversal", StringComparison.OrdinalIgnoreCase);
        if (reversal)
        {
            return longSide
                ? dPx < -p.PriceChangeThreshold && dOi > p.OiChangeThreshold && emaFast < emaSlow
                : dPx > p.PriceChangeThreshold && dOi > p.OiChangeThreshold && emaFast > emaSlow;
        }

        return longSide
            ? dPx > p.PriceChangeThreshold && dOi > p.OiChangeThreshold && emaFast > emaSlow
            : dPx < -p.PriceChangeThreshold && dOi > p.OiChangeThreshold && emaFast < emaSlow;
    }

    private static StrategySignalDetail FundingRegime(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (context.FundingRate is null || context.OpenInterest is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: historical funding and/or open interest is not timestamp-aligned.", candles, i, status: "DATA_UNAVAILABLE");
        }

        var funding = context.FundingRate;
        if (funding.Count <= i || funding[i] is null)
        {
            return Detail(SignalType.NoAction, "DATA_UNAVAILABLE: funding missing at this closed candle.", candles, i, status: "DATA_UNAVAILABLE");
        }

        var n = Math.Max(2, p.OiLookback);
        var oiChange = AlignedMarketSeries.Change(context.OpenInterest, i, n);
        var priceChange = i >= n && candles[i - n].Close != 0m
            ? (decimal?)((candles[i].Close - candles[i - n].Close) / candles[i - n].Close)
            : null;
        var pct = ResearchIndicatorSeries.PercentileRank(funding, p.FundingLookback);
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        var contrarian = string.Equals(p.FundingHypothesis, "contrarian", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.FundingHypothesis, "reversal", StringComparison.OrdinalIgnoreCase);
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && CrossesBelow(fast, slow, i))
            {
                return Detail(SignalType.Exit, "Funding regime EMA reversed.", candles, i);
            }

            if (!IsLong(context) && CrossesAbove(fast, slow, i))
            {
                return Detail(SignalType.Exit, "Funding regime EMA reversed.", candles, i);
            }

            return Detail(SignalType.Hold, "Position open; funding regime exit not triggered.", candles, i);
        }

        if (pct[i] is not { } fundPct || oiChange is not { } dOi || priceChange is not { } dPx || fast[i] is not { } emaFast || slow[i] is not { } emaSlow)
        {
            return Detail(SignalType.NoAction, "Funding regime indicators not ready.", candles, i);
        }

        var extremeHigh = fundPct >= p.FundingExtremePercentile;
        var extremeLow = fundPct <= 1m - p.FundingExtremePercentile;
        bool longOk;
        bool shortOk;
        if (contrarian)
        {
            longOk = extremeLow && dPx < 0m && dOi > 0m && candles[i].Close > candles[i].Open;
            shortOk = extremeHigh && dPx > 0m && dOi > 0m && candles[i].Close < candles[i].Open;
        }
        else
        {
            longOk = !extremeHigh && dPx > p.PriceChangeThreshold && dOi > 0m && emaFast > emaSlow;
            shortOk = !extremeLow && dPx < -p.PriceChangeThreshold && dOi > 0m && emaFast < emaSlow;
        }

        if (longOk)
        {
            return Detail(SignalType.Buy, contrarian ? "Funding contrarian hypothesis LONG." : "Funding continuation hypothesis LONG.", candles, i);
        }

        if (shortOk)
        {
            return Detail(SignalType.Sell, contrarian ? "Funding contrarian hypothesis SHORT." : "Funding continuation hypothesis SHORT.", candles, i);
        }

        return Detail(SignalType.NoAction, "Funding/price/OI regime not matched.", candles, i);
    }

    private static StrategySignalDetail VolSpikeEma(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "Position open; Isolated book owns SL/TP/time-exit.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
        }

        if (i < 1)
        {
            return Detail(SignalType.NoAction, "Not enough closed candles.", candles, i);
        }

        var rel = cache.RelativeVolume(p.RelativeVolumePeriod);
        var ema21 = cache.Ema(p.EmaSlow);
        if (rel[i] is not { } rvNow || rel[i - 1] is not { } rvPrev || ema21[i] is not { } ema)
        {
            return Detail(SignalType.NoAction, "Relative volume / EMA21 not ready.", candles, i);
        }

        var spike = rvNow > p.MinimumRelativeVolume && rvPrev <= p.MinimumRelativeVolume;
        if (!spike)
        {
            return Detail(SignalType.NoAction, "No first-bar relative-volume spike.", candles, i);
        }

        var close = candles[i].Close;
        if (close > ema)
        {
            return Detail(SignalType.Buy, "Relative volume spike with close above EMA21.", candles, i);
        }

        if (close < ema)
        {
            return Detail(SignalType.Sell, "Relative volume spike with close below EMA21.", candles, i);
        }

        return Detail(SignalType.NoAction, "Volume spike but close equals EMA21.", candles, i);
    }

    private static StrategySignalDetail BbBreak(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "Position open; Isolated book owns SL/TP/time-exit.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
        }

        if (i < 1)
        {
            return Detail(SignalType.NoAction, "Not enough closed candles.", candles, i);
        }

        var (_, upper, lower) = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        if (upper[i] is not { } up || lower[i] is not { } lo || upper[i - 1] is not { } prevUp || lower[i - 1] is not { } prevLo)
        {
            return Detail(SignalType.NoAction, "Bollinger (20,2) not ready.", candles, i);
        }

        var close = candles[i].Close;
        var prevClose = candles[i - 1].Close;
        if (close > up && prevClose <= prevUp)
        {
            return Detail(SignalType.Buy, "Close crossed above upper Bollinger (20,2).", candles, i);
        }

        if (close < lo && prevClose >= prevLo)
        {
            return Detail(SignalType.Sell, "Close crossed below lower Bollinger (20,2).", candles, i);
        }

        return Detail(SignalType.NoAction, "No Bollinger (20,2) break.", candles, i);
    }

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        IReadOnlyDictionary<string, decimal?>? snapshot = null,
        decimal? stop = null,
        string status = "RESEARCHING") =>
        new(signal, reason, candles[i].CloseTime, stop, null, snapshot, status);

    private static Dictionary<string, decimal?> SnapshotOf(
        decimal close,
        IReadOnlyList<decimal?>? trend,
        IReadOnlyList<decimal?>? atr,
        IReadOnlyList<decimal?>? relVol,
        decimal? extra = null,
        decimal? extra2 = null)
    {
        var i = trend is null ? 0 : trend.Count - 1;
        return new Dictionary<string, decimal?>
        {
            ["close"] = close,
            ["ema"] = trend is { Count: > 0 } ? trend[Math.Min(i, trend.Count - 1)] : extra,
            ["atr"] = atr is { Count: > 0 } ? atr[Math.Min(i, atr.Count - 1)] : extra2,
            ["relVol"] = relVol is { Count: > 0 } ? relVol[Math.Min(i, relVol.Count - 1)] : null,
            ["extra"] = extra,
            ["extra2"] = extra2
        };
    }

    private static decimal SwingLow(IReadOnlyList<MarketCandle> candles, int i, int lookback)
    {
        var start = Math.Max(0, i - lookback);
        var min = candles[start].Low;
        for (var j = start + 1; j < i; j++)
        {
            if (candles[j].Low < min)
            {
                min = candles[j].Low;
            }
        }

        return min;
    }

    private static decimal SwingHigh(IReadOnlyList<MarketCandle> candles, int i, int lookback)
    {
        var start = Math.Max(0, i - lookback);
        var max = candles[start].High;
        for (var j = start + 1; j < i; j++)
        {
            if (candles[j].High > max)
            {
                max = candles[j].High;
            }
        }

        return max;
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

    private static bool IsLong(StrategyContext context) =>
        !context.HasOpenPosition || context.PositionSide != PositionSide.Short;
}
