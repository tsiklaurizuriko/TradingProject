using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

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
            StrategyTemplateKeys.BtcEma20Ema50Long => EmaCrossLong(p, candles, i, context, cache),
            StrategyTemplateKeys.TsMomentum285 => TsMomentum(candles, i, context),
            StrategyTemplateKeys.BtcDailyMax10 => BtcDailyMax(candles, i, context),
            StrategyTemplateKeys.FlowZone => FlowZone(candles, i, context),
            StrategyTemplateKeys.SqueezeWatch => SqueezeWatch(candles, i, context),
            StrategyTemplateKeys.ImpulseCatch => ImpulseCatch(candles, i, context),
            var pa when StrategyTemplateKeys.IsPriceAction(pa) =>
                PriceActionStrategyEvaluator.Evaluate(p, candles, i, context, cache),
            var scalp when StrategyTemplateKeys.IsScalping(scalp) => ScalpingStrategyEvaluator.Evaluate(p, candles, i, context, cache),
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
            if (FittedTimeExit(candles, i, context))
            {
                return Detail(SignalType.Exit, "192-bar time exit, 48 hours.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
            }

            return Detail(SignalType.Hold, "Position open. Stop, take, and the 192-bar exit still apply.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
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
            if (FittedTimeExit(candles, i, context))
            {
                return Detail(SignalType.Exit, "192-bar time exit, 48 hours.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
            }

            return Detail(SignalType.Hold, "Position open. Stop, take, and the 192-bar exit still apply.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
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

    private static bool FittedTimeExit(IReadOnlyList<MarketCandle> candles, int i, StrategyContext context) =>
        context.PositionOpenedAt is { } opened && candles[i].CloseTime >= opened.AddHours(48);

    private static StrategySignalDetail FlowZone(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context)
    {
        const string status = "LIVE_PROBE";
        const int window = 24;
        const decimal edge = 0.25m;
        const decimal takerMajority = 0.62m;
        if (i < window - 1)
        {
            return Detail(SignalType.NoAction, "Need 24 closed bars.", candles, i, status: status);
        }

        var lo = candles[i - window + 1].Low;
        var hi = candles[i - window + 1].High;
        for (var k = i - window + 2; k <= i; k++)
        {
            if (candles[k].Low < lo)
            {
                lo = candles[k].Low;
            }

            if (candles[k].High > hi)
            {
                hi = candles[k].High;
            }
        }

        if (hi <= lo)
        {
            return Detail(SignalType.NoAction, "The 24-bar range is flat.", candles, i, status: status);
        }

        var bar = candles[i];
        var place = (bar.Close - lo) / (hi - lo);
        var imbalance = TakerFlow.Imbalance(bar);
        if (imbalance is null)
        {
            return Detail(SignalType.NoAction, "Taker buy volume is missing. No order.", candles, i, status: status);
        }

        var buyShare = (imbalance.Value + 1m) / 2m;
        var interest = OpenInterestRose(context.OpenInterest);
        if (context.HasOpenPosition)
        {
            var leftLong = IsLong(context) && (place < 1m - edge || buyShare <= takerMajority);
            var leftShort = !IsLong(context) && (place > edge || buyShare >= 1m - takerMajority);
            if ((leftLong || leftShort) && !IsLoss(context, bar.Close) && !MoveCoversRoundTripFee(context, bar.Close))
            {
                return Detail(SignalType.Hold, "Flow left the zone, but the gain is still inside the round-trip fee.", candles, i, status: status);
            }

            if (leftLong)
            {
                return Detail(SignalType.Exit, "Buy flow left the upper quarter.", candles, i, status: status);
            }

            if (leftShort)
            {
                return Detail(SignalType.Exit, "Sell flow left the lower quarter.", candles, i, status: status);
            }

            return Detail(SignalType.Hold, "Flow zone is still on.", candles, i, status: status);
        }

        if (interest is null)
        {
            return Detail(SignalType.NoAction, "Open interest is missing. No order.", candles, i, status: status);
        }

        if (interest == false)
        {
            return Detail(SignalType.NoAction, "Open interest is not rising.", candles, i, status: status);
        }

        if (place >= 1m - edge && buyShare > takerMajority && bar.Close > bar.Open)
        {
            return Detail(SignalType.Buy, "Upper zone, taker buy is above 62%, open interest rose.", candles, i, status: status);
        }

        if (place <= edge && buyShare < 1m - takerMajority && bar.Close < bar.Open)
        {
            return Detail(SignalType.Sell, "Lower zone, taker sell is above 62%, open interest rose.", candles, i, status: status);
        }

        return Detail(SignalType.NoAction, "Price, taker flow, and open interest do not agree.", candles, i, status: status);
    }

    private static bool? OpenInterestRose(IReadOnlyList<decimal?>? openInterest)
    {
        if (openInterest is null || openInterest.Count < 2)
        {
            return null;
        }

        var previous = openInterest[^2];
        var latest = openInterest[^1];
        if (previous is not { } prior || latest is not { } now || prior <= 0m || now <= 0m)
        {
            return null;
        }

        return now > prior;
    }

    /// <summary>A loss is already a reason to leave. Holding it so the fee looks smaller lets it walk to the 4% stop.</summary>
    private static bool IsLoss(StrategyContext context, decimal close)
    {
        if (context.AverageEntryPrice is not { } entry || entry <= 0m || close <= 0m)
        {
            return false;
        }

        var move = IsLong(context)
            ? (close - entry) / entry
            : (entry - close) / entry;
        return move < 0m;
    }

    /// <summary>0.20% is about two taker fees. A smaller gain pays the fee and keeps nothing.</summary>
    private static bool MoveCoversRoundTripFee(StrategyContext context, decimal close)
    {
        const decimal feeBand = 0.002m;
        if (context.AverageEntryPrice is not { } entry || entry <= 0m || close <= 0m)
        {
            return true;
        }

        var move = IsLong(context)
            ? (close - entry) / entry
            : (entry - close) / entry;
        return Math.Abs(move) >= feeBand;
    }

    private static StrategySignalDetail SqueezeWatch(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context)
    {
        const string status = "LIVE_PROBE";
        const int window = 24;
        const decimal openInterestRise = 0.15m;
        const decimal priceBand = 0.03m;
        const decimal fundingExtreme = 0.001m;
        if (context.HasOpenPosition)
        {
            if (context.AverageEntryPrice is { } entry && entry > 0m)
            {
                var adverse = IsLong(context)
                    ? (entry - candles[i].Close) / entry
                    : (candles[i].Close - entry) / entry;
                if (adverse >= 0.02m)
                {
                    return Detail(SignalType.Exit, "Price moved 2% against the squeeze. The 4% stop stays as the rail.", candles, i, status: status);
                }
            }

            var openFunding = LatestFunding(context.FundingRate);
            if (openFunding is null)
            {
                return Detail(SignalType.Hold, "Funding is missing. The 4% stop and 8% take own the exit.", candles, i, status: status);
            }

            if (IsLong(context) && openFunding > -fundingExtreme)
            {
                return Detail(SignalType.Exit, "Crowded-short funding faded.", candles, i, status: status);
            }

            if (!IsLong(context) && openFunding < fundingExtreme)
            {
                return Detail(SignalType.Exit, "Crowded-long funding faded.", candles, i, status: status);
            }

            return Detail(SignalType.Hold, "Funding is still extreme. The 4% stop and 8% take stay as the rail.", candles, i, status: status);
        }

        if (i < window - 1)
        {
            return Detail(SignalType.NoAction, "Need 24 closed bars.", candles, i, status: status);
        }

        var then = candles[i - window + 1].Close;
        var now = candles[i].Close;
        if (then <= 0m)
        {
            return Detail(SignalType.NoAction, "Price window is empty.", candles, i, status: status);
        }

        var priceChange = (now - then) / then;
        if (Math.Abs(priceChange) > priceBand)
        {
            return Detail(SignalType.NoAction, "Price already moved more than 3% in 24 hours.", candles, i, status: status);
        }

        var interest = OpenInterestChange(context.OpenInterest);
        if (interest is null)
        {
            return Detail(SignalType.NoAction, "Open interest is missing. No order.", candles, i, status: status);
        }

        if (interest < openInterestRise)
        {
            return Detail(SignalType.NoAction, "Open interest did not rise 15% while price was quiet.", candles, i, status: status);
        }

        var funding = LatestFunding(context.FundingRate);
        if (funding is null)
        {
            return Detail(SignalType.NoAction, "Funding is missing. No order.", candles, i, status: status);
        }

        if (funding <= -fundingExtreme)
        {
            return Detail(SignalType.Buy, "Price is quiet, open interest rose, funding is at or below -0.10%. Crowded shorts.", candles, i, status: status);
        }

        if (funding >= fundingExtreme)
        {
            return Detail(SignalType.Sell, "Price is quiet, open interest rose, funding is at or above +0.10%. Crowded longs.", candles, i, status: status);
        }

        return Detail(SignalType.NoAction, "Funding is not at a ±0.10% extreme.", candles, i, status: status);
    }

    private static StrategySignalDetail ImpulseCatch(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context)
    {
        const string status = "LIVE_PROBE";
        const int window = 16;
        const int volumeLookback = 20;
        const decimal largeMove = 0.08m;
        const decimal giveback = 0.03m;
        const decimal volumeMultiple = 1.5m;
        if (i < volumeLookback || i < window + 1)
        {
            return Detail(SignalType.NoAction, "Need enough closed 15-minute bars.", candles, i, status: status);
        }

        if (context.HasOpenPosition)
        {
            var previous = candles[i - 1].Close;
            var give = previous <= 0m ? 0m : (candles[i].Close - previous) / previous;
            if (give <= -giveback)
            {
                return Detail(SignalType.Exit, "The last bar gave back 3%.", candles, i, status: status);
            }

            return Detail(SignalType.Hold, "The rise is still open.", candles, i, status: status);
        }

        var rise = Rise(candles, i, window);
        var priorRise = Rise(candles, i - 1, window);
        if (rise is not { } now || priorRise is not { } before)
        {
            return Detail(SignalType.NoAction, "The price window is empty.", candles, i, status: status);
        }

        if (now < largeMove)
        {
            return Detail(SignalType.NoAction, "The rise is still under 8%.", candles, i, status: status);
        }

        if (before >= largeMove)
        {
            return Detail(SignalType.NoAction, "The 8% rise was already in place.", candles, i, status: status);
        }

        var bar = candles[i];
        if (bar.Close <= bar.Open)
        {
            return Detail(SignalType.NoAction, "The 15-minute bar did not close higher.", candles, i, status: status);
        }

        if (bar.High > bar.Low && (bar.Close - bar.Low) / (bar.High - bar.Low) < 0.5m)
        {
            return Detail(SignalType.NoAction, "The bar closed back in its lower half.", candles, i, status: status);
        }

        var average = 0m;
        for (var k = i - volumeLookback; k < i; k++)
        {
            average += candles[k].Volume;
        }

        average /= volumeLookback;
        if (average <= 0m || bar.Volume < average * volumeMultiple)
        {
            return Detail(SignalType.NoAction, "Volume is not above the recent average.", candles, i, status: status);
        }

        return Detail(
            SignalType.Buy,
            "Price just rose " + now.ToString("0.0%", System.Globalization.CultureInfo.InvariantCulture) + " and the 15-minute bar closed higher.",
            candles,
            i,
            status: status);
    }

    private static decimal? Rise(IReadOnlyList<MarketCandle> candles, int end, int bars)
    {
        var start = end - bars;
        if (start < 0 || candles[start].Close <= 0m)
        {
            return null;
        }

        return (candles[end].Close - candles[start].Close) / candles[start].Close;
    }

    private static decimal? OpenInterestChange(IReadOnlyList<decimal?>? openInterest)
    {
        if (openInterest is null || openInterest.Count < 2)
        {
            return null;
        }

        // The live bot passes two prints: open interest a day ago, and the latest print.
        // A candle-aligned history is longer, so the same 24-hour window is the print 24 bars back.
        var priorIndex = openInterest.Count > 24 ? openInterest.Count - 24 : 0;
        var previous = openInterest[priorIndex];
        var latest = openInterest[^1];
        if (previous is not { } prior || latest is not { } now || prior <= 0m || now <= 0m)
        {
            return null;
        }

        return (now - prior) / prior;
    }

    private static decimal? LatestFunding(IReadOnlyList<decimal?>? funding)
    {
        if (funding is null)
        {
            return null;
        }

        for (var k = funding.Count - 1; k >= 0; k--)
        {
            if (funding[k] is { } rate)
            {
                return rate;
            }
        }

        return null;
    }

    private static StrategySignalDetail BtcDailyMax(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context)
    {
        const string status = "HISTORICALLY_FITTED_CANDIDATE";
        const int lookback = 10;
        var closes = UtcDailyCloses(candles, i);
        if (closes.Count < lookback)
        {
            return Detail(SignalType.NoAction, "Need 10 closed UTC days.", candles, i, status: status);
        }

        var atHigh = IsWindowHigh(closes, closes.Count - 1, lookback);
        if (context.HasOpenPosition)
        {
            if (!IsLong(context))
            {
                return Detail(SignalType.Exit, "The 10-day high rule is long only.", candles, i, status: status);
            }

            if (!atHigh)
            {
                return Detail(SignalType.Exit, "The close is no longer a 10-day high.", candles, i, status: status);
            }

            return Detail(SignalType.Hold, "Long. The close is still a 10-day high.", candles, i, status: status);
        }

        if (atHigh)
        {
            return Detail(SignalType.Buy, "Close is the 10-day high. Long the next day. No short.", candles, i, status: status);
        }

        return Detail(SignalType.NoAction, "Close is not a 10-day high.", candles, i, status: status);
    }

    private static bool IsWindowHigh(IReadOnlyList<decimal> closes, int day, int lookback)
    {
        if (day < lookback - 1 || closes[day] <= 0m)
        {
            return false;
        }

        var high = closes[day];
        for (var k = 1; k < lookback; k++)
        {
            if (closes[day - k] > high)
            {
                return false;
            }
        }

        return true;
    }

    private static StrategySignalDetail TsMomentum(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context)
    {
        const string status = "HISTORICALLY_FITTED_CANDIDATE";
        const int lookback = 28;
        const int hold = 5;
        var closes = UtcDailyCloses(candles, i);
        if (closes.Count < lookback + 2)
        {
            return Detail(SignalType.NoAction, "Need more than 28 closed UTC days.", candles, i, status: status);
        }

        var last = closes.Count - 1;
        var sleeves = 0;
        for (var lag = 0; lag < hold; lag++)
        {
            if (InOwnTopThird(closes, last - lag, lookback))
            {
                sleeves++;
            }
        }

        if (context.HasOpenPosition)
        {
            if (!IsLong(context))
            {
                return Detail(SignalType.Exit, "Time-series momentum is long only.", candles, i, status: status);
            }

            if (sleeves == 0)
            {
                return Detail(SignalType.Exit, "28-day return left the top third. The five-day sleeve is flat.", candles, i, status: status);
            }

            return Detail(SignalType.Hold, $"Long. {sleeves}/5 daily sleeves are on.", candles, i, status: status);
        }

        if (sleeves > 0)
        {
            return Detail(SignalType.Buy, $"28-day return is in the top third of its own history. {sleeves}/5 sleeves. Long only.", candles, i, status: status);
        }

        return Detail(SignalType.NoAction, "28-day return is not in the top third of its own history.", candles, i, status: status);
    }

    private static List<decimal> UtcDailyCloses(IReadOnlyList<MarketCandle> candles, int i)
    {
        var byDay = new Dictionary<DateOnly, decimal>();
        var order = new List<DateOnly>();
        var last = Math.Min(i, candles.Count - 1);
        for (var k = 0; k <= last; k++)
        {
            var bar = candles[k];
            if (!bar.IsClosed || bar.Close <= 0m)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(bar.OpenTime.UtcDateTime);
            if (!byDay.ContainsKey(day))
            {
                order.Add(day);
            }

            byDay[day] = bar.Close;
        }

        var closes = new List<decimal>(order.Count);
        foreach (var day in order)
        {
            closes.Add(byDay[day]);
        }

        return closes;
    }

    private static bool InOwnTopThird(IReadOnlyList<decimal> closes, int day, int lookback)
    {
        if (day < lookback || day >= closes.Count || closes[day - lookback] <= 0m || closes[day] <= 0m)
        {
            return false;
        }

        var current = closes[day] / closes[day - lookback] - 1m;
        var history = 0;
        var below = 0;
        for (var t = lookback; t < day; t++)
        {
            if (closes[t - lookback] <= 0m || closes[t] <= 0m)
            {
                continue;
            }

            history++;
            if (closes[t] / closes[t - lookback] - 1m < current)
            {
                below++;
            }
        }

        return history > 0 && below * 3 >= history * 2;
    }

    private static StrategySignalDetail EmaCrossLong(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var fast = cache.Ema(p.EmaFast);
        var slow = cache.Ema(p.EmaSlow);
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && CrossesBelow(fast, slow, i))
            {
                return Detail(SignalType.Exit, "EMA20 crossed below EMA50.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
            }

            return Detail(SignalType.Hold, "Position open; EMA20 is still above EMA50.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
        }

        if (CrossesAbove(fast, slow, i))
        {
            return Detail(SignalType.Buy, "EMA20 crossed above EMA50. Long only.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
        }

        return Detail(SignalType.NoAction, "No EMA20 cross above EMA50.", candles, i, status: "HISTORICALLY_FITTED_CANDIDATE");
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
