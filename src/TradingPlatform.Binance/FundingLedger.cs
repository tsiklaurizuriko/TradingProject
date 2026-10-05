namespace TradingPlatform.Binance;

/// <summary>
/// Binance <c>FUNDING_FEE</c> income rows, attributed to a closed trip by coin and time.
/// The income endpoint without a start time returns the last 7 days, capped at the row limit,
/// so a trip that opened before that window gets null (not recorded), never a partial sum.
/// </summary>
internal sealed class FundingLedger
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);
    private readonly IReadOnlyList<(string Symbol, decimal Amount, DateTimeOffset Time)> _rows;

    private FundingLedger(IReadOnlyList<(string Symbol, decimal Amount, DateTimeOffset Time)> rows, DateTimeOffset coveredFrom)
    {
        _rows = rows;
        CoveredFrom = coveredFrom;
    }

    public DateTimeOffset CoveredFrom { get; }

    public static FundingLedger From(IReadOnlyList<(string Symbol, decimal Amount, DateTimeOffset Time)> rows, DateTimeOffset now, int rowLimit)
    {
        var coveredFrom = now - DefaultWindow + TimeSpan.FromHours(1);
        if (rows.Count >= rowLimit && rows.Count > 0)
        {
            var oldest = rows.Min(row => row.Time);
            coveredFrom = oldest > coveredFrom ? oldest : coveredFrom;
        }

        return new FundingLedger(rows, coveredFrom);
    }

    /// <summary>Funding settled while the trip was open: after it opened, up to and including its close.</summary>
    public decimal? For(string symbol, DateTimeOffset openedAt, DateTimeOffset closedAt)
    {
        if (openedAt < CoveredFrom || closedAt < openedAt)
        {
            return null;
        }

        return _rows
            .Where(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                && row.Time > openedAt
                && row.Time <= closedAt)
            .Sum(row => row.Amount);
    }
}
