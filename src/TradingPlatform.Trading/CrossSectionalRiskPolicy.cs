using TradingPlatform.Application.Trading;
using TradingPlatform.Research;

namespace TradingPlatform.Trading;

public static class CrossSectionalApproval
{
    public const string Researching = "RESEARCHING";
    public const string InsufficientEvidence = "INSUFFICIENT_EVIDENCE";
    public const string ValidatedForPaper = "VALIDATED_FOR_PAPER";
    public const string PaperApproved = "PAPER_APPROVED";
    public const string LiveApproved = "LIVE_APPROVED";
    public const string LiveDisabled = "LIVE_DISABLED";
}

public sealed record CrossSectionRejection(string Symbol, string Direction, string Reason);

public sealed record CrossSectionAllocation(
    string Symbol,
    string Direction,
    decimal PlannedRiskPercent,
    int ClusterSize);

public sealed record CrossSectionPlan(
    IReadOnlyList<CrossSectionAllocation> Accepted,
    IReadOnlyList<CrossSectionRejection> Rejected,
    decimal PlannedRiskPercent,
    decimal LongRiskPercent,
    decimal ShortRiskPercent,
    decimal Heat);

public readonly record struct CrossSectionCluster(string Symbol, string ClusterId);

/// <summary>
/// Strategy-specific limits. The existing Risk Engine still has to approve every order.
/// </summary>
public static class CrossSectionalRiskPolicy
{
    public const string SameSymbolOccupied = "SAME_SYMBOL_OCCUPIED";
    public const string MaxLongPositions = "MAX_LONG_POSITIONS";
    public const string MaxShortPositions = "MAX_SHORT_POSITIONS";
    public const string MaxTotalPositions = "MAX_TOTAL_POSITIONS";
    public const string MaxCrossSectionalRisk = "MAX_CROSS_SECTIONAL_RISK";
    public const string ClusterLimit = "CLUSTER_LIMIT";
    public const string DirectionalHeat = "DIRECTIONAL_HEAT";
    public const string BasketHeat = "CORRELATION_HEAT";
    public const string InsufficientUniverse = "INSUFFICIENT_DATA";
    public const string LiveBlocked = "OTHER_RISK_BLOCK";

    public static string? LiveActivationBlock(TradingOptions options, bool exchangeHealthy, bool marketDataHealthy, bool accountHealthy, bool riskHalted, bool dailyLossHalted, bool emergencyStop, bool isolatedConfirmed)
    {
        if (!options.LiveTradingEnabled)
        {
            return "LIVE = OFF. Global live trading is disabled.";
        }

        if (!exchangeHealthy)
        {
            return "Exchange connectivity is not healthy.";
        }

        if (!marketDataHealthy)
        {
            return "Market data is not healthy.";
        }

        if (!accountHealthy)
        {
            return "Account state is not healthy.";
        }

        if (riskHalted)
        {
            return "A global risk halt is active.";
        }

        if (dailyLossHalted)
        {
            return "Daily loss halt is active.";
        }

        if (emergencyStop)
        {
            return "Emergency stop is active.";
        }

        if (!isolatedConfirmed)
        {
            return "Isolated margin is not confirmed.";
        }

        return null;
    }

    public static CrossSectionPlan Select(
        IReadOnlyList<CrossSectionCandidate> candidates,
        CrossSectionalReversalOptions options,
        IReadOnlySet<string> occupiedSymbols,
        IReadOnlyList<CrossSectionCluster>? clusters = null,
        int universeSize = int.MaxValue)
    {
        var rejected = new List<CrossSectionRejection>();
        if (universeSize < options.MinimumEligibleSymbols)
        {
            foreach (var candidate in candidates)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, InsufficientUniverse));
            }

            return new CrossSectionPlan([], rejected, 0m, 0m, 0m, 0m);
        }

        var clusterOf = new Dictionary<string, string>(StringComparer.Ordinal);
        if (clusters is not null)
        {
            foreach (var row in clusters)
            {
                clusterOf[row.Symbol] = row.ClusterId;
            }
        }

        var longs = candidates.Where(row => row.Direction == "LONG").OrderBy(row => row.FeatureValue).ThenBy(row => row.Symbol, StringComparer.Ordinal).ToList();
        var shorts = candidates.Where(row => row.Direction == "SHORT").OrderByDescending(row => row.FeatureValue).ThenBy(row => row.Symbol, StringComparer.Ordinal).ToList();
        var accepted = new List<CrossSectionAllocation>();
        var clusterCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        decimal longRisk = 0m;
        decimal shortRisk = 0m;
        foreach (var candidate in longs.Concat(shorts))
        {
            if (occupiedSymbols.Contains(candidate.Symbol))
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, SameSymbolOccupied));
                continue;
            }

            var isLong = candidate.Direction == "LONG";
            if (accepted.Count >= options.MaxTotalPositions)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, MaxTotalPositions));
                continue;
            }

            if (isLong && accepted.Count(row => row.Direction == "LONG") >= options.MaxLongPositions)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, MaxLongPositions));
                continue;
            }

            if (!isLong && accepted.Count(row => row.Direction == "SHORT") >= options.MaxShortPositions)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, MaxShortPositions));
                continue;
            }

            var risk = options.EqualRiskSizing || !options.DynamicRiskWeighting
                ? options.MaxPerPositionRiskPercent
                : options.MaxPerPositionRiskPercent;
            var next = (isLong ? longRisk : shortRisk) + risk;
            if (longRisk + shortRisk + risk > options.MaxCrossSectionalRiskPercent)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, MaxCrossSectionalRisk));
                continue;
            }

            if (next > options.MaxDirectionalRiskPercent)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, DirectionalHeat));
                continue;
            }

            var cluster = clusterOf.TryGetValue(candidate.Symbol, out var id) ? id : "UNCLUSTERED";
            clusterCounts.TryGetValue(cluster, out var clusterCount);
            if (cluster != "UNCLUSTERED" && clusterCount >= options.MaxClusterPositions)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, ClusterLimit));
                continue;
            }

            var heat = Heat(accepted.Count + 1, longRisk + shortRisk + risk, options);
            if (heat > options.MaxHeat)
            {
                rejected.Add(new CrossSectionRejection(candidate.Symbol, candidate.Direction, BasketHeat));
                continue;
            }

            accepted.Add(new CrossSectionAllocation(candidate.Symbol, candidate.Direction, risk, clusterCount + 1));
            clusterCounts[cluster] = clusterCount + 1;
            if (isLong)
            {
                longRisk += risk;
            }
            else
            {
                shortRisk += risk;
            }
        }

        var total = longRisk + shortRisk;
        return new CrossSectionPlan(accepted, rejected, total, longRisk, shortRisk, Heat(accepted.Count, total, options));
    }

    public static decimal Heat(int positions, decimal plannedRiskPercent, CrossSectionalReversalOptions options)
    {
        var positionPart = options.MaxTotalPositions <= 0 ? 1m : (decimal)positions / options.MaxTotalPositions;
        var riskPart = options.MaxCrossSectionalRiskPercent <= 0m ? 1m : plannedRiskPercent / options.MaxCrossSectionalRiskPercent;
        return Math.Max(positionPart, riskPart);
    }
}
