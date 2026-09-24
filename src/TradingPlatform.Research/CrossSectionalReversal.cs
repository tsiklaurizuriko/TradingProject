using TradingPlatform.Backtesting.Validation;

namespace TradingPlatform.Research;

/// <summary>
/// Research-only cross-sectional reversal. Ranking uses closed bars at T.
/// Forward returns are evaluation outputs and are not ranking inputs.
/// </summary>
public static class CrossSectionalReversalCatalog
{
    public const string Family = "CROSS_SECTIONAL_REVERSAL";
    public const string Return15mKey = "cross_sectional_reversal_return_15m";
    public const string Return1hKey = "cross_sectional_reversal_return_1h";
    public const string Feature15m = "return_15m";
    public const string Feature1h = "return_1h";
    public const string RankingVersion = "cross-section-v1";
    public const string ManifestSha256 = "fe81edf56d4af31cc0db0a215c09a6245437ad2a66eeb1f6caec11afb0ef247e";
    public const string Status = "RESEARCHING";
    public const string ValidatedForPaper = "NONE";
    public const string Notice = "Repeatable cross-sectional reversal factor — not validated for trading.";
    public const string Clock = "BTCUSDT_15M";
    public const int HistoryBarsRequired = 96;
    public const int MinimumUniverseSize = 30;

    public static int LookbackBars(string feature) =>
        string.Equals(feature, Feature1h, StringComparison.Ordinal) ? 4 : 1;

    public static string StrategyKey(string feature) =>
        string.Equals(feature, Feature1h, StringComparison.Ordinal) ? Return1hKey : Return15mKey;

    /// <summary>Model B: fee 0.04% plus slippage 0.02% per fill, two fills per replaced leg.</summary>
    public static decimal RoundTripPerLeg
    {
        get
        {
            var model = StrategyValidation.LowIsolatedRisk(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
            return (model.FeePercent + model.SlippagePercent) / 100m * 2m;
        }
    }
}

public sealed class CrossSectionSymbolBar
{
    public required string Symbol { get; init; }
    public decimal? Close { get; init; }
    public decimal? PriorClose { get; init; }
    public int OwnClosedBars { get; init; }
    public bool OnClock { get; init; } = true;
}

public sealed record CrossSectionCandidate(
    string StrategyKey,
    string Feature,
    decimal FeatureValue,
    decimal PercentileRank,
    string Decile,
    int UniverseSize,
    DateTimeOffset SignalTimestamp,
    DateTimeOffset RankingTimestamp,
    string RankingVersion,
    string Symbol,
    string Direction);

public sealed record CrossSectionHold(
    DateTimeOffset Timestamp,
    string Symbol,
    string Feature,
    decimal FeatureValue,
    decimal PercentileRank,
    string Decile,
    string Direction,
    int UniverseSize,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal GrossReturn,
    decimal Fees,
    decimal Slippage,
    decimal NetReturn);

public static class CrossSectionalReversalRanker
{
    public static IReadOnlyList<CrossSectionCandidate> Rank(
        DateTimeOffset timestamp,
        string feature,
        IReadOnlyList<CrossSectionSymbolBar> bars,
        int minimumUniverse = CrossSectionalReversalCatalog.MinimumUniverseSize,
        int historyBars = CrossSectionalReversalCatalog.HistoryBarsRequired,
        bool allowLong = true,
        bool allowShort = true)
    {
        var ranked = Eligible(bars, historyBars);
        if (ranked.Count < minimumUniverse)
        {
            return [];
        }

        ranked.Sort(static (a, b) =>
        {
            var value = a.Feature.CompareTo(b.Feature);
            return value != 0 ? value : string.CompareOrdinal(a.Symbol, b.Symbol);
        });

        var tail = Math.Max(1, ranked.Count / 10);
        var key = CrossSectionalReversalCatalog.StrategyKey(feature);
        var rows = new List<CrossSectionCandidate>();
        for (var i = 0; i < ranked.Count; i++)
        {
            string? decile = i < tail ? "bottom" : i >= ranked.Count - tail ? "top" : null;
            if (decile is null)
            {
                continue;
            }

            var direction = decile == "bottom" ? "LONG" : "SHORT";
            if ((direction == "LONG" && !allowLong) || (direction == "SHORT" && !allowShort))
            {
                continue;
            }

            var row = ranked[i];
            var percentile = ranked.Count == 1 ? 0.5m : (decimal)i / (ranked.Count - 1);
            rows.Add(new CrossSectionCandidate(
                key,
                feature,
                row.Feature,
                percentile,
                decile,
                ranked.Count,
                timestamp,
                timestamp,
                CrossSectionalReversalCatalog.RankingVersion,
                row.Symbol,
                direction));
        }

        return rows;
    }

    public static decimal? FeatureValue(decimal? close, decimal? prior)
    {
        if (close is not > 0m || prior is not > 0m)
        {
            return null;
        }

        return close.Value / prior.Value - 1m;
    }

    private static List<EligibleRow> Eligible(IReadOnlyList<CrossSectionSymbolBar> bars, int historyBars)
    {
        var rows = new List<EligibleRow>();
        foreach (var bar in bars)
        {
            if (!bar.OnClock || !CrossSectionMath.AcceptedName(bar.Symbol) || bar.OwnClosedBars < historyBars)
            {
                continue;
            }

            var feature = FeatureValue(bar.Close, bar.PriorClose);
            if (feature is null)
            {
                continue;
            }

            rows.Add(new EligibleRow(bar.Symbol, feature.Value));
        }

        return rows;
    }

    private readonly record struct EligibleRow(string Symbol, decimal Feature);
}

public static class CrossSectionalReversalReplay
{
    public static IReadOnlyList<CrossSectionCandidate> RankClock(
        DateTimeOffset[] clock,
        string[] symbols,
        decimal?[,] close,
        int timeIndex,
        string feature,
        int historyBars = CrossSectionalReversalCatalog.HistoryBarsRequired)
    {
        if (timeIndex < 0 || timeIndex >= clock.Length)
        {
            return [];
        }

        var lookback = CrossSectionalReversalCatalog.LookbackBars(feature);
        var bars = new List<CrossSectionSymbolBar>(symbols.Length);
        for (var s = 0; s < symbols.Length; s++)
        {
            var own = 0;
            for (var t = 0; t <= timeIndex; t++)
            {
                if (close[t, s] is > 0m)
                {
                    own++;
                }
            }

            decimal? prior = timeIndex >= lookback ? close[timeIndex - lookback, s] : null;
            bars.Add(new CrossSectionSymbolBar
            {
                Symbol = symbols[s],
                Close = close[timeIndex, s],
                PriorClose = prior,
                OwnClosedBars = own,
                OnClock = true
            });
        }

        return CrossSectionalReversalRanker.Rank(clock[timeIndex], feature, bars, historyBars: historyBars);
    }

    /// <summary>
    /// Research measurement book from the source audit: long the high-return decile, short the low-return decile.
    /// This is not the strategy signal. The strategy shorts the top decile and longs the bottom decile.
    /// </summary>
    public static (decimal Gross, decimal Turnover, decimal Net, int Holds) MeasureResearchBook(
        string[] symbols,
        decimal?[,] close,
        string feature,
        int horizonBars,
        int historyBars = 0)
    {
        var lookback = CrossSectionalReversalCatalog.LookbackBars(feature);
        var times = close.GetLength(0);
        HashSet<string>? priorLong = null;
        HashSet<string>? priorShort = null;
        decimal grossSum = 0m;
        decimal turnSum = 0m;
        var holds = 0;
        var step = Math.Max(1, horizonBars);
        for (var t = Math.Max(lookback, historyBars); t + step < times; t += step)
        {
            var points = new List<(string Symbol, decimal Feature, decimal Forward)>();
            for (var s = 0; s < symbols.Length; s++)
            {
                var now = close[t, s];
                var past = close[t - lookback, s];
                var next = close[t + step, s];
                var featureValue = CrossSectionalReversalRanker.FeatureValue(now, past);
                var forward = CrossSectionalReversalRanker.FeatureValue(next, now);
                if (featureValue is null || forward is null || !CrossSectionMath.AcceptedName(symbols[s]))
                {
                    continue;
                }

                if (historyBars > 0)
                {
                    var own = 0;
                    for (var k = 0; k <= t; k++)
                    {
                        if (close[k, s] is > 0m)
                        {
                            own++;
                        }
                    }

                    if (own < historyBars)
                    {
                        continue;
                    }
                }

                points.Add((symbols[s], featureValue.Value, forward.Value));
            }

            if (points.Count < CrossSectionalReversalCatalog.MinimumUniverseSize)
            {
                continue;
            }

            points.Sort(static (a, b) =>
            {
                var value = a.Feature.CompareTo(b.Feature);
                return value != 0 ? value : string.CompareOrdinal(a.Symbol, b.Symbol);
            });
            var tail = Math.Max(1, points.Count / 10);
            var bottom = points.Take(tail).ToList();
            var top = points.Skip(points.Count - tail).ToList();
            var gross = top.Average(row => row.Forward) - bottom.Average(row => row.Forward);
            var longs = top.Select(row => row.Symbol).ToHashSet(StringComparer.Ordinal);
            var shorts = bottom.Select(row => row.Symbol).ToHashSet(StringComparer.Ordinal);
            var turnLong = Turnover(priorLong, longs);
            var turnShort = Turnover(priorShort, shorts);
            grossSum += gross;
            turnSum += turnLong + turnShort;
            holds++;
            priorLong = longs;
            priorShort = shorts;
        }

        if (holds == 0)
        {
            return (0m, 0m, 0m, 0);
        }

        var meanGross = grossSum / holds;
        var meanTurn = turnSum / holds;
        var net = meanGross - meanTurn * CrossSectionalReversalCatalog.RoundTripPerLeg;
        return (meanGross, meanTurn / 2m, net, holds);
    }

    public static IReadOnlyList<CrossSectionHold> ReversalHolds(
        DateTimeOffset[] clock,
        string[] symbols,
        decimal?[,] close,
        string feature,
        int horizonBars)
    {
        var step = Math.Max(1, horizonBars);
        var holds = new List<CrossSectionHold>();
        for (var t = 0; t + step < clock.Length; t += step)
        {
            var candidates = RankClock(clock, symbols, close, t, feature);
            foreach (var candidate in candidates)
            {
                var symbolIndex = Array.IndexOf(symbols, candidate.Symbol);
                var entry = close[t, symbolIndex];
                var exit = close[t + step, symbolIndex];
                if (entry is not > 0m || exit is not > 0m)
                {
                    continue;
                }

                var raw = exit.Value / entry.Value - 1m;
                var gross = candidate.Direction == "SHORT" ? -raw : raw;
                var fee = CrossSectionalReversalCatalog.RoundTripPerLeg * (0.04m / 0.06m);
                var slip = CrossSectionalReversalCatalog.RoundTripPerLeg - fee;
                holds.Add(new CrossSectionHold(
                    clock[t],
                    candidate.Symbol,
                    candidate.Feature,
                    candidate.FeatureValue,
                    candidate.PercentileRank,
                    candidate.Decile,
                    candidate.Direction,
                    candidate.UniverseSize,
                    entry.Value,
                    exit.Value,
                    gross,
                    fee,
                    slip,
                    gross - CrossSectionalReversalCatalog.RoundTripPerLeg));
            }
        }

        return holds;
    }

    private static decimal Turnover(HashSet<string>? prior, HashSet<string> current)
    {
        if (prior is null || current.Count == 0)
        {
            return 1m;
        }

        var entered = current.Count(symbol => !prior.Contains(symbol));
        var exited = prior.Count(symbol => !current.Contains(symbol));
        return (entered + exited) / 2m / current.Count;
    }
}
