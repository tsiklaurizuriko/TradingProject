using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// Live/research signals from confirmed pattern events only. Forward MFE/MAE is never read here.
/// Isolated Risk Engine still owns size/SL/TP. These returns are hypotheses, not promotion.
/// </summary>
public static class PriceActionStrategyEvaluator
{
    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "Position open; Isolated book owns SL/TP.", candles, i);
        }

        var book = cache.PriceAction();
        return p.TemplateKey switch
        {
            StrategyTemplateKeys.PaWDoubleBottom => Confirmed(book, i, PatternKinds.WDoubleBottom, true, candles),
            StrategyTemplateKeys.PaMDoubleTop => Confirmed(book, i, PatternKinds.MDoubleTop, false, candles),
            StrategyTemplateKeys.PaBullFlag => Confirmed(book, i, PatternKinds.BullFlag, true, candles),
            StrategyTemplateKeys.PaBearFlag => Confirmed(book, i, PatternKinds.BearFlag, false, candles),
            StrategyTemplateKeys.PaPennant => Directional(book, i, PatternKinds.Pennant, candles),
            StrategyTemplateKeys.PaAscendingTriangle => Directional(book, i, PatternKinds.AscendingTriangle, candles),
            StrategyTemplateKeys.PaDescendingTriangle => Directional(book, i, PatternKinds.DescendingTriangle, candles),
            StrategyTemplateKeys.PaSymmetricalTriangle => Directional(book, i, PatternKinds.SymmetricalTriangle, candles),
            StrategyTemplateKeys.PaRisingWedge => Directional(book, i, PatternKinds.RisingWedge, candles),
            StrategyTemplateKeys.PaFallingWedge => Directional(book, i, PatternKinds.FallingWedge, candles),
            StrategyTemplateKeys.PaRectangleBreakout => Directional(book, i, PatternKinds.Rectangle, candles),
            StrategyTemplateKeys.PaBreakoutRetest => Directional(book, i, PatternKinds.BreakoutRetest, candles),
            StrategyTemplateKeys.PaLiquiditySweep => Sweep(book, i, candles),
            StrategyTemplateKeys.PaHeadShoulders => Confirmed(book, i, PatternKinds.HeadShoulders, false, candles),
            StrategyTemplateKeys.PaInverseHeadShoulders => Confirmed(book, i, PatternKinds.InverseHeadShoulders, true, candles),
            StrategyTemplateKeys.PaCandleSequence => SequenceFade(book, i, candles),
            StrategyTemplateKeys.PaStructureBreak => Bos(book, i, candles),
            StrategyTemplateKeys.PaFailedBreakout => Directional(book, i, PatternKinds.FailedBreakout, candles),
            _ => Detail(SignalType.NoAction, "Unknown price-action template.", candles, i, "IMPLEMENTATION_ERROR")
        };
    }

    private static StrategySignalDetail Confirmed(
        PriceActionBook book,
        int i,
        string kind,
        bool buy,
        IReadOnlyList<MarketCandle> candles)
    {
        if (book.ConfirmedAt(i, kind).Any())
        {
            return Detail(buy ? SignalType.Buy : SignalType.Sell, $"Confirmed {kind} at this close. Event, not a textbook direction claim.", candles, i);
        }

        return Detail(SignalType.NoAction, $"{kind} not confirmed on this bar.", candles, i);
    }

    private static StrategySignalDetail Directional(PriceActionBook book, int i, string kind, IReadOnlyList<MarketCandle> candles)
    {
        var hit = book.ConfirmedAt(i, kind).LastOrDefault();
        if (hit is null)
        {
            return Detail(SignalType.NoAction, $"{kind} not confirmed on this bar.", candles, i);
        }

        var buy = string.Equals(hit.Direction, "BULLISH", StringComparison.OrdinalIgnoreCase);
        return Detail(buy ? SignalType.Buy : SignalType.Sell, $"Confirmed {kind} breakout direction={hit.Direction}. Measured, not assumed.", candles, i);
    }

    private static StrategySignalDetail Sweep(PriceActionBook book, int i, IReadOnlyList<MarketCandle> candles)
    {
        if (book.ConfirmedAt(i, PatternKinds.LiquiditySweepLow).Any())
        {
            return Detail(SignalType.Buy, "Confirmed local-low sweep with close back through the level (BULLISH_REJECTION event).", candles, i);
        }

        if (book.ConfirmedAt(i, PatternKinds.LiquiditySweepHigh).Any())
        {
            return Detail(SignalType.Sell, "Confirmed local-high sweep with close back through the level (BEARISH_REJECTION event).", candles, i);
        }

        return Detail(SignalType.NoAction, "No liquidity sweep confirmation on this bar.", candles, i);
    }

    private static StrategySignalDetail SequenceFade(PriceActionBook book, int i, IReadOnlyList<MarketCandle> candles)
    {
        if (i < 1)
        {
            return Detail(SignalType.NoAction, "Sequence warmup.", candles, i);
        }

        var seq = book.Sequences[i];
        if (seq.BullRun >= 3 && book.HasEvent(i, PatternKinds.BearishRejection))
        {
            return Detail(SignalType.Sell, "Bullish sequence then rejection wick. Fade hypothesis, not 3-green=long.", candles, i);
        }

        if (seq.BearRun >= 3 && book.HasEvent(i, PatternKinds.BullishRejection))
        {
            return Detail(SignalType.Buy, "Bearish sequence then rejection wick. Fade hypothesis, not 3-red=short.", candles, i);
        }

        return Detail(SignalType.NoAction, "No sequence+rejection event on this bar.", candles, i);
    }

    private static StrategySignalDetail Bos(PriceActionBook book, int i, IReadOnlyList<MarketCandle> candles)
    {
        var s = book.Structure[i];
        if (s.BosBull)
        {
            return Detail(SignalType.Buy, "Causal BOS through last confirmed swing high.", candles, i);
        }

        if (s.BosBear)
        {
            return Detail(SignalType.Sell, "Causal BOS through last confirmed swing low.", candles, i);
        }

        return Detail(SignalType.NoAction, "No BOS on this bar.", candles, i);
    }

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        IReadOnlyList<MarketCandle> candles,
        int i,
        string status = "RESEARCHING") =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime, Status: status);
}
