using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Trades;

public static class TradeFee
{
    /// <summary>
    /// Writes a commission onto a trade. A replace is a full recompute of the same trip.
    /// An add merges the next leg. An unknown or uncertain result never overwrites a stored amount with 0.
    /// </summary>
    public static void Apply(Trade trade, FeeBook next, bool replace)
    {
        var current = FeeBook.FromStored(trade.FeeStatus, trade.Fees, trade.FeeAsset);
        if (replace && next.Status == FeeKnowledge.Unknown && current.Status == FeeKnowledge.Known)
        {
            trade.FeeStatus = FeeKnowledge.Uncertain;
            trade.FeeAsset = null;
            return;
        }

        var chosen = replace ? next : FeeBook.Combine(current, next);
        trade.FeeStatus = chosen.Status;
        if (chosen.Status == FeeKnowledge.Known)
        {
            trade.Fees = chosen.Amount ?? 0m;
            trade.FeeAsset = chosen.Asset;
            return;
        }

        if (chosen.Status == FeeKnowledge.AssetMissing)
        {
            trade.Fees = chosen.Amount ?? trade.Fees;
            trade.FeeAsset = null;
            return;
        }

        trade.FeeAsset = null;
    }
}
