using TradingPlatform.Backtesting.Validation;

namespace TradingPlatform.Backtesting;

public static class ValidationMetricsAggregator
{
    public static ReplayResult Combine(ReplayResult left, ReplayResult right)
    {
        var leftTotals = ResolveTotals(left, out var leftOk);
        var rightTotals = ResolveTotals(right, out var rightOk);
        if (!leftOk || !rightOk)
        {
            throw new InvalidOperationException(
                "Exact profit factor cannot be reconstructed from stripped summary fields. Persist PnlTotals or the trade list.");
        }

        var totals = PnlTotals.Combine(leftTotals, rightTotals);
        var pf = totals.ProfitFactor;
        var books = BookReturnsOf(left).Concat(BookReturnsOf(right)).ToList();
        var drawdowns = BookDrawdownsOf(left).Concat(BookDrawdownsOf(right)).ToList();
        var capital = left.InitialBalance;
        var equalBook = EqualBookAggregation.EqualBookReturn(books);
        return left with
        {
            FinalBalance = capital + totals.NetPnl,
            NetProfit = totals.NetPnl,
            ReturnPercent = Math.Round(equalBook * 100m, 8, MidpointRounding.AwayFromZero),
            NumberOfTrades = totals.Trades,
            WinRate = totals.WinRate,
            ProfitFactor = pf.IsFinite ? pf.Ratio : 0m,
            AverageWin = totals.WinningTrades == 0 ? 0m : Math.Round(totals.PositivePnlSum / totals.WinningTrades, 8, MidpointRounding.AwayFromZero),
            AverageLoss = totals.LosingTrades == 0 ? 0m : Math.Round(-(totals.AbsoluteNegativePnlSum / totals.LosingTrades), 8, MidpointRounding.AwayFromZero),
            MaximumDrawdown = drawdowns.Count == 0 ? 0m : drawdowns.Max(),
            SharpeRatio = null,
            FeesPaid = totals.Fees,
            LargestWinningTrade = MaxWin(left.LargestWinningTrade, right.LargestWinningTrade, totals.Trades),
            LargestLosingTrade = MinLoss(left.LargestLosingTrade, right.LargestLosingTrade, totals.Trades),
            BarsUsed = left.BarsUsed + right.BarsUsed,
            WindowStart = left.WindowStart < right.WindowStart ? left.WindowStart : right.WindowStart,
            WindowEnd = left.WindowEnd > right.WindowEnd ? left.WindowEnd : right.WindowEnd,
            Trades = [],
            Equity = [],
            Long = ReplayMetrics.CombineSides(EnsureSide(left, "Long"), EnsureSide(right, "Long")),
            Short = ReplayMetrics.CombineSides(EnsureSide(left, "Short"), EnsureSide(right, "Short")),
            Totals = totals,
            BookReturns = books,
            BookDrawdowns = drawdowns
        };
    }

    public static ReplayResult AttachBookStats(ReplayResult result)
    {
        var totals = ResolveTotals(result, out var ok);
        if (!ok)
        {
            return result with { SharpeRatio = result.BookReturns is { Count: > 1 } ? null : result.SharpeRatio };
        }

        return result with
        {
            Totals = totals,
            BookReturns = BookReturnsOf(result),
            BookDrawdowns = BookDrawdownsOf(result),
            Long = EnsureSide(result, "Long"),
            Short = EnsureSide(result, "Short")
        };
    }

    public static ReplayResult Strip(ReplayResult result) =>
        AttachBookStats(result) with { Trades = [], Equity = [] };

    public static ReplayResult FromTrades(
        IReadOnlyList<ReplayTrade> trades,
        decimal initialBalance = 10_000m,
        int barsUsed = 0)
    {
        var totals = PnlTotals.FromTrades(trades);
        var pf = totals.ProfitFactor;
        var dd = ReplayMetrics.ClosedPnlDrawdown(trades, initialBalance);
        var bookReturn = initialBalance == 0m ? 0m : totals.NetPnl / initialBalance;
        return new ReplayResult(
            initialBalance,
            initialBalance + totals.NetPnl,
            totals.NetPnl,
            Math.Round(bookReturn * 100m, 8, MidpointRounding.AwayFromZero),
            totals.Trades,
            totals.WinRate,
            pf.IsFinite ? pf.Ratio : 0m,
            totals.WinningTrades == 0 ? 0m : Math.Round(totals.PositivePnlSum / totals.WinningTrades, 8, MidpointRounding.AwayFromZero),
            totals.LosingTrades == 0 ? 0m : Math.Round(-(totals.AbsoluteNegativePnlSum / totals.LosingTrades), 8, MidpointRounding.AwayFromZero),
            Math.Round(dd, 8, MidpointRounding.AwayFromZero),
            null,
            totals.Fees,
            trades.Count == 0 ? 0m : trades.Max(t => t.PnL),
            trades.Count == 0 ? 0m : trades.Min(t => t.PnL),
            barsUsed,
            trades.Count == 0 ? DateTimeOffset.UnixEpoch : trades.Min(t => t.OpenedAt),
            trades.Count == 0 ? DateTimeOffset.UnixEpoch : trades.Max(t => t.ClosedAt),
            "Synthetic aggregation fixture. Costs are already inside ReplayTrade.PnL.",
            trades,
            [],
            ReplayMetrics.ForSide(trades, "Long"),
            ReplayMetrics.ForSide(trades, "Short"),
            Totals: totals,
            BookReturns: [bookReturn],
            BookDrawdowns: [dd]);
    }

    public static PnlTotals ResolveTotals(ReplayResult result) =>
        ResolveTotals(result, out _);

    public static PnlTotals ResolveTotals(ReplayResult result, out bool exact)
    {
        if (result.Totals is not null &&
            (result.Totals.Trades == result.NumberOfTrades || result.Trades.Count == 0 && result.Totals.Trades > 0))
        {
            exact = result.Totals.ProfitFactor.Kind != ProfitFactorKind.Insufficient;
            return result.Totals;
        }

        if (result.Trades.Count > 0)
        {
            exact = true;
            return PnlTotals.FromTrades(result.Trades);
        }

        if (result.NumberOfTrades == 0)
        {
            exact = true;
            return result.Totals ?? PnlTotals.Empty;
        }

        var longTotals = ReplayMetrics.SideTotals(result.Long);
        var shortTotals = ReplayMetrics.SideTotals(result.Short);
        var combined = PnlTotals.Combine(longTotals, shortTotals);
        if (combined.Trades == result.NumberOfTrades &&
            combined.ProfitFactor.Kind != ProfitFactorKind.Insufficient)
        {
            exact = true;
            return combined;
        }

        exact = false;
        return combined.Trades > 0 ? combined : PnlTotals.Empty;
    }

    public static ProfitFactorValue ProfitFactorOf(ReplayResult result)
    {
        var totals = ResolveTotals(result, out var exact);
        return exact ? totals.ProfitFactor : ProfitFactorValue.Insufficient;
    }

    public static ProfitFactorValue ProfitFactorOf(ReplaySideMetrics? side)
    {
        if (side is null || side.Trades <= 0)
        {
            return ProfitFactorValue.NoTrades;
        }

        return ProfitFactorValue.FromStored(ReplayMetrics.SideTotals(side));
    }

    public static IReadOnlyList<decimal> BookReturnsOf(ReplayResult result)
    {
        if (result.BookReturns is { Count: > 0 } listed)
        {
            return listed;
        }

        var initial = result.InitialBalance == 0m ? 10_000m : result.InitialBalance;
        return [result.NetProfit / initial];
    }

    public static IReadOnlyList<decimal> BookDrawdownsOf(ReplayResult result) =>
        result.BookDrawdowns is { Count: > 0 } listed ? listed : [result.MaximumDrawdown];

    public static string FormatReplay(ReplayResult? result) => FormatSlice(result);

    public static string FormatSlice(ReplayResult? result)
    {
        if (result is null)
        {
            return "n/a";
        }

        var totals = ResolveTotals(result);
        return
            $"n={totals.Trades} Combined PF={ProfitFactorOf(result).Render()} " +
            $"LONG PF={ProfitFactorOf(result.Long).Render()} SHORT PF={ProfitFactorOf(result.Short).Render()} " +
            $"net={totals.NetPnl:0.00} fees={totals.Fees:0.00} win={totals.WinRate:0.00}% exp={totals.Expectancy:0.00} " +
            $"+W={totals.PositivePnlSum:0.00} |L|={totals.AbsoluteNegativePnlSum:0.00}";
    }

    public static string FormatWalk(IReadOnlyList<ValidationSlice> windows)
    {
        if (windows.Count == 0)
        {
            return "no windows (insufficient bars)";
        }

        var stats = WalkForwardAggregation.From(windows);
        var worst = stats.WorstLabel is null ? "n/a" : $"{stats.WorstLabel} PF {stats.WorstProfitFactor.Render()}";
        var best = stats.BestLabel is null ? "n/a" : $"{stats.BestLabel} PF {stats.BestProfitFactor.Render()}";
        return
            $"{stats.TotalWindows} windows ({stats.WindowsWithTrades} with trades, {stats.WindowsWithoutTrades} empty); " +
            $"empty PF=N/A; mean finite PF {stats.MeanFiniteProfitFactor.Render()}; median PF {stats.MedianProfitFactor.Render()}; " +
            $"no-loss windows {stats.NoLossesWindows}; no-win windows {stats.NoWinsWindows}; " +
            $"median trades/non-empty {stats.MedianTradeCount:0.##}; test net {stats.TestNetPnl:0.00}; " +
            $"positive-window {stats.PositiveWindowPercent:0.00}%; worst {worst}; best {best}";
    }

    private static ReplaySideMetrics EnsureSide(ReplayResult result, string side)
    {
        var metrics = string.Equals(side, "Short", StringComparison.OrdinalIgnoreCase) ? result.Short : result.Long;
        if (metrics.Trades == 0 || metrics.WinningTrades + metrics.LosingTrades + metrics.ZeroPnlTrades == metrics.Trades)
        {
            return metrics;
        }

        if (result.Trades.Count > 0)
        {
            return ReplayMetrics.ForSide(result.Trades, side);
        }

        return metrics;
    }

    private static decimal MaxWin(decimal left, decimal right, int trades)
    {
        if (trades == 0)
        {
            return 0m;
        }

        return Math.Max(left, right);
    }

    private static decimal MinLoss(decimal left, decimal right, int trades)
    {
        if (trades == 0)
        {
            return 0m;
        }

        if (left == 0m)
        {
            return right;
        }

        if (right == 0m)
        {
            return left;
        }

        return Math.Min(left, right);
    }
}
