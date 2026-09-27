using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Rules copied from the two research repositories. Existing templates do not call this type.
/// </summary>
public static class ImportedRuleEvaluator
{
    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        p.TemplateKey switch
        {
            StrategyTemplateKeys.MacContrarian710 => MacContrarian(p, candles, i, context, cache),
            StrategyTemplateKeys.ZigZagFade => ZigZagFade(p, candles, i, context, cache),
            StrategyTemplateKeys.DonchianV2 => DonchianV2(p, candles, i, context, cache),
            StrategyTemplateKeys.BinHv45 => BinHv45(candles, i, context, cache),
            StrategyTemplateKeys.ClucMay72018 => ClucMay72018(candles, i, context, cache),
            StrategyTemplateKeys.CombinedBinHCluc => CombinedBinHCluc(candles, i, context, cache),
            StrategyTemplateKeys.Hlhb => Hlhb(candles, i, context, cache),
            StrategyTemplateKeys.FAdxSma => FAdxSma(candles, i, context, cache),
            StrategyTemplateKeys.TripleSupertrend => TripleSupertrend(candles, i, context, cache),
            _ => new StrategySignalDetail(SignalType.NoAction, "Unknown imported rule.", candles[i].CloseTime, Status: "IMPLEMENTATION_ERROR")
        };

    /// <summary>
    /// octopus444 MAc(7,10,0.01,0,0). SMA fast above slow*(1+b) is a raw long, then the sign is flipped.
    /// Inside the band the previous raw signal is kept. Flat only before the first signal. No stop.
    /// </summary>
    private static StrategySignalDetail MacContrarian(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var position = cache.ContrarianSmaPosition(p.EmaFast, p.EmaSlow, p.PriceChangeThreshold);
        var desired = position[i];
        if (desired == 0m)
        {
            return Detail(SignalType.NoAction, "SMA band has not produced a position yet.", candles, i);
        }

        var wantLong = desired > 0m;
        if (!context.HasOpenPosition)
        {
            return wantLong
                ? Detail(SignalType.Buy, "Contrarian SMA band is long. Fast SMA is below the slow band.", candles, i)
                : Detail(SignalType.Sell, "Contrarian SMA band is short. Fast SMA is above the slow band.", candles, i);
        }

        var isLong = context.PositionSide != PositionSide.Short;
        if (isLong == wantLong)
        {
            return Detail(SignalType.Hold, "Contrarian SMA band is unchanged.", candles, i);
        }

        return wantLong
            ? Detail(SignalType.Buy, "Contrarian SMA band flipped to long.", candles, i)
            : Detail(SignalType.Sell, "Contrarian SMA band flipped to short.", candles, i);
    }

    /// <summary>
    /// Eric-Prod zigzag_fade.pine. Pivot length on both sides. Fade a close through the previous swing
    /// when the swing range is at least the deviation percent. ATR stop is close ± multiplier × ATR, replaced every bar.
    /// </summary>
    private static StrategySignalDetail ZigZagFade(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var length = Math.Max(2, p.SwingLength);
        var deviation = ZigZagDeviationPercent(context.Symbol, p.PriceChangeThreshold);
        var highs = cache.ConfirmedSwingHigh(length);
        var lows = cache.ConfirmedSwingLow(length);
        var atr = cache.Atr(p.AtrPeriod);
        var high = highs[i];
        var low = lows[i];
        var prevHigh = i > 0 ? highs[i - 1] : null;
        var prevLow = i > 0 ? lows[i - 1] : null;
        var close = candles[i].Close;
        var prevClose = candles[i - 1].Close;
        var mid = high is { } h && low is { } l ? (h + l) / 2m : (decimal?)null;
        var deviationOk = high is { } swingHigh && low is { } swingLow && mid is > 0m
            && (swingHigh - swingLow) / mid.Value * 100m >= deviation;
        var shortFade = deviationOk && high is { } fadeHigh && prevHigh is { } priorHigh
            && close > fadeHigh && prevClose <= priorHigh;
        var longFade = deviationOk && low is { } fadeLow && prevLow is { } priorLow
            && close < fadeLow && prevClose >= priorLow;
        decimal? stop = atr[i] is { } atrNow
            ? (longFade || (context.HasOpenPosition && context.PositionSide != PositionSide.Short && !shortFade)
                ? close - p.AtrStopMultiplier * atrNow
                : close + p.AtrStopMultiplier * atrNow)
            : null;

        if (shortFade && longFade)
        {
            shortFade = true;
            longFade = false;
        }

        if (context.HasOpenPosition)
        {
            var isLong = context.PositionSide != PositionSide.Short;
            if (isLong && shortFade)
            {
                return Detail(SignalType.Sell, "ZigZag fade: close broke the prior swing high.", candles, i, stop);
            }

            if (!isLong && longFade)
            {
                return Detail(SignalType.Buy, "ZigZag fade: close broke the prior swing low.", candles, i, stop);
            }

            return Detail(SignalType.Hold, "ZigZag fade position is open. ATR stop follows the close.", candles, i, isLong ? StopForLong(close, atr, i, p) : StopForShort(close, atr, i, p));
        }

        if (longFade)
        {
            return Detail(SignalType.Buy, "ZigZag fade: close broke the prior swing low.", candles, i, stop);
        }

        if (shortFade)
        {
            return Detail(SignalType.Sell, "ZigZag fade: close broke the prior swing high.", candles, i, stop);
        }

        return Detail(SignalType.NoAction, "ZigZag fade breakout is not confirmed.", candles, i);
    }

    private static decimal ZigZagDeviationPercent(string? symbol, decimal fallback)
    {
        if (string.Equals(symbol, "ETHUSDT", StringComparison.OrdinalIgnoreCase))
        {
            return 6m;
        }

        if (string.Equals(symbol, "SOLUSDT", StringComparison.OrdinalIgnoreCase))
        {
            return 5m;
        }

        return fallback;
    }

    /// <summary>
    /// Eric-Prod donchian_breakout_v2.pine. Entry is the prior channel. Exit is the shorter prior channel.
    /// ATR stop is close ± multiplier × ATR and is replaced every bar. Take-profit and time stop stay off.
    /// </summary>
    private static StrategySignalDetail DonchianV2(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var (entryHigh, entryLow) = cache.Donchian(Math.Max(5, p.EntryLookback));
        var (exitHigh, exitLow) = cache.Donchian(Math.Max(2, p.ExitLookback));
        var atr = cache.Atr(p.AtrPeriod);
        var close = candles[i].Close;
        if (entryHigh[i] is not { } hi || entryLow[i] is not { } lo)
        {
            return Detail(SignalType.NoAction, "Donchian v2 entry channel is not ready.", candles, i);
        }

        var longBreak = close > hi;
        var shortBreak = close < lo;
        var exitLong = exitLow[i] is { } exitLo && close < exitLo;
        var exitShort = exitHigh[i] is { } exitHi && close > exitHi;
        if (context.HasOpenPosition)
        {
            var isLong = context.PositionSide != PositionSide.Short;
            if (isLong && exitLong)
            {
                return Detail(SignalType.Exit, "Donchian v2 close broke the exit-channel low.", candles, i);
            }

            if (!isLong && exitShort)
            {
                return Detail(SignalType.Exit, "Donchian v2 close broke the exit-channel high.", candles, i);
            }

            if (isLong && shortBreak)
            {
                return Detail(SignalType.Sell, "Donchian v2 close broke the entry-channel low.", candles, i, StopForShort(close, atr, i, p));
            }

            if (!isLong && longBreak)
            {
                return Detail(SignalType.Buy, "Donchian v2 close broke the entry-channel high.", candles, i, StopForLong(close, atr, i, p));
            }

            return Detail(SignalType.Hold, "Donchian v2 position is open. ATR stop follows the close.", candles, i, isLong ? StopForLong(close, atr, i, p) : StopForShort(close, atr, i, p));
        }

        if (longBreak && !shortBreak)
        {
            return Detail(SignalType.Buy, "Donchian v2 close broke the entry-channel high.", candles, i, StopForLong(close, atr, i, p));
        }

        if (shortBreak)
        {
            return Detail(SignalType.Sell, "Donchian v2 close broke the entry-channel low.", candles, i, StopForShort(close, atr, i, p));
        }

        return Detail(SignalType.NoAction, "Donchian v2 entry is not matched.", candles, i);
    }

    /// <summary>
    /// freqtrade BinHV45. Close Bollinger(40, 2). Published gates are 0.008, 0.0175 and a wick under 0.25 of the band.
    /// No separate exit signal. minimal_roi 1.25%, stoploss 5%. Long only.
    /// </summary>
    private static StrategySignalDetail BinHv45(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        MeanReversion(
            candles,
            i,
            context,
            cache,
            binhv: true,
            cluc: false,
            exitOnMiddle: false,
            bbRatio: 0.008m,
            closeRatio: 0.0175m,
            tailRatio: 0.25m,
            stopPct: 0.05m,
            roiPct: 0.0125m,
            exitProfitOnly: false,
            entryReason: "BinHV45 close is under the prior lower band with a short lower wick.",
            exitReason: "BinHV45 minimal ROI or 5% stop.");

    /// <summary>freqtrade ClucMay72018. Typical-price Bollinger(20, 2), EMA(50), volume cap. ROI 1%, stop 5%.</summary>
    private static StrategySignalDetail ClucMay72018(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        MeanReversion(
            candles,
            i,
            context,
            cache,
            binhv: false,
            cluc: true,
            exitOnMiddle: true,
            bbRatio: 0m,
            closeRatio: 0m,
            tailRatio: 0m,
            stopPct: 0.05m,
            roiPct: 0.01m,
            exitProfitOnly: false,
            entryReason: "Cluc close is under EMA(50) and 98.5% of the lower band.",
            exitReason: "Cluc close crossed the middle band, or the 1% ROI / 5% stop.");

    /// <summary>freqtrade CombinedBinHAndCluc. BinHV constants 0.008 / 0.0175 / 0.25 or Cluc. Exit only in profit. ROI 5%, stop 5%.</summary>
    private static StrategySignalDetail CombinedBinHCluc(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache) =>
        MeanReversion(
            candles,
            i,
            context,
            cache,
            binhv: true,
            cluc: true,
            exitOnMiddle: true,
            bbRatio: 0.008m,
            closeRatio: 0.0175m,
            tailRatio: 0.25m,
            stopPct: 0.05m,
            roiPct: 0.05m,
            exitProfitOnly: true,
            entryReason: "Combined BinH or Cluc long.",
            exitReason: "Combined close is above the middle band in profit, or the 5% ROI / 5% stop.");

    private static StrategySignalDetail MeanReversion(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache,
        bool binhv,
        bool cluc,
        bool exitOnMiddle,
        decimal bbRatio,
        decimal closeRatio,
        decimal tailRatio,
        decimal stopPct,
        decimal roiPct,
        bool exitProfitOnly,
        string entryReason,
        string exitReason)
    {
        if (context.HasOpenPosition && context.PositionSide != PositionSide.Short)
        {
            if (PriceTargetHit(context, candles[i], stopPct, roiPct))
            {
                return Detail(SignalType.Exit, exitReason, candles, i);
            }

            if (exitOnMiddle)
            {
                var bands = cache.SampleBollinger(20, 2m, typicalPrice: true);
                var inProfit = context.AverageEntryPrice is not { } entry || candles[i].Close > entry;
                if (bands.Mid[i] is { } middle && candles[i].Close > middle && (!exitProfitOnly || inProfit))
                {
                    return Detail(SignalType.Exit, exitReason, candles, i);
                }
            }

            return Detail(SignalType.Hold, "Freqtrade long is still open.", candles, i);
        }

        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "Freqtrade rule is long only.", candles, i);
        }

        var binhvHit = binhv && BinHvEntry(candles, i, cache, bbRatio, closeRatio, tailRatio);
        var clucHit = cluc && ClucEntry(candles, i, cache);
        if (!binhvHit && !clucHit)
        {
            return Detail(SignalType.NoAction, "Freqtrade long entry is not matched.", candles, i);
        }

        var close = candles[i].Close;
        return Detail(SignalType.Buy, entryReason, candles, i, close * (1m - stopPct), close * (1m + roiPct));
    }

    private static bool PriceTargetHit(StrategyContext context, MarketCandle bar, decimal stopPct, decimal roiPct)
    {
        if (context.AverageEntryPrice is not { } entry || entry <= 0m)
        {
            return false;
        }

        return bar.Low <= entry * (1m - stopPct) || bar.High >= entry * (1m + roiPct);
    }

    private static bool BinHvEntry(
        IReadOnlyList<MarketCandle> candles,
        int i,
        CausalIndicatorCache cache,
        decimal bbRatio,
        decimal closeRatio,
        decimal tailRatio)
    {
        if (i < 1)
        {
            return false;
        }

        var (mid, _, lower) = cache.SampleBollinger(40, 2m, typicalPrice: false);
        if (lower[i - 1] is not { } previousLower || previousLower <= 0m || mid[i] is not { } midNow || lower[i] is not { } lowerNow)
        {
            return false;
        }

        var close = candles[i].Close;
        var bandWidth = Math.Abs(midNow - lowerNow);
        var closeMove = Math.Abs(close - candles[i - 1].Close);
        var tail = Math.Abs(close - candles[i].Low);
        return bandWidth > close * bbRatio
            && closeMove > close * closeRatio
            && tail < bandWidth * tailRatio
            && close < previousLower
            && close <= candles[i - 1].Close;
    }

    private static bool ClucEntry(IReadOnlyList<MarketCandle> candles, int i, CausalIndicatorCache cache)
    {
        var ema = cache.Ema(50);
        var (_, _, lower) = cache.SampleBollinger(20, 2m, typicalPrice: true);
        var volume = cache.PriorVolumeMean(30);
        if (ema[i] is not { } slow || lower[i] is not { } band || volume[i] is not { } meanVolume)
        {
            return false;
        }

        var close = candles[i].Close;
        return close < slow && close < 0.985m * band && candles[i].Volume < meanVolume * 20m;
    }

    /// <summary>
    /// freqtrade hlhb. RSI of (open+close)/2 crosses 50 and EMA(5) crosses EMA(10) on the same bar, ADX above 25.
    /// Long only. The hyperopt ROI table and 32% stop are not part of the signal.
    /// </summary>
    private static StrategySignalDetail Hlhb(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < 1)
        {
            return Detail(SignalType.NoAction, "HLHB needs a previous bar.", candles, i);
        }

        var rsi = cache.OpenCloseMidRsi(10);
        var fast = cache.Ema(5);
        var slow = cache.Ema(10);
        var adx = cache.Adx(14);
        if (rsi[i] is not { } nowRsi || rsi[i - 1] is not { } prevRsi
            || fast[i] is not { } nowFast || fast[i - 1] is not { } prevFast
            || slow[i] is not { } nowSlow || slow[i - 1] is not { } prevSlow
            || adx[i] is not { } nowAdx)
        {
            return Detail(SignalType.NoAction, "HLHB indicators are not ready.", candles, i);
        }

        var crossedUp = nowRsi > 50m && prevRsi <= 50m && nowFast > nowSlow && prevFast <= prevSlow && nowAdx > 25m && candles[i].Volume > 0m;
        var crossedDown = nowRsi < 50m && prevRsi >= 50m && nowFast < nowSlow && prevFast >= prevSlow && nowAdx > 25m && candles[i].Volume > 0m;
        if (context.HasOpenPosition && context.PositionSide != PositionSide.Short)
        {
            return crossedDown
                ? Detail(SignalType.Exit, "HLHB RSI and EMA crossed back down with ADX above 25.", candles, i)
                : Detail(SignalType.Hold, "HLHB long is still open.", candles, i);
        }

        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "HLHB is long only.", candles, i);
        }

        return crossedUp
            ? Detail(SignalType.Buy, "HLHB RSI and EMA crossed up with ADX above 25.", candles, i)
            : Detail(SignalType.NoAction, "HLHB entry is not matched.", candles, i);
    }

    /// <summary>freqtrade FAdxSmaStrategy. SMA(12) cross SMA(48) with ADX(14) above 30. Exit when ADX falls under 30. ROI 5%, stop 5%.</summary>
    private static StrategySignalDetail FAdxSma(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (i < 48)
        {
            return Detail(SignalType.NoAction, "ADX SMA needs 48 bars.", candles, i);
        }

        var fast = Sma(candles, 12);
        var slow = Sma(candles, 48);
        var adx = cache.Adx(14);
        if (fast[i] is not { } nowFast || fast[i - 1] is not { } prevFast
            || slow[i] is not { } nowSlow || slow[i - 1] is not { } prevSlow
            || adx[i] is not { } nowAdx)
        {
            return Detail(SignalType.NoAction, "ADX SMA indicators are not ready.", candles, i);
        }

        if (context.HasOpenPosition && nowAdx < 30m)
        {
            return Detail(SignalType.Exit, "ADX fell below 30.", candles, i);
        }

        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "ADX SMA position stays open while ADX is at least 30.", candles, i);
        }

        var crossUp = prevFast <= prevSlow && nowFast > nowSlow && nowAdx > 30m;
        var crossDown = prevFast >= prevSlow && nowFast < nowSlow && nowAdx > 30m;
        if (crossUp)
        {
            return Detail(SignalType.Buy, "SMA(12) crossed above SMA(48) with ADX above 30.", candles, i);
        }

        if (crossDown)
        {
            return Detail(SignalType.Sell, "SMA(12) crossed below SMA(48) with ADX above 30.", candles, i);
        }

        return Detail(SignalType.NoAction, "ADX SMA entry is not matched.", candles, i);
    }

    /// <summary>
    /// freqtrade FSupertrendStrategy buy_params. Long when 8/4, 9/7 and 8/1 are up.
    /// Short when 16/1, 18/3 and 18/6 are down. Exit long on 18/3 down, exit short on 9/7 up.
    /// </summary>
    private static StrategySignalDetail TripleSupertrend(
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var longA = cache.SupertrendDirection(8, 4m);
        var longB = cache.SupertrendDirection(9, 7m);
        var longC = cache.SupertrendDirection(8, 1m);
        var shortA = cache.SupertrendDirection(16, 1m);
        var shortB = cache.SupertrendDirection(18, 3m);
        var shortC = cache.SupertrendDirection(18, 6m);
        if (longA[i] is not { } a || longB[i] is not { } b || longC[i] is not { } c
            || shortA[i] is not { } d || shortB[i] is not { } e || shortC[i] is not { } f
            || candles[i].Volume <= 0m)
        {
            return Detail(SignalType.NoAction, "Triple Supertrend is not ready.", candles, i);
        }

        var longUp = a > 0m && b > 0m && c > 0m;
        var shortDown = d < 0m && e < 0m && f < 0m;
        if (context.HasOpenPosition && context.PositionSide != PositionSide.Short && e < 0m)
        {
            return Detail(SignalType.Exit, "Supertrend 18/3 turned down.", candles, i);
        }

        if (context.HasOpenPosition && context.PositionSide == PositionSide.Short && b > 0m)
        {
            return Detail(SignalType.Exit, "Supertrend 9/7 turned up.", candles, i);
        }

        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "Triple Supertrend position stays open.", candles, i);
        }

        if (longUp)
        {
            return Detail(SignalType.Buy, "Three long Supertrends are up.", candles, i);
        }

        if (shortDown)
        {
            return Detail(SignalType.Sell, "Three short Supertrends are down.", candles, i);
        }

        return Detail(SignalType.NoAction, "Triple Supertrend entry is not matched.", candles, i);
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

    private static decimal? StopForLong(decimal close, IReadOnlyList<decimal?> atr, int i, StrategyTemplateParams p) =>
        atr[i] is { } value ? close - p.AtrStopMultiplier * value : null;

    private static decimal? StopForShort(decimal close, IReadOnlyList<decimal?> atr, int i, StrategyTemplateParams p) =>
        atr[i] is { } value ? close + p.AtrStopMultiplier * value : null;

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal? stop = null,
        decimal? take = null) =>
        new(signal, reason, candles[i].CloseTime, stop, take, null, "IMPORTED_RULE");
}
