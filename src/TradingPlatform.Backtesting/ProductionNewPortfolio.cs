namespace TradingPlatform.Backtesting;

public sealed record ProductionNewBookTrade(
    string StrategyId,
    string Symbol,
    string Side,
    DateTimeOffset OpenedAt,
    DateTimeOffset ClosedAt,
    decimal NetPnl,
    decimal GrossPnl,
    decimal Fees,
    decimal Slippage,
    decimal Funding,
    decimal BookEquity);

/// <summary>
/// Shared-capital overlay. Each trade's pnl is scaled by equity at entry divided by the equity used when the trade was simulated.
/// Simultaneous exposure is capped. This is diagnostic: signals were generated on isolated books, so a rejected trade is not replaced by another signal.
/// </summary>
public static class ProductionNewPortfolio
{
    public const string DiagnosticLabel = "DIAGNOSTIC_SHARED_CAPITAL";

    public sealed record Result(
        string Label,
        decimal InitialEquity,
        decimal FinalEquity,
        decimal NetPnl,
        decimal GrossPnl,
        decimal Fees,
        decimal Slippage,
        decimal Funding,
        decimal MaxDrawdownPercent,
        int Accepted,
        int Rejected,
        int LongTrades,
        int ShortTrades,
        IReadOnlyDictionary<string, int> RejectReasons);

    public static Result Simulate(
        IReadOnlyList<ProductionNewBookTrade> trades,
        decimal initialEquity = 10_000m,
        decimal riskPercent = 0.35m,
        decimal maxAggregateRiskPercent = 1.5m,
        decimal dailyLossPercent = 2m,
        int maxPositions = 8)
    {
        var ordered = trades
            .Where(trade => trade.ClosedAt >= trade.OpenedAt && trade.BookEquity > 0m)
            .OrderBy(trade => trade.OpenedAt)
            .ThenBy(trade => trade.StrategyId, StringComparer.Ordinal)
            .ThenBy(trade => trade.Symbol, StringComparer.Ordinal)
            .ToList();
        var equity = initialEquity;
        var peak = equity;
        var maxDd = 0m;
        var gross = 0m;
        var fees = 0m;
        var slip = 0m;
        var funding = 0m;
        var accepted = 0;
        var rejected = 0;
        var longs = 0;
        var shorts = 0;
        var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var open = new List<Open>();
        DateOnly? day = null;
        var dayStartEquity = equity;
        var dayPnl = 0m;

        foreach (var trade in ordered)
        {
            var tradeDay = DateOnly.FromDateTime(trade.OpenedAt.UtcDateTime);
            if (day != tradeDay)
            {
                day = tradeDay;
                dayStartEquity = equity;
                dayPnl = 0m;
            }

            open.RemoveAll(row => row.ClosedAt <= trade.OpenedAt);
            var reason = Admit(open, trade, equity, dayPnl, dayStartEquity, riskPercent, maxAggregateRiskPercent, dailyLossPercent, maxPositions);
            if (reason is not null)
            {
                rejected++;
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
                continue;
            }

            var scale = equity / trade.BookEquity;
            var net = trade.NetPnl * scale;
            equity += net;
            gross += trade.GrossPnl * scale;
            fees += trade.Fees * scale;
            slip += trade.Slippage * scale;
            funding += trade.Funding * scale;
            dayPnl += net;
            accepted++;
            if (string.Equals(trade.Side, "Short", StringComparison.OrdinalIgnoreCase))
            {
                shorts++;
            }
            else
            {
                longs++;
            }

            open.Add(new Open(trade.Symbol, trade.ClosedAt, riskPercent));
            peak = Math.Max(peak, equity);
            if (peak > 0m)
            {
                maxDd = Math.Max(maxDd, (peak - equity) / peak * 100m);
            }
        }

        return new Result(DiagnosticLabel, initialEquity, equity, equity - initialEquity, gross, fees, slip, funding, maxDd, accepted, rejected, longs, shorts, reasons);
    }

    private static string? Admit(
        List<Open> open,
        ProductionNewBookTrade trade,
        decimal equity,
        decimal dayPnl,
        decimal dayStartEquity,
        decimal riskPercent,
        decimal maxAggregateRiskPercent,
        decimal dailyLossPercent,
        int maxPositions)
    {
        if (equity <= 0m)
        {
            return "no-equity";
        }

        if (dayStartEquity > 0m && dayPnl <= -(dailyLossPercent / 100m) * dayStartEquity)
        {
            return "daily-loss";
        }

        if (open.Any(row => string.Equals(row.Symbol, trade.Symbol, StringComparison.OrdinalIgnoreCase)))
        {
            return "symbol-open";
        }

        if (open.Count >= maxPositions)
        {
            return "max-positions";
        }

        var openRisk = open.Sum(row => row.RiskPercent);
        if (openRisk + riskPercent > maxAggregateRiskPercent)
        {
            return "aggregate-risk";
        }

        return null;
    }

    private sealed record Open(string Symbol, DateTimeOffset ClosedAt, decimal RiskPercent);
}
