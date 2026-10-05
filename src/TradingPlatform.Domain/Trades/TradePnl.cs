using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Trades;

/// <summary>
/// One PnL model for every closed trade.
/// <list type="bullet">
/// <item><c>Trade.PnL</c> is gross price PnL in USDT (the same meaning as Binance <c>realizedPnl</c>).</item>
/// <item><c>Trade.Fees</c> with <c>FeeStatus</c>/<c>FeeAsset</c> is every commission on the trip, entry and exit.</item>
/// <item><c>Trade.FundingPnL</c> is funding received (positive) or paid (negative). Null means not recorded.</item>
/// <item><c>Trade.NetPnL</c> is gross minus fees plus recorded funding, and stays null ("pending") unless the fee is
/// certified in USDT. A fee in BNB or another asset is never subtracted from a USDT figure.</item>
/// </list>
/// </summary>
public static class TradePnl
{
    public const string SettlementAsset = "USDT";

    public static decimal? Net(decimal gross, FeeBook fee, decimal? funding) =>
        UsdtFee(fee) is { } amount ? gross - amount + (funding ?? 0m) : null;

    /// <summary>The fee as a USDT amount, or null when it is not certified in USDT. A known zero counts in any asset.</summary>
    public static decimal? UsdtFee(FeeBook fee)
    {
        if (fee.Status != FeeKnowledge.Known || fee.Amount is not { } amount)
        {
            return null;
        }

        return amount == 0m || string.Equals(fee.Asset, SettlementAsset, StringComparison.OrdinalIgnoreCase)
            ? amount
            : null;
    }

    /// <summary>Why <see cref="Net"/> is null, in operator words. Null when net is known.</summary>
    public static string? PendingReason(FeeBook fee) => fee.Status switch
    {
        FeeKnowledge.Known when fee.Amount is 0m => null,
        FeeKnowledge.Known when string.Equals(fee.Asset, SettlementAsset, StringComparison.OrdinalIgnoreCase) => null,
        FeeKnowledge.Known => $"Fee paid in {fee.Asset}. Net in USDT needs a recorded conversion rate.",
        FeeKnowledge.AssetMissing => "Fee asset is missing.",
        FeeKnowledge.Uncertain => "Fees are uncertain (mixed, duplicate or partial reports).",
        _ => "Fee not reported yet."
    };

    public static FeeBook Fee(Trade trade) => FeeBook.FromStored(trade.FeeStatus, trade.Fees, trade.FeeAsset);

    /// <summary>Recompute the stored net after gross, fee or funding changed.</summary>
    public static void Refresh(Trade trade) => trade.NetPnL = Net(trade.PnL, Fee(trade), trade.FundingPnL);

    /// <summary>
    /// Totals for a set of closed trades. <c>KnownNet</c> sums only trades whose net is known;
    /// <c>Pending</c> counts the rest so the UI can say "N trades pending fees" instead of guessing.
    /// </summary>
    public static PnlTotals Totals(IEnumerable<(decimal Gross, decimal? Net, decimal? Funding)> trades)
    {
        decimal gross = 0m, known = 0m, funding = 0m;
        int count = 0, pending = 0, fundingMissing = 0;
        foreach (var (g, n, f) in trades)
        {
            count++;
            gross += g;
            if (n is { } net)
            {
                known += net;
            }
            else
            {
                pending++;
            }

            if (f is { } paid)
            {
                funding += paid;
            }
            else
            {
                fundingMissing++;
            }
        }

        return new PnlTotals(gross, known, pending == 0 && count > 0 ? known : null, pending, funding, fundingMissing);
    }
}

/// <param name="Net">Total net, only when every trade's net is known.</param>
public sealed record PnlTotals(
    decimal Gross,
    decimal KnownNet,
    decimal? Net,
    int Pending,
    decimal Funding,
    int FundingMissing);
