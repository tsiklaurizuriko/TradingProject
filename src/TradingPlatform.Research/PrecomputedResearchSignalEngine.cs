using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

/// <summary>
/// Replays causally precomputed research signals. Production strategy evaluation is unaffected.
/// Price Action positions still hold until the authoritative replay book SL/TP closes them.
/// </summary>
public sealed class PrecomputedResearchSignalEngine : IStrategyEngine
{
    private readonly IReadOnlyList<SignalType> _signals;

    public PrecomputedResearchSignalEngine(IReadOnlyList<SignalType> signals)
    {
        _signals = signals;
    }

    public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason)
    {
        var index = context.ClosedCandles.Count - 1;
        return EvaluateAt(definition, context, new CausalIndicatorCache(context.ClosedCandles), index, out reason);
    }

    public SignalType EvaluateAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index,
        out string reason)
    {
        if (context.HasOpenPosition)
        {
            reason = "Position open; Isolated research book owns SL/TP.";
            return SignalType.Hold;
        }

        if (index < 0 || index >= _signals.Count)
        {
            reason = "Precomputed signal index unavailable.";
            return SignalType.NoAction;
        }

        reason = "Causal precomputed Price Action research signal.";
        return _signals[index];
    }
}
