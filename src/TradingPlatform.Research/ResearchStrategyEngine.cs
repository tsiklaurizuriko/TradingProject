using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed class ResearchStrategyEngine : IStrategyEngine
{
    private readonly StrategyEngine _inner = new();
    private readonly ResearchCandidate _candidate;
    private readonly StrategyDefinition? _parentDefinition;

    public ResearchStrategyEngine(ResearchCandidate candidate)
    {
        _candidate = candidate;
        if (!string.IsNullOrWhiteSpace(candidate.ParentTemplateKey))
        {
            _parentDefinition = StrategyValidationDefinition(candidate.ParentTemplateKey);
        }
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
        StrategySignalDetail detail;
        if (string.Equals(_candidate.Kind, ResearchKinds.Native, StringComparison.OrdinalIgnoreCase))
        {
            detail = ResearchNativeEvaluator.EvaluateDetailAt(_candidate, context, cache, index);
        }
        else if (_parentDefinition is not null)
        {
            detail = _inner.EvaluateDetailAt(_parentDefinition, context, cache, index);
        }
        else
        {
            return new StrategySignalDetail(SignalType.NoAction, "Research candidate has no parent template and is not native.");
        }

        if (detail.Signal is SignalType.Buy or SignalType.Sell && !PassesFilters(detail.Signal, cache, index, context, out var filterReason))
        {
            return new StrategySignalDetail(SignalType.NoAction, filterReason);
        }

        return detail;
    }

    StrategySignalDetail IStrategyEngine.EvaluateDetailAt(
        StrategyDefinition definition,
        StrategyContext context,
        CausalIndicatorCache cache,
        int index) =>
        EvaluateDetailAt(definition, context, cache, index);

    public static int LastClosedHigherTimeframeIndex(CausalIndicatorCache htf, DateTimeOffset signalCloseTime)
    {
        var candles = htf.Candles;
        var lo = 0;
        var hi = candles.Count - 1;
        var idx = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (candles[mid].CloseTime <= signalCloseTime)
            {
                idx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return idx;
    }

    private bool PassesFilters(
        SignalType signal,
        CausalIndicatorCache cache,
        int index,
        StrategyContext context,
        out string reason)
    {
        var f = _candidate.Filters;
        var longSide = signal == SignalType.Buy;
        if (f.RequireEmaAlignment || f.RequirePriceVsSlowEma || f.RequireEmaSlope)
        {
            var fast = cache.Ema(f.EmaFast);
            var slow = cache.Ema(f.EmaSlow);
            if (fast[index] is not { } emaFast || slow[index] is not { } emaSlow)
            {
                reason = "EMA filter not ready.";
                return false;
            }

            if (f.RequireEmaAlignment && (longSide ? emaFast <= emaSlow : emaFast >= emaSlow))
            {
                reason = "EMA alignment filter skipped this bar.";
                return false;
            }

            var close = cache.Candles[index].Close;
            if (f.RequirePriceVsSlowEma && (longSide ? close <= emaSlow : close >= emaSlow))
            {
                reason = "Price versus slow EMA filter skipped this bar.";
                return false;
            }

            if (f.RequireEmaSlope)
            {
                var slope = cache.EmaSlope(f.EmaSlow);
                if (slope[index] is not { } sl || (longSide ? sl <= 0m : sl >= 0m))
                {
                    reason = "EMA slope filter skipped this bar.";
                    return false;
                }
            }
        }

        if (f.MinAdx is { } minAdx)
        {
            var adx = cache.Adx(f.AdxPeriod);
            if (adx[index] is not { } value || value < minAdx)
            {
                reason = "ADX filter skipped this bar.";
                return false;
            }
        }

        if (f.RequireAtrExpansion)
        {
            var atr = cache.Atr(f.AtrPeriod);
            if (index < 1 || atr[index] is not { } now || atr[index - 1] is not { } prev || now <= prev)
            {
                reason = "ATR expansion filter skipped this bar.";
                return false;
            }
        }

        if (f.MinAtrPercentile is not null || f.MaxAtrPercentile is not null)
        {
            var pctl = cache.AtrPercentile(f.AtrPeriod, f.AtrPercentileLookback);
            if (pctl[index] is not { } pct)
            {
                reason = "ATR percentile not ready.";
                return false;
            }

            if (f.MinAtrPercentile is { } minP && pct < minP)
            {
                reason = "ATR percentile below research threshold.";
                return false;
            }

            if (f.MaxAtrPercentile is { } maxP && pct > maxP)
            {
                reason = "ATR percentile above research threshold.";
                return false;
            }
        }

        if (f.MinRelativeVolume is { } minVol)
        {
            var rel = cache.RelativeVolume(f.RelativeVolumeLookback);
            if (rel[index] is not { } rv || rv < minVol)
            {
                reason = "Relative volume filter skipped this bar.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(f.HigherTimeframe))
        {
            var htf = context.HigherTimeframeCache;
            var required = !string.Equals(f.HigherTimeframe, "next", StringComparison.OrdinalIgnoreCase);
            if (htf is null)
            {
                if (required)
                {
                    reason = "Higher-timeframe cache missing; HTF filter skipped the bar (no look-ahead).";
                    return false;
                }
            }
            else
            {
                var htfIndex = LastClosedHigherTimeframeIndex(htf, cache.Candles[index].CloseTime);
                if (htfIndex < 0)
                {
                    reason = "No closed higher-timeframe candle yet.";
                    return false;
                }

                var hFast = htf.Ema(f.EmaFast);
                var hSlow = htf.Ema(f.EmaSlow);
                if (hFast[htfIndex] is not { } hf || hSlow[htfIndex] is not { } hs)
                {
                    reason = "Higher-timeframe EMA not ready.";
                    return false;
                }

                if (longSide ? hf <= hs : hf >= hs)
                {
                    reason = "Higher-timeframe EMA does not agree.";
                    return false;
                }
            }
        }

        reason = "";
        return true;
    }

    private static StrategyDefinition StrategyValidationDefinition(string templateKey)
    {
        var json = StrategyTemplates.Build(
            templateKey,
            1,
            StrategyTemplates.DefaultsFor(templateKey, true) with
            {
                TemplateKey = templateKey,
                AllowedSide = StrategySides.Both,
                Timeframe = "1h"
            });
        return new StrategyDefinitionValidator().Parse(json);
    }
}
