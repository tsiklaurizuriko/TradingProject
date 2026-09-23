using TradingPlatform.Domain.Market;

namespace TradingPlatform.Strategies.PriceAction;

/// <summary>
/// One causal pass over a candle list. Safe to cache on CausalIndicatorCache.
/// Outcome labels are NOT stored here.
/// </summary>
public sealed class PriceActionBook
{
    public const int DefaultSwing = 3;

    public IReadOnlyList<MarketCandle> Candles { get; }
    public CandleGeom[] Geoms { get; }
    public IReadOnlyList<string>[] CandleEvents { get; }
    public SequenceBar[] Sequences { get; }
    public IReadOnlyList<SwingPoint> Highs { get; }
    public IReadOnlyList<SwingPoint> Lows { get; }
    public StructureBar[] Structure { get; }
    public IReadOnlyList<PatternOccurrence> Occurrences { get; }
    private IReadOnlyList<PatternOccurrence>[] ConfirmedByIndex { get; }

    private PriceActionBook(
        IReadOnlyList<MarketCandle> candles,
        CandleGeom[] geoms,
        IReadOnlyList<string>[] candleEvents,
        SequenceBar[] sequences,
        IReadOnlyList<SwingPoint> highs,
        IReadOnlyList<SwingPoint> lows,
        StructureBar[] structure,
        IReadOnlyList<PatternOccurrence> occurrences)
    {
        Candles = candles;
        Geoms = geoms;
        CandleEvents = candleEvents;
        Sequences = sequences;
        Highs = highs;
        Lows = lows;
        Structure = structure;
        Occurrences = occurrences;
        var confirmed = new List<PatternOccurrence>?[candles.Count];
        foreach (var occurrence in occurrences)
        {
            if (occurrence.Status != PatternKinds.Confirmed
                || occurrence.ConfirmationIndex is not { } index
                || index < 0
                || index >= confirmed.Length)
            {
                continue;
            }

            (confirmed[index] ??= []).Add(occurrence);
        }

        ConfirmedByIndex = confirmed
            .Select(x => (IReadOnlyList<PatternOccurrence>?)x ?? Array.Empty<PatternOccurrence>())
            .ToArray();
    }

    public static PriceActionBook Build(IReadOnlyList<MarketCandle> candles, int swingN = DefaultSwing)
    {
        var geoms = CandleGeom.Series(candles);
        var events = CandlePatternDetector.Detect(candles, geoms);
        var sequences = CandleSequenceEngine.Detect(geoms);
        var (highs, lows) = CausalSwingSeries.Detect(candles, swingN);
        var structure = MarketStructureEngine.Detect(candles, highs, lows);
        var sweeps = LiquiditySweepEngine.Detect(candles, highs, lows);
        var charts = ChartPatternEngine.Detect(candles, geoms, highs, lows);
        var occ = new List<PatternOccurrence>(sweeps.Count + charts.Count + 8);
        occ.AddRange(sweeps);
        occ.AddRange(charts);
        for (var i = 0; i < structure.Length; i++)
        {
            var s = structure[i];
            if (s.BosBull || s.BosBear)
            {
                occ.Add(new PatternOccurrence(
                    PatternKinds.StructureBos,
                    "v1",
                    i,
                    i,
                    i,
                    i,
                    null,
                    candles[i].Close,
                    s.BosBull ? "BULLISH" : "BEARISH",
                    PatternKinds.Confirmed,
                    [new PatternPoint(i, candles[i].Close, "bos")]));
            }

            if (s.ChochBull || s.ChochBear)
            {
                occ.Add(new PatternOccurrence(
                    PatternKinds.StructureChoch,
                    "v1",
                    i,
                    i,
                    i,
                    i,
                    null,
                    candles[i].Close,
                    s.ChochBull ? "BULLISH" : "BEARISH",
                    PatternKinds.Confirmed,
                    [new PatternPoint(i, candles[i].Close, "choch")]));
            }
        }

        return new PriceActionBook(candles, geoms, events, sequences, highs, lows, structure, occ);
    }

    public IEnumerable<PatternOccurrence> ConfirmedAt(int i, string? kind = null) =>
        i < 0 || i >= ConfirmedByIndex.Length
            ? []
            : kind is null
                ? ConfirmedByIndex[i]
                : ConfirmedByIndex[i].Where(o => string.Equals(o.PatternType, kind, StringComparison.Ordinal));

    public bool HasEvent(int i, string kind) =>
        i >= 0 && i < CandleEvents.Length && CandleEvents[i].Contains(kind);
}
