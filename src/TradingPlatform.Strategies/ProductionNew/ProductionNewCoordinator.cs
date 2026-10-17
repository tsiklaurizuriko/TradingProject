namespace TradingPlatform.Strategies.Engine;

public sealed record ProductionNewIntent(
    string StrategyId,
    string Symbol,
    DateTimeOffset Timestamp,
    string Direction,
    string SignalId,
    string Reason);

public sealed record ProductionNewDecision(ProductionNewIntent Intent, bool Accepted, string Reason);

/// <summary>
/// Deterministic admission for simultaneous -new signals. The regime id cannot be admitted as an order.
/// Opposite signals are rejected rather than reversed.
/// </summary>
public static class ProductionNewCoordinator
{
    public static IReadOnlyList<ProductionNewDecision> Select(
        IReadOnlyList<ProductionNewIntent> intents,
        IReadOnlyDictionary<string, string> openDirectionBySymbol)
    {
        var ordered = intents
            .OrderBy(row => row.Timestamp)
            .ThenBy(row => row.StrategyId, StringComparer.Ordinal)
            .ThenBy(row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(row => row.SignalId, StringComparer.Ordinal)
            .ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var decisions = new List<ProductionNewDecision>(ordered.Count);
        foreach (var intent in ordered)
        {
            if (string.Equals(intent.StrategyId, StrategyTemplateKeys.MarketRegimeNew, StringComparison.Ordinal))
            {
                decisions.Add(new ProductionNewDecision(intent, false, "market-regime-new cannot place an order."));
                continue;
            }

            if (!seen.Add(intent.SignalId))
            {
                decisions.Add(new ProductionNewDecision(intent, false, "Duplicate signal id."));
                continue;
            }

            if (openDirectionBySymbol.TryGetValue(intent.Symbol, out var openSide))
            {
                decisions.Add(new ProductionNewDecision(
                    intent,
                    false,
                    string.Equals(openSide, intent.Direction, StringComparison.OrdinalIgnoreCase)
                        ? "Coin already has an open position in this direction."
                        : "Opposite signal is rejected. Positions are not silently reversed."));
                continue;
            }

            if (!taken.Add(intent.Symbol))
            {
                decisions.Add(new ProductionNewDecision(intent, false, "Another -new strategy already took this coin on this pass."));
                continue;
            }

            decisions.Add(new ProductionNewDecision(intent, true, "Admitted."));
        }

        return decisions;
    }

    public static string? CorrelationBlock(
        IReadOnlyList<(string Symbol, IReadOnlyList<decimal> Returns)> openBook,
        IReadOnlyList<decimal> candidateReturns,
        int minimumSample = 30,
        decimal maxCorrelation = 0.8m)
    {
        if (openBook.Count == 0)
        {
            return null;
        }

        if (candidateReturns.Count < minimumSample)
        {
            return "NO_TRADE: correlation sample is below 30. New exposure is rejected instead of assuming independence.";
        }

        foreach (var open in openBook)
        {
            if (open.Returns.Count < minimumSample)
            {
                return $"NO_TRADE: correlation versus {open.Symbol} is unreliable. New exposure is rejected.";
            }

            var rho = ProductionNewMath.Pearson(open.Returns, candidateReturns);
            if (rho is not { } value)
            {
                return $"NO_TRADE: correlation versus {open.Symbol} is undefined.";
            }

            if (value > maxCorrelation)
            {
                return $"NO_TRADE: correlation {value:0.00} versus {open.Symbol} exceeds {maxCorrelation:0.00}.";
            }
        }

        return null;
    }
}
