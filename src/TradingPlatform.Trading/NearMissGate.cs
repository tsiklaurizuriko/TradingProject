using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

public static class NearMissGate
{
    public static string? BlockReason(TradingOptions options, string? template, TradingMode mode)
    {
        if (!StrategyTemplateKeys.IsNearMiss(template))
        {
            return null;
        }

        _ = options;
        _ = mode;
        return null;
    }

    public static bool CandidateEnabled(PriceActionOptions price, string? template)
    {
        var key = StrategyTemplateKeys.Normalize(template);
        return price.Candidates is not null
            && price.Candidates.TryGetValue(key, out var row)
            && row.Enabled;
    }

    public static string RejectLabel(string? reason)
    {
        var text = reason ?? "";
        if (text.Contains("already has an open", StringComparison.OrdinalIgnoreCase)
            || text.Contains("already occupied", StringComparison.OrdinalIgnoreCase))
        {
            return "RejectedSameSymbol";
        }

        if (text.Contains("simultaneous", StringComparison.OrdinalIgnoreCase))
        {
            return "RejectedSlot";
        }

        if (text.Contains("portfolio", StringComparison.OrdinalIgnoreCase))
        {
            return "RejectedHeat";
        }

        return "RejectedPortfolioRisk";
    }
}
