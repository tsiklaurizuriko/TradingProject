using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.Research;

/// <summary>
/// Research-only contextual Price Action layer. Production evaluators, Risk, and Execution are not used.
/// Entry is a completed 5m candle. Higher-timeframe bars must already be closed at that close time.
/// </summary>
public static class ContextualPriceActionCatalog
{
    public const string Version = "contextual-pa-v1";
    public const string EntryTimeframe = "5m";
    public const string ConfirmationTimeframe = "3m";
    public const string StrictConfirmationTimeframe = "1m";
    public const string StructureTimeframe = "15m";
    public const string ContextTimeframe = "1h";
    public const decimal DisplacementBodyRatio = 0.55m;
    public const int AtrPeriod = 14;
    public const int RelativeVolumeLookback = 20;
    public const decimal StrictRelativeVolumeMin = 1m;
    public const int CompressionRunMin = 3;

    public const string SurvivorRule =
        "Frozen before OOS: aggregate IS trades >= 30, IS PF >= 0.90 or NoLosses, VALIDATION trades >= 20, VALIDATION PF > 1 and net > 0, and VALIDATION trades on at least 5 symbols. OOS is not an input.";

    public static readonly string[] Timeframes = ["1m", "3m", "5m", "15m", "1h"];

    public static readonly string[] CostLabels =
    [
        ResearchCostLabels.Base,
        ResearchCostLabels.Mild,
        ResearchCostLabels.High,
        ResearchCostLabels.Stress
    ];

    public static IReadOnlyList<ContextualHypothesis> Hypotheses { get; } = Build();

    public static ContextualHypothesis ById(string candidateId) =>
        Hypotheses.First(x => string.Equals(x.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<ContextualHypothesis> Build()
    {
        ContextualHypothesis Row(
            string family,
            string variant,
            string? confirmation,
            bool structure,
            bool context,
            bool strictVolume,
            string eventRule,
            string confirmationRule,
            string contextRule) =>
            new(
                $"CPA-{family}|{variant}|{EntryTimeframe}",
                family,
                variant,
                EntryTimeframe,
                confirmation,
                structure ? StructureTimeframe : null,
                context ? ContextTimeframe : null,
                strictVolume,
                eventRule,
                confirmationRule,
                contextRule);

        const string sweepEvent = "5m liquidity sweep of a swing already confirmed at or before the signal bar. Low sweep is long; high sweep is short.";
        const string failEvent = "5m failed breakout confirmed on the close back inside a level known before the breakout bar.";
        const string retestEvent = "5m breakout-retest confirmed on the retest close. The broken level and the breakout bar are already closed.";
        const string pullEvent = "5m liquidity sweep that reclaims a swing while the same-timeframe structure bias already agrees.";
        const string wmEvent = "W/M known at the second swing confirmation. Baseline does not wait for the neckline.";
        const string wmBreak = "W/M neckline break on a later completed 5m close. Detection time is not rewritten.";
        const string flagEvent = "Existing causal flag or pennant breakout: impulse, consolidation, and the breakout close.";
        const string compressEvent = "At least three prior compressed bars, then an expansion bar. Continuation requires same-side BOS. Reversal requires same-side CHoCH.";
        const string bosEvent = "5m BOS on the completed candle. Bullish and bearish BOS on the same bar is not a signal.";
        const string none = "No auxiliary timeframe.";
        const string bos3 = "Last 3m candle with CloseTime <= 5m CloseTime must print same-side BOS.";
        const string bos1 = "Last 1m candle with CloseTime <= 5m CloseTime must print same-side BOS.";
        const string bosOrChoch3 = "Last closed 3m candle must print same-side BOS or CHoCH.";
        const string h1Trend = "Last closed 1h structure must already be HH+HL with bullish bias, or LH+LL with bearish bias.";
        const string m15Bias = "Last closed 15m structure bias must agree.";
        const string m15Trend = "Last closed 15m structure must already be HH+HL or LH+LL in the same direction.";
        const string volume = "Strict only: causal relative volume, lookback 20, must be at least 1. Missing volume stays missing.";

        return
        [
            Row("SWEEP", "BASELINE", null, false, false, false, sweepEvent, none, "Same-timeframe sweep only."),
            Row("SWEEP", "CONTEXTUAL", ConfirmationTimeframe, false, true, false, sweepEvent, bos3, h1Trend),
            Row("SWEEP", "STRICT", StrictConfirmationTimeframe, true, true, true, sweepEvent, bos1, h1Trend + " " + m15Bias + " " + volume),

            Row("FAILED_BREAKOUT", "BASELINE", null, false, false, false, failEvent, none, "Same-timeframe failed breakout only."),
            Row("FAILED_BREAKOUT", "CONTEXTUAL", ConfirmationTimeframe, true, false, false, failEvent, bosOrChoch3, "Last closed 15m bar must still be inside its causal range."),
            Row("FAILED_BREAKOUT", "STRICT", StrictConfirmationTimeframe, true, true, true, failEvent, "Last closed 1m candle must print same-side BOS or CHoCH.", h1Trend + " " + m15Bias + " " + volume),

            Row("BREAKOUT_RETEST", "BASELINE", null, false, false, false, retestEvent, none, "Same-timeframe breakout-retest only."),
            Row("BREAKOUT_RETEST", "CONTEXTUAL", ConfirmationTimeframe, false, true, false, retestEvent + " Breakout bar body/range >= 0.55 and range >= ATR(14).", bos3, h1Trend),
            Row("BREAKOUT_RETEST", "STRICT", StrictConfirmationTimeframe, true, true, true, retestEvent + " Breakout bar body/range >= 0.55 and range >= ATR(14).", bos1, h1Trend + " " + m15Trend + " " + volume),

            Row("PULLBACK", "BASELINE", null, false, false, false, pullEvent, none, "Same-timeframe trend bias only."),
            Row("PULLBACK", "CONTEXTUAL", ConfirmationTimeframe, false, true, false, pullEvent, bos3, h1Trend),
            Row("PULLBACK", "STRICT", StrictConfirmationTimeframe, true, true, true, pullEvent, bos1, h1Trend + " " + m15Trend + " " + volume),

            Row("WM", "BASELINE", null, false, false, false, wmEvent, none, "Pattern detection only. Neckline break is not required."),
            Row("WM", "CONTEXTUAL", ConfirmationTimeframe, false, true, false, wmBreak, bos3, h1Trend),
            Row("WM", "STRICT", StrictConfirmationTimeframe, true, true, true, wmBreak, bos1, h1Trend + " " + m15Bias + " " + volume),

            Row("FLAG", "BASELINE", null, false, false, false, flagEvent, none, "Engine breakout only. No extra displacement gate."),
            Row("FLAG", "CONTEXTUAL", ConfirmationTimeframe, false, true, false, flagEvent + " Breakout bar body/range >= 0.55 and range >= ATR(14).", bos3, h1Trend),
            Row("FLAG", "STRICT", StrictConfirmationTimeframe, true, true, true, flagEvent + " Breakout bar body/range >= 0.55 and range >= ATR(14).", bos1, h1Trend + " " + m15Trend + " " + volume),

            Row("COMPRESSION", "CONTINUATION", null, false, false, false, compressEvent, "Same-bar 5m BOS agrees with the expansion candle.", "No higher timeframe."),
            Row("COMPRESSION", "REVERSAL", null, false, false, false, compressEvent, "Same-bar 5m CHoCH agrees with the expansion candle.", "No higher timeframe."),
            Row("COMPRESSION", "CONTINUATION_CONTEXT", null, false, true, false, compressEvent, "Same-bar 5m BOS agrees with the expansion candle.", h1Trend),
            Row("COMPRESSION", "REVERSAL_CONTEXT", null, false, true, false, compressEvent, "Same-bar 5m CHoCH agrees with the expansion candle.", h1Trend),

            Row("MTF", "BASELINE", null, false, false, false, bosEvent, none, "5m BOS only."),
            Row("MTF", "CONTEXTUAL", ConfirmationTimeframe, true, true, false, bosEvent, bos3, h1Trend + " " + m15Trend),
            Row("MTF", "STRICT", StrictConfirmationTimeframe, true, true, true, bosEvent, bos1, h1Trend + " " + m15Trend + " " + volume)
        ];
    }
}

public sealed record ContextualHypothesis(
    string CandidateId,
    string Family,
    string Variant,
    string EntryTimeframe,
    string? ConfirmationTimeframe,
    string? StructureTimeframe,
    string? ContextTimeframe,
    bool RequireRelativeVolume,
    string EventRule,
    string ConfirmationRule,
    string ContextRule);

public sealed record ContextualSignalRow(string CandidateId, SignalType[] Signals, bool SeriesMissing);

public static class ContextualPriceActionSignals
{
    public static IReadOnlyList<ContextualSignalRow> BuildAll(IReadOnlyDictionary<string, CausalIndicatorCache> caches)
    {
        var entry = caches.GetValueOrDefault(ContextualPriceActionCatalog.EntryTimeframe);
        EntryState? state = entry is null || entry.Candles.Count == 0 ? null : new EntryState(entry);
        var rows = new List<ContextualSignalRow>(ContextualPriceActionCatalog.Hypotheses.Count);
        foreach (var hypothesis in ContextualPriceActionCatalog.Hypotheses)
        {
            if (state is null || Missing(caches, hypothesis))
            {
                rows.Add(new ContextualSignalRow(hypothesis.CandidateId, [], true));
                continue;
            }

            var signals = new SignalType[state.Count];
            for (var i = 0; i < signals.Length; i++)
            {
                signals[i] = SignalAt(hypothesis, state, caches, i);
            }

            rows.Add(new ContextualSignalRow(hypothesis.CandidateId, signals, false));
        }

        return rows;
    }

    public static bool TrendAligned(StructureBar bar, bool longSide) =>
        longSide ? bar.Bias > 0 && bar.Hh && bar.Hl : bar.Bias < 0 && bar.Lh && bar.Ll;

    public static bool Displacement(CandleGeom geom, decimal? atr, bool longSide) =>
        atr is > 0m
        && geom.BodyRange >= ContextualPriceActionCatalog.DisplacementBodyRatio
        && geom.Range >= atr.Value
        && (longSide ? geom.Bullish : geom.Bearish);

    private static bool Missing(IReadOnlyDictionary<string, CausalIndicatorCache> caches, ContextualHypothesis hypothesis)
    {
        foreach (var tf in new[] { hypothesis.ConfirmationTimeframe, hypothesis.StructureTimeframe, hypothesis.ContextTimeframe })
        {
            if (string.IsNullOrWhiteSpace(tf))
            {
                continue;
            }

            if (!caches.TryGetValue(tf, out var cache) || cache.Candles.Count == 0)
            {
                return true;
            }
        }

        return false;
    }

    private static SignalType SignalAt(
        ContextualHypothesis hypothesis,
        EntryState entry,
        IReadOnlyDictionary<string, CausalIndicatorCache> caches,
        int index)
    {
        var side = EventSide(hypothesis, entry, index);
        if (side == 0)
        {
            return SignalType.NoAction;
        }

        var close = entry.Book.Candles[index].CloseTime;
        if (hypothesis.RequireRelativeVolume
            && (entry.RelativeVolume[index] is not { } volume
                || volume < ContextualPriceActionCatalog.StrictRelativeVolumeMin))
        {
            return SignalType.NoAction;
        }

        if (!PassesConfirmation(hypothesis, caches, close, side))
        {
            return SignalType.NoAction;
        }

        if (!PassesContext(hypothesis, caches, close, side))
        {
            return SignalType.NoAction;
        }

        return side > 0 ? SignalType.Buy : SignalType.Sell;
    }

    private static int EventSide(ContextualHypothesis hypothesis, EntryState entry, int index)
    {
        var structure = entry.Book.Structure[index];
        return hypothesis.Family switch
        {
            "SWEEP" => entry.Side(PatternKinds.LiquiditySweepLow, index, confirmed: true) > 0
                ? 1
                : entry.Side(PatternKinds.LiquiditySweepHigh, index, confirmed: true) < 0 ? -1 : 0,
            "FAILED_BREAKOUT" => entry.Side(PatternKinds.FailedBreakout, index, confirmed: true),
            "BREAKOUT_RETEST" => RetestSide(hypothesis, entry, index),
            "PULLBACK" => PullbackSide(entry, structure, index),
            "WM" => WmSide(hypothesis, entry, index),
            "FLAG" => FlagSide(hypothesis, entry, index),
            "COMPRESSION" => CompressionSide(hypothesis, entry, structure, index),
            "MTF" => structure.BosBull == structure.BosBear ? 0 : structure.BosBull ? 1 : structure.BosBear ? -1 : 0,
            _ => 0
        };
    }

    private static int RetestSide(ContextualHypothesis hypothesis, EntryState entry, int index)
    {
        var side = entry.Side(PatternKinds.BreakoutRetest, index, confirmed: true);
        if (side == 0 || hypothesis.Variant == "BASELINE")
        {
            return side;
        }

        var start = entry.StartIndex(PatternKinds.BreakoutRetest, index);
        return start >= 0 && Displacement(entry.Book.Geoms[start], entry.Atr[start], side > 0) ? side : 0;
    }

    private static int PullbackSide(EntryState entry, StructureBar structure, int index)
    {
        if (structure.Bias > 0 && entry.Side(PatternKinds.LiquiditySweepLow, index, confirmed: true) > 0)
        {
            return 1;
        }

        if (structure.Bias < 0 && entry.Side(PatternKinds.LiquiditySweepHigh, index, confirmed: true) < 0)
        {
            return -1;
        }

        return 0;
    }

    private static int WmSide(ContextualHypothesis hypothesis, EntryState entry, int index)
    {
        var confirmed = hypothesis.Variant != "BASELINE";
        var w = entry.Side(PatternKinds.WDoubleBottom, index, confirmed);
        if (w > 0)
        {
            return 1;
        }

        var m = entry.Side(PatternKinds.MDoubleTop, index, confirmed);
        return m < 0 ? -1 : 0;
    }

    private static int FlagSide(ContextualHypothesis hypothesis, EntryState entry, int index)
    {
        var side = entry.Side(PatternKinds.BullFlag, index, confirmed: true);
        if (side == 0)
        {
            side = entry.Side(PatternKinds.BearFlag, index, confirmed: true);
        }

        if (side == 0)
        {
            side = entry.Side(PatternKinds.Pennant, index, confirmed: true);
        }

        if (side == 0 || hypothesis.Variant == "BASELINE")
        {
            return side;
        }

        return Displacement(entry.Book.Geoms[index], entry.Atr[index], side > 0) ? side : 0;
    }

    private static int CompressionSide(ContextualHypothesis hypothesis, EntryState entry, StructureBar structure, int index)
    {
        if (index < 1
            || entry.Book.Sequences[index - 1].CompressionRun < ContextualPriceActionCatalog.CompressionRunMin
            || entry.Book.Sequences[index].ExpansionRun < 1)
        {
            return 0;
        }

        var geom = entry.Book.Geoms[index];
        var side = geom.Bullish ? 1 : geom.Bearish ? -1 : 0;
        if (side == 0)
        {
            return 0;
        }

        var reversal = hypothesis.Variant is "REVERSAL" or "REVERSAL_CONTEXT";
        var agrees = reversal
            ? side > 0 ? structure.ChochBull : structure.ChochBear
            : side > 0 ? structure.BosBull : structure.BosBear;
        return agrees ? side : 0;
    }

    private static bool PassesConfirmation(
        ContextualHypothesis hypothesis,
        IReadOnlyDictionary<string, CausalIndicatorCache> caches,
        DateTimeOffset close,
        int side)
    {
        if (hypothesis.Family == "COMPRESSION" || string.IsNullOrWhiteSpace(hypothesis.ConfirmationTimeframe))
        {
            return true;
        }

        var bar = ClosedStructure(caches, hypothesis.ConfirmationTimeframe, close);
        if (bar is null)
        {
            return false;
        }

        var bos = side > 0 ? bar.Value.BosBull : bar.Value.BosBear;
        if (hypothesis.Family == "FAILED_BREAKOUT")
        {
            var choch = side > 0 ? bar.Value.ChochBull : bar.Value.ChochBear;
            return bos || choch;
        }

        return bos;
    }

    private static bool PassesContext(
        ContextualHypothesis hypothesis,
        IReadOnlyDictionary<string, CausalIndicatorCache> caches,
        DateTimeOffset close,
        int side)
    {
        if (hypothesis.Family == "FAILED_BREAKOUT" && hypothesis.Variant != "BASELINE")
        {
            var range = ClosedStructure(caches, ContextualPriceActionCatalog.StructureTimeframe, close);
            if (range is not { Range: true })
            {
                return false;
            }

            if (hypothesis.Variant == "STRICT")
            {
                var longSide = side > 0;
                if (longSide ? range.Value.Bias <= 0 : range.Value.Bias >= 0)
                {
                    return false;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(hypothesis.ContextTimeframe))
        {
            var context = ClosedStructure(caches, hypothesis.ContextTimeframe, close);
            if (context is null || !TrendAligned(context.Value, side > 0))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(hypothesis.StructureTimeframe) && hypothesis.Family != "FAILED_BREAKOUT")
        {
            var structure = ClosedStructure(caches, hypothesis.StructureTimeframe, close);
            if (structure is null)
            {
                return false;
            }

            var longSide = side > 0;
            var aligned = hypothesis.Family is "PULLBACK" or "BREAKOUT_RETEST" or "FLAG" or "MTF"
                ? TrendAligned(structure.Value, longSide)
                : longSide ? structure.Value.Bias > 0 : structure.Value.Bias < 0;
            if (!aligned)
            {
                return false;
            }
        }

        return true;
    }

    private static StructureBar? ClosedStructure(
        IReadOnlyDictionary<string, CausalIndicatorCache> caches,
        string timeframe,
        DateTimeOffset close)
    {
        if (!caches.TryGetValue(timeframe, out var cache) || cache.Candles.Count == 0)
        {
            return null;
        }

        var index = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(cache, close);
        return index < 0 ? null : cache.PriceAction().Structure[index];
    }

    private sealed class EntryState
    {
        private readonly Dictionary<string, int[]> _confirmed = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> _detected = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int[]> _start = new(StringComparer.Ordinal);

        public EntryState(CausalIndicatorCache cache)
        {
            Book = cache.PriceAction();
            Count = Book.Candles.Count;
            Atr = cache.Atr(ContextualPriceActionCatalog.AtrPeriod);
            RelativeVolume = cache.RelativeVolume(ContextualPriceActionCatalog.RelativeVolumeLookback);
            foreach (var occurrence in Book.Occurrences)
            {
                var side = occurrence.Direction.Equals("BULLISH", StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : occurrence.Direction.Equals("BEARISH", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
                if (side == 0)
                {
                    continue;
                }

                if (occurrence.Status == PatternKinds.Confirmed && occurrence.ConfirmationIndex is { } confirmed)
                {
                    Paint(_confirmed, occurrence.PatternType, confirmed, side);
                    Paint(_start, occurrence.PatternType, confirmed, occurrence.StartIndex);
                }

                if (occurrence.DetectionIndex >= 0)
                {
                    Paint(_detected, occurrence.PatternType, occurrence.DetectionIndex, side);
                }
            }
        }

        public PriceActionBook Book { get; }
        public int Count { get; }
        public IReadOnlyList<decimal?> Atr { get; }
        public IReadOnlyList<decimal?> RelativeVolume { get; }

        public int Side(string kind, int index, bool confirmed)
        {
            var map = confirmed ? _confirmed : _detected;
            if (!map.TryGetValue(kind, out var sides) || index < 0 || index >= sides.Length)
            {
                return 0;
            }

            var side = sides[index];
            return side is 1 or -1 ? side : 0;
        }

        public int StartIndex(string kind, int index)
        {
            if (!_start.TryGetValue(kind, out var starts) || index < 0 || index >= starts.Length)
            {
                return -1;
            }

            return starts[index];
        }

        private void Paint(Dictionary<string, int[]> map, string kind, int index, int value)
        {
            if (index < 0 || index >= Count)
            {
                return;
            }

            if (!map.TryGetValue(kind, out var values))
            {
                values = new int[Count];
                map[kind] = values;
            }

            if (values[index] == 0)
            {
                values[index] = value;
            }
            else if (values[index] != value)
            {
                values[index] = int.MinValue;
            }
        }
    }
}
