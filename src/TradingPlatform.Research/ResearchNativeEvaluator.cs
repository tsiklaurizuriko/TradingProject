using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public static class ResearchNativeEvaluator
{
    public static SignalType EvaluateAt(
        ResearchCandidate candidate,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        var detail = EvaluateDetailAt(candidate, context, cache, index);
        reason = detail.Reason;
        return detail.Signal;
    }

    public static StrategySignalDetail EvaluateDetailAt(
        ResearchCandidate candidate,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index)
    {
        var candles = cache.Candles;
        if (index < 1 || index >= candles.Count)
        {
            return new StrategySignalDetail(SignalType.NoAction, "Not enough closed candles.");
        }

        if (Btc15mFittedEvaluator.Handles(candidate.NativeKey))
        {
            return Btc15mFittedEvaluator.Evaluate(candidate, candles, cache, index, context);
        }

        if (Wave2NativeEvaluator.Handles(candidate.NativeKey))
        {
            return Wave2NativeEvaluator.Evaluate(candidate, candles, cache, index, context);
        }

        return candidate.NativeKey switch
        {
            "vwap_reclaim" => Wrap(VwapReclaim(candidate, candles, cache, index, context, out var r1), r1, candles, index),
            "supertrend_ema" => Wrap(SupertrendEma(candidate, candles, cache, index, context, out var r2), r2, candles, index),
            "trend_pullback" => Wrap(TrendPullback(candidate, candles, cache, index, context, out var r3), r3, candles, index),
            _ => new StrategySignalDetail(SignalType.NoAction, "Unknown native research template.")
        };
    }

    private static StrategySignalDetail Wrap(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i) =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime);

    private static SignalType VwapReclaim(
        ResearchCandidate candidate,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int i,
        StrategyContext context,
        out string reason)
    {
        var vwap = cache.SessionVwap();
        if (vwap[i] is not { } now || vwap[i - 1] is not { } prev)
        {
            reason = "VWAP not ready.";
            return SignalType.NoAction;
        }

        var slope = now - prev;
        var close = candles[i].Close;
        var prevClose = candles[i - 1].Close;
        var longReclaim = slope > 0m && prevClose <= prev && close > now;
        var shortReclaim = slope < 0m && prevClose >= prev && close < now;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && (shortReclaim || slope < 0m))
            {
                reason = "VWAP slope reversed or opposite reclaim.";
                return SignalType.Exit;
            }

            if (!IsLong(context) && (longReclaim || slope > 0m))
            {
                reason = "VWAP slope reversed or opposite reclaim.";
                return SignalType.Exit;
            }

            reason = "Position open; VWAP exit not triggered.";
            return SignalType.Hold;
        }

        if (longReclaim)
        {
            reason = "Close reclaimed session VWAP with positive slope.";
            return SignalType.Buy;
        }

        if (shortReclaim)
        {
            reason = "Close lost session VWAP with negative slope.";
            return SignalType.Sell;
        }

        reason = "VWAP reclaim not matched.";
        return SignalType.NoAction;
    }

    private static SignalType SupertrendEma(
        ResearchCandidate candidate,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int i,
        StrategyContext context,
        out string reason)
    {
        var st = cache.SupertrendDirection(candidate.Native.SupertrendPeriod, candidate.Native.SupertrendMultiplier);
        var fast = cache.Ema(candidate.Filters.EmaFast);
        var slow = cache.Ema(candidate.Filters.EmaSlow);
        if (st[i] is not { } dir || fast[i] is not { } f || slow[i] is not { } s)
        {
            reason = "Supertrend/EMA not ready.";
            return SignalType.NoAction;
        }

        var close = candles[i].Close;
        var longOk = dir > 0m && f > s && close > s;
        var shortOk = dir < 0m && f < s && close < s;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && !longOk)
            {
                reason = "Supertrend or EMA structure no longer bullish.";
                return SignalType.Exit;
            }

            if (!IsLong(context) && !shortOk)
            {
                reason = "Supertrend or EMA structure no longer bearish.";
                return SignalType.Exit;
            }

            reason = "Position open; Supertrend exit not triggered.";
            return SignalType.Hold;
        }

        if (longOk)
        {
            reason = "Supertrend bullish with EMA20 > EMA50 and close > EMA50.";
            return SignalType.Buy;
        }

        if (shortOk)
        {
            reason = "Supertrend bearish with EMA20 < EMA50 and close < EMA50.";
            return SignalType.Sell;
        }

        reason = "Supertrend + EMA entry not matched.";
        return SignalType.NoAction;
    }

    private static SignalType TrendPullback(
        ResearchCandidate candidate,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int i,
        StrategyContext context,
        out string reason)
    {
        var fast = cache.Ema(candidate.Filters.EmaFast);
        var slow = cache.Ema(candidate.Filters.EmaSlow);
        var slope = cache.EmaSlope(candidate.Filters.EmaSlow);
        var rsi = cache.Rsi(candidate.Native.RsiPeriod);
        if (fast[i] is not { } f || slow[i] is not { } s || slope[i] is not { } sl || rsi[i] is not { } r || rsi[i - 1] is not { } rp)
        {
            reason = "Trend-pullback indicators not ready.";
            return SignalType.NoAction;
        }

        var bar = candles[i];
        var longTrend = f > s && sl > 0m;
        var shortTrend = f < s && sl < 0m;
        var longPb = longTrend && bar.Low <= f && bar.Close > f && rp <= candidate.Native.RsiLongRecover && r > candidate.Native.RsiLongRecover;
        var shortPb = shortTrend && bar.High >= f && bar.Close < f && rp >= candidate.Native.RsiShortRecover && r < candidate.Native.RsiShortRecover;
        if (context.HasOpenPosition)
        {
            if (IsLong(context) && (shortTrend || r < 50m && rp >= 50m))
            {
                reason = "Trend reversed or RSI lost 50.";
                return SignalType.Exit;
            }

            if (!IsLong(context) && (longTrend || r > 50m && rp <= 50m))
            {
                reason = "Trend reversed or RSI recrossed 50.";
                return SignalType.Exit;
            }

            reason = "Position open; trend-pullback exit not triggered.";
            return SignalType.Hold;
        }

        if (longPb)
        {
            reason = "Bullish EMA trend, pullback to EMA20, RSI recovered.";
            return SignalType.Buy;
        }

        if (shortPb)
        {
            reason = "Bearish EMA trend, pullback to EMA20, RSI recovered.";
            return SignalType.Sell;
        }

        reason = "Trend pullback not matched.";
        return SignalType.NoAction;
    }

    private static bool IsLong(StrategyContext context) => context.PositionSide == PositionSide.Long;
}
