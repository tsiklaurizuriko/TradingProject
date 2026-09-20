namespace TradingPlatform.Backtesting;

public enum ProfitFactorKind
{
    NoTrades,
    NoWins,
    NoLosses,
    Finite,
    Insufficient
}

public readonly record struct ProfitFactorValue(ProfitFactorKind Kind, decimal Ratio)
{
    public static ProfitFactorValue NoTrades { get; } = new(ProfitFactorKind.NoTrades, 0m);
    public static ProfitFactorValue Insufficient { get; } = new(ProfitFactorKind.Insufficient, 0m);

    public static ProfitFactorValue From(decimal positivePnlSum, decimal absoluteNegativePnlSum, int tradeCount)
    {
        if (tradeCount <= 0)
        {
            return NoTrades;
        }

        if (absoluteNegativePnlSum <= 0m)
        {
            return positivePnlSum > 0m
                ? new ProfitFactorValue(ProfitFactorKind.NoLosses, 0m)
                : new ProfitFactorValue(ProfitFactorKind.NoWins, 0m);
        }

        if (positivePnlSum <= 0m)
        {
            return new ProfitFactorValue(ProfitFactorKind.NoWins, 0m);
        }

        return new ProfitFactorValue(
            ProfitFactorKind.Finite,
            Math.Round(positivePnlSum / absoluteNegativePnlSum, 8, MidpointRounding.AwayFromZero));
    }

    public static ProfitFactorValue FromStored(PnlTotals totals)
    {
        if (totals.Trades <= 0)
        {
            return NoTrades;
        }

        var classified = totals.WinningTrades + totals.LosingTrades + totals.ZeroPnlTrades;
        if (classified != totals.Trades && totals.PositivePnlSum == 0m && totals.AbsoluteNegativePnlSum == 0m)
        {
            return Insufficient;
        }

        return From(totals.PositivePnlSum, totals.AbsoluteNegativePnlSum, totals.Trades);
    }

    public bool IsFinite => Kind == ProfitFactorKind.Finite;
    public decimal? FiniteRatio => IsFinite ? Ratio : null;

    public string Render() => Kind switch
    {
        ProfitFactorKind.NoTrades => "N/A",
        ProfitFactorKind.NoLosses => "Infinity",
        ProfitFactorKind.NoWins => "0",
        ProfitFactorKind.Insufficient => "unavailable",
        _ => Ratio.ToString("0.00000000")
    };

    public int Rank => Kind switch
    {
        ProfitFactorKind.NoWins => 0,
        ProfitFactorKind.Finite => 1,
        ProfitFactorKind.NoLosses => 2,
        _ => -1
    };
}

public sealed record PnlTotals(
    int Trades,
    int WinningTrades,
    int LosingTrades,
    int ZeroPnlTrades,
    decimal PositivePnlSum,
    decimal AbsoluteNegativePnlSum,
    decimal NetPnl,
    decimal Fees)
{
    public static PnlTotals Empty { get; } = new(0, 0, 0, 0, 0m, 0m, 0m, 0m);

    public ProfitFactorValue ProfitFactor => ProfitFactorValue.FromStored(this);

    public decimal GrossAfterSlippageBeforeFees => NetPnl + Fees;

    public decimal WinRate => Trades == 0
        ? 0m
        : Math.Round((decimal)WinningTrades / Trades * 100m, 8, MidpointRounding.AwayFromZero);

    public decimal Expectancy => Trades == 0
        ? 0m
        : Math.Round(NetPnl / Trades, 8, MidpointRounding.AwayFromZero);

    public static PnlTotals FromTrades(IEnumerable<ReplayTrade> trades)
    {
        var list = trades as IList<ReplayTrade> ?? trades.ToList();
        var wins = 0;
        var losses = 0;
        var zeros = 0;
        var positive = 0m;
        var negativeAbs = 0m;
        var net = 0m;
        var fees = 0m;
        foreach (var trade in list)
        {
            net += trade.PnL;
            fees += trade.Fees;
            if (trade.PnL > 0m)
            {
                wins++;
                positive += trade.PnL;
            }
            else if (trade.PnL < 0m)
            {
                losses++;
                negativeAbs += -trade.PnL;
            }
            else
            {
                zeros++;
            }
        }

        return new PnlTotals(list.Count, wins, losses, zeros, positive, negativeAbs, net, fees);
    }

    public static PnlTotals Combine(PnlTotals left, PnlTotals right) =>
        new(
            left.Trades + right.Trades,
            left.WinningTrades + right.WinningTrades,
            left.LosingTrades + right.LosingTrades,
            left.ZeroPnlTrades + right.ZeroPnlTrades,
            left.PositivePnlSum + right.PositivePnlSum,
            left.AbsoluteNegativePnlSum + right.AbsoluteNegativePnlSum,
            left.NetPnl + right.NetPnl,
            left.Fees + right.Fees);

    public static PnlTotals Combine(IEnumerable<PnlTotals> rows)
    {
        var acc = Empty;
        foreach (var row in rows)
        {
            acc = Combine(acc, row);
        }

        return acc;
    }
}

public sealed record BookReturnStats(
    int Books,
    decimal EqualBookReturn,
    decimal MeanReturn,
    decimal MedianReturn,
    decimal PositiveBookPercent);

public sealed record BookDrawdownStats(
    int Books,
    decimal Mean,
    decimal Median,
    decimal P90,
    decimal P95,
    decimal Maximum);

public sealed record WalkForwardStats(
    int TotalWindows,
    int WindowsWithTrades,
    int WindowsWithoutTrades,
    int NoLossesWindows,
    int NoWinsWindows,
    int FiniteWindows,
    ProfitFactorValue MedianProfitFactor,
    ProfitFactorValue WorstProfitFactor,
    ProfitFactorValue BestProfitFactor,
    string? WorstLabel,
    string? BestLabel,
    decimal MedianTradeCount,
    decimal TestNetPnl,
    decimal PositiveWindowPercent,
    ProfitFactorValue MeanFiniteProfitFactor);

public static class EqualBookAggregation
{
    public static decimal EqualBookReturn(IReadOnlyList<decimal> bookReturns) =>
        bookReturns.Count == 0 ? 0m : bookReturns.Average();

    public static BookReturnStats Returns(IReadOnlyList<decimal> bookReturns) =>
        new(
            bookReturns.Count,
            EqualBookReturn(bookReturns),
            EqualBookReturn(bookReturns),
            Median(bookReturns),
            PositiveShare(bookReturns));

    public static BookDrawdownStats Drawdowns(IReadOnlyList<decimal> bookDrawdowns) =>
        new(
            bookDrawdowns.Count,
            bookDrawdowns.Count == 0 ? 0m : bookDrawdowns.Average(),
            Median(bookDrawdowns),
            Percentile(bookDrawdowns, 90m),
            Percentile(bookDrawdowns, 95m),
            bookDrawdowns.Count == 0 ? 0m : bookDrawdowns.Max());

    public static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(v => v).ToList();
        var mid = ordered.Count / 2;
        return ordered.Count % 2 == 1 ? ordered[mid] : (ordered[mid - 1] + ordered[mid]) / 2m;
    }

    public static decimal Percentile(IReadOnlyList<decimal> values, decimal percentile)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(v => v).ToList();
        var index = (int)Math.Round((ordered.Count - 1) * percentile / 100m, MidpointRounding.AwayFromZero);
        return ordered[Math.Clamp(index, 0, ordered.Count - 1)];
    }

    public static decimal PositiveShare(IReadOnlyList<decimal> bookReturns) =>
        bookReturns.Count == 0
            ? 0m
            : Math.Round((decimal)bookReturns.Count(r => r > 0m) / bookReturns.Count * 100m, 8, MidpointRounding.AwayFromZero);

    public static decimal? EqualWeightNormalizedDrawdown(IReadOnlyList<IReadOnlyList<decimal>> bookEquity)
    {
        if (bookEquity.Count == 0)
        {
            return null;
        }

        var length = bookEquity[0].Count;
        if (length == 0 || bookEquity.Any(book => book.Count != length))
        {
            return null;
        }

        var peak = 0m;
        var maxDd = 0m;
        for (var i = 0; i < length; i++)
        {
            var equity = bookEquity.Average(book => book[i]);
            if (i == 0 || equity > peak)
            {
                peak = equity;
            }

            if (peak > 0m)
            {
                var dd = (peak - equity) / peak * 100m;
                if (dd > maxDd)
                {
                    maxDd = dd;
                }
            }
        }

        return maxDd;
    }
}

public static class WalkForwardAggregation
{
    public static WalkForwardStats From(IReadOnlyList<Validation.ValidationSlice> windows)
    {
        if (windows.Count == 0)
        {
            return new WalkForwardStats(
                0, 0, 0, 0, 0, 0,
                ProfitFactorValue.NoTrades,
                ProfitFactorValue.NoTrades,
                ProfitFactorValue.NoTrades,
                null,
                null,
                0m,
                0m,
                0m,
                ProfitFactorValue.NoTrades);
        }

        var withTrades = windows.Where(w => w.Result.NumberOfTrades > 0).ToList();
        var pfs = withTrades
            .Select(w => (Window: w, Pf: ValidationMetricsAggregator.ProfitFactorOf(w.Result)))
            .Where(x => x.Pf.Kind is ProfitFactorKind.Finite or ProfitFactorKind.NoWins or ProfitFactorKind.NoLosses)
            .ToList();
        var ordered = pfs.OrderBy(x => x.Pf.Rank).ThenBy(x => x.Pf.Ratio).ToList();
        ProfitFactorValue median;
        if (ordered.Count == 0)
        {
            median = ProfitFactorValue.NoTrades;
        }
        else
        {
            var mid = ordered.Count / 2;
            if (ordered.Count % 2 == 1)
            {
                median = ordered[mid].Pf;
            }
            else
            {
                var a = ordered[mid - 1].Pf;
                var b = ordered[mid].Pf;
                median = a.IsFinite && b.IsFinite
                    ? new ProfitFactorValue(ProfitFactorKind.Finite, (a.Ratio + b.Ratio) / 2m)
                    : b.Rank >= a.Rank ? b : a;
            }
        }
        (Validation.ValidationSlice Window, ProfitFactorValue Pf)? worst = ordered.Count == 0 ? null : ordered.First();
        (Validation.ValidationSlice Window, ProfitFactorValue Pf)? best = ordered.Count == 0 ? null : ordered.Last();
        var tradeCounts = withTrades.Select(w => (decimal)w.Result.NumberOfTrades).ToList();
        var net = withTrades.Sum(w => w.Result.NetProfit);
        var positive = withTrades.Count == 0
            ? 0m
            : Math.Round((decimal)withTrades.Count(w => w.Result.NetProfit > 0m) / withTrades.Count * 100m, 8, MidpointRounding.AwayFromZero);
        var finiteRatios = pfs.Where(x => x.Pf.IsFinite).Select(x => x.Pf.Ratio).ToList();
        var meanFinite = finiteRatios.Count == 0
            ? ProfitFactorValue.NoTrades
            : new ProfitFactorValue(ProfitFactorKind.Finite, Math.Round(finiteRatios.Average(), 8, MidpointRounding.AwayFromZero));
        return new WalkForwardStats(
            windows.Count,
            withTrades.Count,
            windows.Count - withTrades.Count,
            pfs.Count(x => x.Pf.Kind == ProfitFactorKind.NoLosses),
            pfs.Count(x => x.Pf.Kind == ProfitFactorKind.NoWins),
            pfs.Count(x => x.Pf.IsFinite),
            median,
            worst?.Pf ?? ProfitFactorValue.NoTrades,
            best?.Pf ?? ProfitFactorValue.NoTrades,
            worst?.Window.Label,
            best?.Window.Label,
            EqualBookAggregation.Median(tradeCounts),
            net,
            positive,
            meanFinite);
    }
}
