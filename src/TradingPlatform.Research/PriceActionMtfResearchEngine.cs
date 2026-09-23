using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

/// <summary>
/// Research-only causal MTF gate. The production Price Action evaluator remains unchanged.
/// Both auxiliary indices resolve to candles closed no later than the entry candle close.
/// </summary>
public sealed class PriceActionMtfResearchEngine : IStrategyEngine
{
    private readonly ResearchStrategyEngine _entry;
    private readonly CausalIndicatorCache? _confirmation;
    private readonly CausalIndicatorCache _context;
    private readonly ResearchFilters _filters;

    public PriceActionMtfResearchEngine(
        ResearchCandidate candidate,
        CausalIndicatorCache? confirmation,
        CausalIndicatorCache context)
    {
        _entry = new ResearchStrategyEngine(candidate with
        {
            Filters = candidate.Filters with
            {
                HigherTimeframe = null,
                ConfirmationTimeframe = null,
                ContextTimeframe = null
            }
        });
        _confirmation = confirmation;
        _context = context;
        _filters = candidate.Filters;
    }

    public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
    {
        var cache = new CausalIndicatorCache(context.ClosedCandles);
        return EvaluateAt(definition, context, cache, context.ClosedCandles.Count - 1, out reason);
    }

    public SignalType EvaluateAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        var detail = EvaluateDetailAt(definition, context, cache, index);
        reason = detail.Reason;
        return detail.Signal;
    }

    public StrategySignalDetail EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index)
    {
        var detail = _entry.EvaluateDetailAt(definition, context, cache, index);
        if (detail.Signal is not (SignalType.Buy or SignalType.Sell))
        {
            return detail;
        }

        var signalClose = cache.Candles[index].CloseTime;
        var longSide = detail.Signal == SignalType.Buy;
        if (_confirmation is not null)
        {
            var confirmationIndex = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(_confirmation, signalClose);
            if (confirmationIndex < 0)
            {
                return detail with { Signal = SignalType.NoAction, Reason = "No fully closed MTF confirmation candle." };
            }

            var structure = _confirmation.PriceAction().Structure[confirmationIndex];
            var aligned = longSide
                ? structure.Bias > 0 || structure.BosBull
                : structure.Bias < 0 || structure.BosBear;
            if (!aligned)
            {
                return detail with { Signal = SignalType.NoAction, Reason = "MTF causal structure/BOS confirmation disagrees." };
            }
        }

        var contextIndex = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(_context, signalClose);
        if (contextIndex < 0)
        {
            return detail with { Signal = SignalType.NoAction, Reason = "No fully closed MTF context candle." };
        }

        var fast = _context.Ema(_filters.EmaFast)[contextIndex];
        var slow = _context.Ema(_filters.EmaSlow)[contextIndex];
        if (fast is null || slow is null || (longSide ? fast <= slow : fast >= slow))
        {
            return detail with { Signal = SignalType.NoAction, Reason = "MTF causal EMA20/EMA50 context disagrees." };
        }

        return detail with { Reason = $"{detail.Reason} MTF confirmation/context passed using fully closed candles." };
    }
}
