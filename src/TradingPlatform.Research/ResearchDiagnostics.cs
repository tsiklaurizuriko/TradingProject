using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.Research;

public static class ResearchDiagnostics
{
    public static void Fill(
        ResearchBookResult seed,
        ReplayResult replay,
        IReadOnlyList<MarketCandle> candles,
        out ResearchBookResult filled)
    {
        var holds = new List<double>();
        var mfe = new List<decimal>();
        var mae = new List<decimal>();
        var r1 = new List<decimal>();
        var r3 = new List<decimal>();
        var r5 = new List<decimal>();
        var r10 = new List<decimal>();
        foreach (var trade in replay.Trades)
        {
            holds.Add((trade.ClosedAt - trade.OpenedAt).TotalMinutes);
            var entryIdx = IndexAtOrAfter(candles, trade.OpenedAt);
            if (entryIdx < 0)
            {
                continue;
            }

            var longSide = string.Equals(trade.Side, "Long", StringComparison.OrdinalIgnoreCase);
            CaptureHorizon(candles, entryIdx, trade.EntryPrice, longSide, 1, r1);
            CaptureHorizon(candles, entryIdx, trade.EntryPrice, longSide, 3, r3);
            CaptureHorizon(candles, entryIdx, trade.EntryPrice, longSide, 5, r5);
            CaptureHorizon(candles, entryIdx, trade.EntryPrice, longSide, 10, r10);
            var exitIdx = IndexAtOrAfter(candles, trade.ClosedAt);
            if (exitIdx < entryIdx)
            {
                exitIdx = entryIdx;
            }

            decimal fav = 0m;
            decimal adv = 0m;
            for (var i = entryIdx; i <= Math.Min(exitIdx, candles.Count - 1); i++)
            {
                var highMove = longSide
                    ? candles[i].High - trade.EntryPrice
                    : trade.EntryPrice - candles[i].Low;
                var lowMove = longSide
                    ? trade.EntryPrice - candles[i].Low
                    : candles[i].High - trade.EntryPrice;
                if (highMove > fav) fav = highMove;
                if (lowMove > adv) adv = lowMove;
            }

            if (trade.EntryPrice != 0m)
            {
                mfe.Add(fav / trade.EntryPrice);
                mae.Add(adv / trade.EntryPrice);
            }
        }

        var (state, ratio) = ResearchPf.From(replay.Totals);
        filled = seed with
        {
            TradeCount = replay.NumberOfTrades,
            NetPnl = replay.NetProfit,
            Fees = replay.FeesPaid,
            WinRate = replay.WinRate,
            Expectancy = replay.Totals?.Expectancy ?? 0m,
            ProfitFactorState = state,
            ProfitFactor = ratio,
            PositivePnlSum = replay.Totals?.PositivePnlSum ?? 0m,
            AbsoluteNegativePnlSum = replay.Totals?.AbsoluteNegativePnlSum ?? 0m,
            WinningTrades = replay.Totals?.WinningTrades ?? 0,
            LosingTrades = replay.Totals?.LosingTrades ?? 0,
            ZeroPnlTrades = replay.Totals?.ZeroPnlTrades ?? 0,
            LongTotals = ReplayMetrics.SideTotals(replay.Long),
            ShortTotals = ReplayMetrics.SideTotals(replay.Short),
            CombinedTotals = replay.Totals,
            MeanHoldingMinutes = holds.Count == 0 ? null : (decimal)holds.Average(),
            MedianHoldingMinutes = holds.Count == 0 ? null : (decimal)Median(holds),
            MaxHoldingMinutes = holds.Count == 0 ? null : (decimal)holds.Max(),
            MfeMean = Mean(mfe),
            MaeMean = Mean(mae),
            Return1 = Mean(r1),
            Return3 = Mean(r3),
            Return5 = Mean(r5),
            Return10 = Mean(r10)
        };
    }

    public static string ClassifyAtSignal(IReadOnlyList<MarketCandle> candles, int index)
    {
        var start = Math.Max(0, index - 39);
        var window = candles.Skip(start).Take(index - start + 1).ToList();
        if (window.Count < 10 || window[0].Close <= 0m)
        {
            return "TRANSITION";
        }

        var first = window[0].Close;
        var last = window[^1].Close;
        var ret = (last - first) / first * 100m;
        var returns = new List<decimal>();
        for (var i = 1; i < window.Count; i++)
        {
            if (window[i - 1].Close > 0m)
            {
                returns.Add((window[i].Close - window[i - 1].Close) / window[i - 1].Close);
            }
        }

        var vol = 0m;
        if (returns.Count > 1)
        {
            var mean = returns.Average();
            vol = (decimal)Math.Sqrt((double)returns.Average(r => (r - mean) * (r - mean)));
        }

        if (vol >= 0.02m)
        {
            return "HIGH_VOLATILITY";
        }

        if (vol <= 0.004m)
        {
            return "LOW_VOLATILITY";
        }

        if (ret > 15m) return "STRONG_BULL";
        if (ret > 5m) return "BULL";
        if (ret < -15m) return "STRONG_BEAR";
        if (ret < -5m) return "BEAR";
        if (Math.Abs(ret) < 3m) return "RANGE";
        return "TRANSITION";
    }

    public static ResearchRobustness Summarize(string candidateId, IReadOnlyList<ResearchBookResult> books)
    {
        var scoped = books.Where(b => string.Equals(b.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase)).ToList();
        var oos = scoped.Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        var bySymbol = oos.GroupBy(b => b.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>()))
            .Where(t => t.Trades > 0)
            .ToList();
        var finite = bySymbol.Select(t => t.ProfitFactor).Where(p => p.IsFinite).Select(p => p.Ratio).OrderBy(x => x).ToList();
        var above = bySymbol.Count(t => t.ProfitFactor.IsFinite && t.ProfitFactor.Ratio > 1m || t.ProfitFactor.Kind == ProfitFactorKind.NoLosses);
        var below = bySymbol.Count(t => t.ProfitFactor.Kind == ProfitFactorKind.NoWins || t.ProfitFactor.IsFinite && t.ProfitFactor.Ratio < 1m);
        var nets = oos.GroupBy(b => b.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => Math.Abs(g.Sum(x => x.NetPnl)))
            .OrderByDescending(x => x)
            .ToList();
        var netSum = nets.Sum();
        var topTwo = nets.Take(2).Sum();
        var tf = oos.GroupBy(b => b.Timeframe, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => ResearchPf.Render(ResearchPf.From(CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())).State,
                    ResearchPf.From(CombinePf(g.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>())).Ratio),
                StringComparer.OrdinalIgnoreCase);

        var basePf = CombinePf(oos.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        var high = scoped.Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.High).ToList();
        var highPf = CombinePf(high.Select(x => x.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        var costFragile = basePf.ProfitFactor.IsFinite
            && basePf.ProfitFactor.Ratio >= 1m
            && (highPf.Trades == 0 || highPf.ProfitFactor.IsFinite && highPf.ProfitFactor.Ratio < 1m);

        var notes = new List<string>();
        if (bySymbol.Count >= 2 && netSum > 0m && topTwo / netSum >= 0.80m)
        {
            notes.Add("SYMBOL_CONCENTRATION: top two symbols hold >= 80% of |net|.");
        }

        if (tf.Count == 1)
        {
            notes.Add("Single timeframe in this run; timeframe dependence not identified.");
        }

        var status = ResearchStatuses.ResearchComplete;
        if (oos.Count == 0)
        {
            status = ResearchStatuses.InsufficientData;
        }

        return new ResearchRobustness(
            candidateId,
            bySymbol.Count,
            above,
            below,
            finite.Count == 0 ? null : Median(finite),
            finite.Count == 0 ? null : finite.Average(),
            finite.Count == 0 ? null : Percentile(finite, 0.10m),
            finite.Count == 0 ? null : Percentile(finite, 0.90m),
            netSum <= 0m ? 0m : topTwo / netSum,
            tf,
            costFragile,
            costFragile ? ResearchStatuses.CostFragile : status,
            notes);
    }

    public static PnlTotals CombinePf(IEnumerable<PnlTotals> rows)
    {
        var acc = PnlTotals.Empty;
        foreach (var row in rows)
        {
            acc = PnlTotals.Combine(acc, row);
        }

        return acc;
    }

    private static void CaptureHorizon(
        IReadOnlyList<MarketCandle> candles,
        int entryIdx,
        decimal entry,
        bool longSide,
        int bars,
        List<decimal> sink)
    {
        var j = entryIdx + bars;
        if (j >= candles.Count || entry == 0m)
        {
            return;
        }

        var move = (candles[j].Close - entry) / entry;
        sink.Add(longSide ? move : -move);
    }

    private static int IndexAtOrAfter(IReadOnlyList<MarketCandle> candles, DateTimeOffset time)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            if (candles[i].OpenTime >= time || candles[i].CloseTime >= time)
            {
                return i;
            }
        }

        return -1;
    }

    private static decimal? Mean(IReadOnlyList<decimal> values) =>
        values.Count == 0 ? null : values.Average();

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2.0;
    }

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2m;
    }

    private static decimal Percentile(IReadOnlyList<decimal> ordered, decimal p)
    {
        if (ordered.Count == 0)
        {
            return 0m;
        }

        var idx = (int)Math.Clamp((double)p * (ordered.Count - 1), 0, ordered.Count - 1);
        return ordered[idx];
    }
}
