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
        CausalIndicatorCache cache)
    {
        if (StrategyTemplateKeys.IsCanonical(p.TemplateKey) || StrategyTemplateKeys.IsObsoleteAlias(p.TemplateKey))
        {
            return RefactoredStrategyEvaluator.Evaluate(p, candles, i, context, cache);
        }

        return p.TemplateKey switch
        {
            StrategyTemplateKeys.MacContrarian710 => MacContrarian(p, candles, i, context, cache),
            StrategyTemplateKeys.Hlhb => Hlhb(candles, i, context, cache),
            _ => new StrategySignalDetail(SignalType.NoAction, "Unknown imported rule.", candles[i].CloseTime, Status: "IMPLEMENTATION_ERROR")
        };
    }

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

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        decimal? stop = null,
        decimal? take = null) =>
        new(signal, reason, candles[i].CloseTime, stop, take, null, "IMPORTED_RULE");
}
