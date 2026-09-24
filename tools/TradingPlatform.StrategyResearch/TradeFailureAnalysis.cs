using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

internal static class TradeFailureAnalysis
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Arm[] Arms =
    [
        new("ema_rsi_trend", "FROZEN_TREND", "FROZEN", "15m", ArmKind.Frozen),
        new("macd_trend", "FROZEN_TREND", "FROZEN", "15m", ArmKind.Frozen),
        new("donchian_breakout", "FROZEN_TREND", "FROZEN", "15m", ArmKind.Frozen),
        new("rsi_pullback", "FROZEN_MEAN", "FROZEN", "15m", ArmKind.Frozen),
        new("bollinger_reversion", "FROZEN_MEAN", "FROZEN", "15m", ArmKind.Frozen),
        new("FF-SWEEP-HOURLY", "PA_SWEEP", "FINAL_FIVE", "15m", ArmKind.FinalFive),
        new("FF-FAILED-DOWN", "PA_FAILED", "FINAL_FIVE", "15m", ArmKind.FinalFive),
        new("FF-EXPANSION-BOS", "PA_CONTINUATION", "FINAL_FIVE", "15m", ArmKind.FinalFive),
        new("FF-RANGE-RELEASE", "PA_CONTINUATION", "FINAL_FIVE", "1h", ArmKind.FinalFive),
        new("FF-RSI-QUIET", "PA_STRUCTURE", "FINAL_FIVE", "1h", ArmKind.FinalFive),
        new("CPA-SWEEP", "PA_SWEEP", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-PULLBACK", "PA_STRUCTURE", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-WM", "PA_STRUCTURE", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-COMPRESSION", "PA_CONTINUATION", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-MTF", "PA_STRUCTURE", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-FAILED_BREAKOUT", "PA_FAILED", "PHASE8", "5m", ArmKind.Contextual)
    ];

    private static readonly string[] Labels =
    [
        "IMMEDIATELY_WRONG",
        "RIGHT_DIRECTION_BAD_ENTRY",
        "RIGHT_DIRECTION_STOP_TOO_TIGHT",
        "RIGHT_DIRECTION_EXIT_TOO_EARLY",
        "COST_DOMINATED",
        "NO_CLEAR_CAUSE"
    ];

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        var dir = Path.Combine(root, "artifacts", "strategy-research", "trade-failure");
        Directory.CreateDirectory(dir);
        var hash = WriteManifest(dir);
        var rows = new List<Diag>();
        var btcHourly = await LoadOne(cacheDir, root, "BTCUSDT", "1h");
        var btcCache = new CausalIndicatorCache(btcHourly);
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            Console.WriteLine($"Trade failure loading {symbol}.");
            var series = await LoadSymbol(cacheDir, root, symbol);
            var caches = series.ToDictionary(pair => pair.Key.Timeframe, pair => new CausalIndicatorCache(pair.Value), StringComparer.OrdinalIgnoreCase);
            var contextual = ContextualPriceActionSignals.BuildAll(caches);
            foreach (var arm in Arms)
            {
                var candles = series[(symbol, arm.Timeframe)];
                var signals = SignalsFor(arm, caches, contextual);
                var trades = Replay(arm, symbol, candles, signals);
                var openIndex = new Dictionary<DateTimeOffset, int>();
                var closeIndex = new Dictionary<DateTimeOffset, int>();
                for (var i = 0; i < candles.Count; i++)
                {
                    openIndex[candles[i].OpenTime] = i;
                    closeIndex[candles[i].CloseTime] = i;
                }

                var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
                foreach (var trade in trades)
                {
                    if (!openIndex.TryGetValue(trade.OpenedAt, out var fill) || fill <= 0 || !TryExit(trade, openIndex, closeIndex, out var exit) || exit < fill)
                    {
                        continue;
                    }

                    var facts = TradeFailurePath.Measure(
                        candles, fill, exit, trade.Side.Equals("Long", StringComparison.OrdinalIgnoreCase),
                        trade.EntryPrice, trade.Reason, trade.GrossPnl, trade.Fees, trade.SlippageCost, trade.FundingPnl, trade.PnL);
                    var signal = fill - 1;
                    var phase = signal < insEnd ? "IS" : signal < valEnd ? "VALIDATION" : "OOS";
                    var block = Math.Clamp((signal * 4 / candles.Count) + 1, 1, 4);
                    var feature = EdgeFeatureBuilder.Build(
                        arm.Group, arm.Id, arm.Id, symbol, arm.Timeframe, trade.Side, phase, block,
                        trade.ClosedAt, trade.PnL, trade.Fees, trade.SlippageCost,
                        caches[arm.Timeframe], caches["1h"], btcCache, signal);
                    var vol = feature.VolHigh == true ? "HIGH" : feature.VolLow == true ? "LOW" : feature.VolNormal == true ? "NORMAL" : "UNKNOWN";
                    var trend = feature.AdxTrend == true ? "TREND" : feature.AdxRange == true ? "RANGE" : "TRANSITION";
                    rows.Add(new Diag(arm.Group, arm.ResearchFamily, arm.Id, symbol, arm.Timeframe, feature.Side, phase, block, vol, trend, TradeFailurePath.Classify(facts), facts));
                }

                Console.WriteLine($"{symbol} {arm.Id} diagnosed={trades.Count}.");
            }
        }

        var phase4 = Phase4Costs(Path.Combine(root, "artifacts", "strategy-research", "futures-alpha-phase4", "trades.json"));
        WriteJson(Path.Combine(dir, "summary.json"), new { hash, Trades = rows.Count, Losers = rows.Count(x => x.Label != "WINNER"), Phase4 = phase4.Trades });
        WriteReport(root, hash, rows, phase4);
        Console.WriteLine("TRADE FAILURE ANALYSIS COMPLETE");
        return 0;
    }

    private static string WriteManifest(string dir)
    {
        var body = JsonSerializer.Serialize(new
        {
            TradeFailureCatalog.Version,
            TradeFailureCatalog.Rule,
            TradeFailureCatalog.StopDistance,
            TradeFailureCatalog.TargetDistance,
            TradeFailureCatalog.ImmediateBars,
            TradeFailureCatalog.ImmediateAdverse,
            TradeFailureCatalog.ImmediateFavorableCeiling,
            TradeFailureCatalog.HalfStop,
            TradeFailureCatalog.HorizonBars,
            Arms = Arms.Select(x => new { x.Id, x.Group, x.ResearchFamily, x.Timeframe }).ToArray(),
            Note = "Same reconstructed arms as edge discovery. No new strategy. Near-miss is not replayed. Per-trade rows were not stored by the prior phase, so the identical book is replayed only to attach path diagnostics."
        }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, "definitions.json"), JsonSerializer.Serialize(new { Sha256 = hash, Body = JsonDocument.Parse(body).RootElement }, JsonOptions));
        return hash;
    }

    private static void WriteReport(string root, string hash, IReadOnlyList<Diag> rows, Phase4Cost phase4)
    {
        var losers = rows.Where(x => x.Label != "WINNER").ToList();
        var winners = rows.Where(x => x.Label == "WINNER").ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Trade failure analysis");
        sb.AppendLine();
        sb.AppendLine($"Definition SHA-256 `{hash}`.");
        sb.AppendLine();
        sb.AppendLine("This is a diagnosis of the existing reconstructed book. It is not an edge. It is not a new strategy. No stop, target, or filter was changed.");
        sb.AppendLine();
        sb.AppendLine("## 1. Dataset and coverage");
        sb.AppendLine();
        sb.AppendLine($"Path-diagnosed trades: {rows.Count}. Winners: {winners.Count}. Losers: {losers.Count}.");
        sb.AppendLine("Arms: Frozen Five at 15m, Final Five primary timeframes, Phase 8 baselines at 5m. Near-miss was not added. Symbols: BTCUSDT, ETHUSDT, BNBUSDT.");
        sb.AppendLine("The prior edge-discovery run stored aggregates only. These are the same arms and the same Model B book, replayed so each trade can carry a path. No new rule was introduced.");
        sb.AppendLine($"Phase 4 baseline trades used for costs only: {phase4.Trades}. They have no exit time, so they are not in the loser taxonomy.");
        sb.AppendLine();
        sb.AppendLine("## 2. Definitions");
        sb.AppendLine();
        sb.AppendLine(TradeFailureCatalog.Rule);
        sb.AppendLine();
        sb.AppendLine("Excursions are fractions of the fill price. Displayed percents are that fraction times 100. Horizon returns are the maximum favorable excursion inside 1, 3, 5, 10, and 20 bars from the fill, and they stop at the last available bar rather than borrowing a future bar that does not exist.");
        sb.AppendLine();
        sb.AppendLine("## 3. Global MAE and MFE");
        sb.AppendLine();
        sb.AppendLine("| Set | n | MAE p25 | MAE p50 | MAE p75 | MFE p25 | MFE p50 | MFE p75 | Hold p50 bars |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        LineDist(sb, "All", rows);
        LineDist(sb, "Winners", winners);
        LineDist(sb, "Losers", losers);
        sb.AppendLine();
        sb.AppendLine("Loser MAE buckets, as shares of the existing 2% stop:");
        sb.AppendLine();
        Bucket(sb, losers, x => x.Facts.Mae, [(0.005m, "<0.5%"), (0.01m, "0.5–1%"), (0.02m, "1–2%"), (decimal.MaxValue, ">=2%")]);
        sb.AppendLine();
        sb.AppendLine("Loser MFE buckets:");
        sb.AppendLine();
        Bucket(sb, losers, x => x.Facts.Mfe, [(0.005m, "<0.5%"), (0.01m, "0.5–1%"), (0.02m, "1–2%"), (0.04m, "2–4%"), (decimal.MaxValue, ">=4%")]);
        sb.AppendLine();
        sb.AppendLine("## 4. Losing-trade taxonomy");
        sb.AppendLine();
        sb.AppendLine("| Label | n | Share of losers |");
        sb.AppendLine("| --- | ---: | ---: |");
        foreach (var label in Labels.OrderByDescending(label => losers.Count(x => x.Label == label)))
        {
            var n = losers.Count(x => x.Label == label);
            sb.AppendLine($"| {label} | {n} | {Share(n, losers.Count)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 5. Family comparison");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Group);
        sb.AppendLine();
        sb.AppendLine("Research family:");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.ResearchFamily);
        sb.AppendLine();
        sb.AppendLine("## 6. Long vs short");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Side);
        foreach (var side in new[] { "LONG", "SHORT" })
        {
            var sample = rows.Where(x => x.Side == side).ToList();
            sb.AppendLine($"- {side} all trades: n={sample.Count} gross expectancy {Money(Mean(sample, x => x.Facts.Gross))} net expectancy {Money(Mean(sample, x => x.Facts.Net))}");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Symbols");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Symbol);
        sb.AppendLine();
        sb.AppendLine("## 8. Timeframes");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Timeframe);
        sb.AppendLine();
        sb.AppendLine("## 9. IS, validation, OOS");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Phase);
        sb.AppendLine();
        sb.AppendLine("## 10. Chronological blocks");
        sb.AppendLine();
        AppendSlice(sb, losers, x => "BLOCK" + x.Block);
        sb.AppendLine();
        sb.AppendLine("Volatility and trend, from the signal bar only:");
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Vol);
        sb.AppendLine();
        AppendSlice(sb, losers, x => x.Trend);
        sb.AppendLine();
        sb.AppendLine("## 11. Costs");
        sb.AppendLine();
        sb.AppendLine($"Reconstructed book: gross expectancy {Money(Mean(rows, x => x.Facts.Gross))}, net expectancy {Money(Mean(rows, x => x.Facts.Net))}.");
        sb.AppendLine($"Mean fees {Money(Mean(rows, x => x.Facts.Fees))}, mean slippage {Money(Mean(rows, x => x.Facts.Slippage))}, mean funding {Money(Mean(rows, x => x.Facts.Funding))}.");
        var costDominated = losers.Count(x => x.Label == "COST_DOMINATED");
        sb.AppendLine($"Losers whose gross was non-negative: {costDominated} ({Share(costDominated, losers.Count)}).");
        sb.AppendLine($"Phase 4 baselines, all symbols in that file: n={phase4.Trades}, gross expectancy {Money(phase4.GrossExpectancy)}, net expectancy {Money(phase4.NetExpectancy)}, mean funding {Money(phase4.FundingExpectancy)}, cost-flipped losers {phase4.CostFlipped}.");
        sb.AppendLine();
        sb.AppendLine("## 12. Cross-family failure modes");
        sb.AppendLine();
        var shared = new List<string>();
        foreach (var label in Labels)
        {
            var groups = losers.GroupBy(x => x.Group).Where(g =>
            {
                var n = g.Count();
                var hit = g.Count(x => x.Label == label);
                return n >= TradeFailureCatalog.MinimumFamilyLosers && hit / (decimal)n >= TradeFailureCatalog.SharedModeShare;
            }).Select(g => g.Key).ToList();
            sb.AppendLine($"- {label}: groups at or above 30% with at least 50 losers: {(groups.Count == 0 ? "none" : string.Join(", ", groups))}.");
            if (groups.Count >= 2)
            {
                shared.Add(label);
            }
        }

        sb.AppendLine();
        var stops = losers.Where(x => x.Facts.Reason == "Stop loss").ToList();
        var favorableBeforeStop = stops.Count(x => x.Facts.MfeBeforeStop > 0m);
        var recovered = losers.Count(x => x.Facts.Mfe20 >= TradeFailureCatalog.StopDistance);
        var fast = losers.Count(x => x.Facts.HoldBars <= TradeFailureCatalog.ImmediateBars);
        var slow = losers.Count(x => x.Facts.HoldBars > 10);
        var neitherQuick = rows.Where(x => (x.Facts.BarsToStop is null || x.Facts.BarsToStop > 3) && (x.Facts.BarsToTarget is null || x.Facts.BarsToTarget > 3)).ToList();
        sb.AppendLine("## 13. Observed failure mechanisms");
        sb.AppendLine();
        sb.AppendLine("Ranked by loser count. A rank is not a proposal to change the book.");
        sb.AppendLine();
        var rank = 1;
        foreach (var label in Labels.OrderByDescending(label => losers.Count(x => x.Label == label)).Take(5))
        {
            var n = losers.Count(x => x.Label == label);
            sb.AppendLine($"{rank}. {label}: {n} losers, {Share(n, losers.Count)}.");
            rank++;
        }

        sb.AppendLine();
        sb.AppendLine($"Signal path: {recovered} losers ({Share(recovered, losers.Count)}) reached a 2% favorable excursion inside 20 bars of entry, including bars after the exit.");
        var afterStop = stops.Count(x => x.Facts.RecoveredAfterStop);
        sb.AppendLine($"Stop path: {favorableBeforeStop} of {stops.Count} stop exits ({Share(favorableBeforeStop, stops.Count)}) had some favorable excursion on a bar strictly before the stop bar.");
        sb.AppendLine($"After the stop bar, {afterStop} of {stops.Count} ({Share(afterStop, stops.Count)}) later traded 2% in the original direction inside 20 bars. That count is not a new stop.");
        sb.AppendLine($"Timing: {fast} losers ({Share(fast, losers.Count)}) were closed within 3 bars. {slow} ({Share(slow, losers.Count)}) were held more than 10 bars.");
        if (neitherQuick.Count > 0)
        {
            sb.AppendLine($"Trades that touched neither the 2% stop nor the 4% target within 3 bars: {neitherQuick.Count}. Their median 20-bar favorable excursion is {P(neitherQuick, x => x.Facts.Mfe20 ?? 0m, 0.5m)}% and their median in-trade MAE is {P(neitherQuick, x => x.Facts.Mae, 0.5m)}%.");
        }

        sb.AppendLine();
        sb.AppendLine("Winners are not evidence of an edge. They are the other half of the same book.");
        sb.AppendLine();
        var hotWinners = winners.Count(x => x.Facts.Reason == "Take profit" && x.Facts.MaeBeforeTarget >= TradeFailureCatalog.HalfStop);
        sb.AppendLine($"Take-profit winners whose adverse excursion before the target bar reached 1%: {hotWinners} of {winners.Count(x => x.Facts.Reason == "Take profit")}.");
        sb.AppendLine($"Winner median hold {Pct(winners.Select(x => (decimal)x.Facts.HoldBars).OrderBy(x => x).ToList(), 0.5m).ToString("0", CultureInfo.InvariantCulture)} bars. Loser median hold {Pct(losers.Select(x => (decimal)x.Facts.HoldBars).OrderBy(x => x).ToList(), 0.5m).ToString("0", CultureInfo.InvariantCulture)} bars.");
        sb.AppendLine();
        sb.AppendLine("## 14. What the evidence does not support");
        sb.AppendLine();
        var gross = Mean(rows, x => x.Facts.Gross);
        var net = Mean(rows, x => x.Facts.Net);
        if (gross < 0m)
        {
            sb.AppendLine($"Gross expectancy is {Money(gross)} and net expectancy is {Money(net)}. The loss is present before fees, slippage, and funding. A cost-only account does not fit this book.");
        }
        else
        {
            sb.AppendLine($"Gross expectancy is {Money(gross)} and net expectancy is {Money(net)}. Costs change the sign or the size. That still does not identify a tradable condition.");
        }

        var mode = Labels.OrderByDescending(label => losers.Count(x => x.Label == label)).First();
        sb.AppendLine($"The largest loser label is {mode}. The other labels are smaller. This phase does not convert the largest label into a new exit.");
        if (shared.Count == 0)
        {
            sb.AppendLine("No loser label reached the pre-registered cross-family bar of two groups, 50 losers, and a 30% share. A single-family pattern is not treated as a shared mechanism.");
        }
        else
        {
            sb.AppendLine("Shared labels under that bar: " + string.Join(", ", shared) + ". Sharing a failure mode is not an edge.");
        }

        var isMode = Mode(losers.Where(x => x.Phase == "IS").ToList());
        var oosMode = Mode(losers.Where(x => x.Phase == "OOS").ToList());
        sb.AppendLine($"IS loser mode is {isMode}. OOS loser mode is {oosMode}. {(isMode == oosMode ? "The same mode appears in both windows." : "The mode is not the same in both windows.")}");
        sb.AppendLine();
        sb.AppendLine("## 15. Next research directions");
        sb.AppendLine();
        sb.AppendLine("The open question is which of the recorded labels dominates, and whether that dominance is the same in two families and in IS and OOS. Answering it does not require a new stop, a new target, or a new strategy.");
        sb.AppendLine("Phase 4 still cannot support path labels until an exit time is stored. Funding on that file is a cost fact, not a path fact.");
        sb.AppendLine("No parameter change is proposed. Paper is off. Live is off. Nothing is VALIDATED_FOR_PAPER.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.");
        File.WriteAllText(Path.Combine(root, "docs", "TRADE_FAILURE_ANALYSIS_REPORT.md"), sb.ToString());
    }

    private static void LineDist(StringBuilder sb, string name, IReadOnlyList<Diag> rows)
    {
        sb.AppendLine($"| {name} | {rows.Count} | {P(rows, x => x.Facts.Mae, 0.25m)} | {P(rows, x => x.Facts.Mae, 0.5m)} | {P(rows, x => x.Facts.Mae, 0.75m)} | {P(rows, x => x.Facts.Mfe, 0.25m)} | {P(rows, x => x.Facts.Mfe, 0.5m)} | {P(rows, x => x.Facts.Mfe, 0.75m)} | {Pct(rows.Select(x => (decimal)x.Facts.HoldBars).OrderBy(x => x).ToList(), 0.5m):0} |");
    }

    private static void Bucket(StringBuilder sb, IReadOnlyList<Diag> rows, Func<Diag, decimal> value, (decimal Max, string Name)[] cuts)
    {
        var start = 0m;
        foreach (var cut in cuts)
        {
            var n = rows.Count(x =>
            {
                var current = value(x);
                return cut.Max == decimal.MaxValue ? current >= start : current >= start && current < cut.Max;
            });
            sb.AppendLine($"- {cut.Name}: {n} ({Share(n, rows.Count)})");
            start = cut.Max;
        }
    }

    private static void AppendSlice(StringBuilder sb, IReadOnlyList<Diag> losers, Func<Diag, string> key)
    {
        sb.AppendLine("| Slice | Losers | Mode | Mode share | Gross exp | Net exp |");
        sb.AppendLine("| --- | ---: | --- | ---: | ---: | ---: |");
        foreach (var group in losers.GroupBy(key).OrderBy(g => g.Key))
        {
            var list = group.ToList();
            var mode = Mode(list);
            var n = list.Count(x => x.Label == mode);
            sb.AppendLine($"| {group.Key} | {list.Count} | {mode} | {Share(n, list.Count)} | {Money(Mean(list, x => x.Facts.Gross))} | {Money(Mean(list, x => x.Facts.Net))} |");
        }

        sb.AppendLine();
    }

    private static string Mode(IReadOnlyList<Diag> losers) =>
        losers.Count == 0 ? "n/a" : Labels.OrderByDescending(label => losers.Count(x => x.Label == label)).First();

    private static string P(IReadOnlyList<Diag> rows, Func<Diag, decimal> value, decimal p) =>
        (Pct(rows.Select(value).OrderBy(x => x).ToList(), p) * 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal Pct(IReadOnlyList<decimal> sorted, decimal p)
    {
        if (sorted.Count == 0)
        {
            return 0m;
        }

        var index = (int)Math.Round((sorted.Count - 1) * p, MidpointRounding.AwayFromZero);
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    private static decimal Mean<T>(IReadOnlyList<T> rows, Func<T, decimal> value) =>
        rows.Count == 0 ? 0m : rows.Sum(value) / rows.Count;

    private static string Share(int n, int d) =>
        d == 0 ? "n/a" : (n / (decimal)d).ToString("0.0%", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static Phase4Cost Phase4Costs(string path)
    {
        if (!File.Exists(path))
        {
            return new Phase4Cost(0, 0m, 0m, 0m, 0);
        }

        var rows = JsonSerializer.Deserialize<List<Phase4Row>>(File.ReadAllText(path), JsonOptions) ?? [];
        var kept = rows.Where(x =>
            x.CostLabel == "BASE"
            && x.CandidateId is "funding_basis_rv|baseline|continuation" or "funding_extreme_momentum_exhaustion|baseline|continuation"
            && x.Phase is "IS" or "VALIDATION" or "OOS").ToList();
        var losers = kept.Where(x => x.PnL < 0m).ToList();
        return new Phase4Cost(
            kept.Count,
            kept.Count == 0 ? 0m : kept.Average(x => x.GrossPnl),
            kept.Count == 0 ? 0m : kept.Average(x => x.PnL),
            kept.Count == 0 ? 0m : kept.Average(x => x.FundingPnl),
            losers.Count(x => x.GrossPnl >= 0m));
    }

    private static bool TryExit(ReplayTrade trade, Dictionary<DateTimeOffset, int> opens, Dictionary<DateTimeOffset, int> closes, out int exit)
    {
        if (trade.Reason is "Stop loss" or "Take profit" or "TIME" or "End of window")
        {
            return closes.TryGetValue(trade.ClosedAt, out exit);
        }

        if (opens.TryGetValue(trade.ClosedAt, out exit))
        {
            return true;
        }

        return closes.TryGetValue(trade.ClosedAt, out exit);
    }

    private static IReadOnlyList<SignalType> SignalsFor(Arm arm, Dictionary<string, CausalIndicatorCache> caches, IReadOnlyList<ContextualSignalRow> contextual)
    {
        if (arm.Kind == ArmKind.Frozen)
        {
            return [];
        }

        if (arm.Kind == ArmKind.FinalFive)
        {
            return FinalFiveSignals.Build(arm.Id + "|" + arm.Timeframe, caches[arm.Timeframe], caches["1h"]);
        }

        var id = arm.Id switch
        {
            "CPA-SWEEP" => "CPA-SWEEP|BASELINE|5m",
            "CPA-PULLBACK" => "CPA-PULLBACK|BASELINE|5m",
            "CPA-WM" => "CPA-WM|BASELINE|5m",
            "CPA-COMPRESSION" => "CPA-COMPRESSION|CONTINUATION|5m",
            "CPA-MTF" => "CPA-MTF|BASELINE|5m",
            "CPA-FAILED_BREAKOUT" => "CPA-FAILED_BREAKOUT|BASELINE|5m",
            _ => arm.Id
        };
        return contextual.First(x => x.CandidateId == id).Signals;
    }

    private static List<ReplayTrade> Replay(Arm arm, string symbol, IReadOnlyList<MarketCandle> candles, IReadOnlyList<SignalType> signals)
    {
        if (candles.Count < 200)
        {
            return [];
        }

        var created = DateTimeOffset.Parse("2026-09-23T00:00:00Z", CultureInfo.InvariantCulture);
        var template = arm.Kind == ArmKind.Frozen ? arm.Id : StrategyTemplateKeys.EmaRsiTrend;
        var candidate = new ResearchCandidate(
            arm.Id + "|" + arm.Timeframe + "|" + symbol,
            template, 1, "Unmodified existing rule.",
            ResearchKinds.ParentFilter, template, "", [], "existing", "Model B",
            new ResearchFilters(), new ResearchNativeParams(), [arm.Timeframe], ["LONG", "SHORT"], [],
            created, "trade failure diagnosis", ResearchStatuses.Researching);
        var definition = ResearchRunner.DefinitionFor(candidate, arm.Timeframe);
        IStrategyEngine engine = arm.Kind == ArmKind.Frozen
            ? new ResearchStrategyEngine(candidate)
            : new PrecomputedResearchSignalEngine(signals);
        var settings = StrategyValidation.LowIsolatedRisk(candles[0].OpenTime, candles[^1].CloseTime);
        var warmup = arm.Kind == ArmKind.Frozen ? StrategyValidation.WarmupBars(definition) : 0;
        return new BacktestReplay(engine).Run(definition, candles, settings, new CausalIndicatorCache(candles), warmup, candles.Count).Trades.ToList();
    }

    private static async Task<Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> LoadSymbol(string cacheDir, string root, string symbol)
    {
        var map = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var tf in new[] { "5m", "15m", "1h" })
        {
            map[(symbol, tf)] = await LoadOne(cacheDir, root, symbol, tf);
        }

        return map;
    }

    private static async Task<IReadOnlyList<MarketCandle>> LoadOne(string cacheDir, string root, string symbol, string timeframe)
    {
        var coverage = JsonSerializer.Deserialize<CoverageFile>(
            await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Coverage artifact missing.");
        var range = coverage.Coverage.First(x =>
            string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase));
        var bars = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, timeframe);
        return bars.Where(x => x.OpenTime >= range.RequestedFrom && x.CloseTime <= range.RequestedTo && x.IsClosed).OrderBy(x => x.OpenTime).ToList();
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private sealed record Arm(string Id, string Group, string ResearchFamily, string Timeframe, ArmKind Kind);

    private enum ArmKind { Frozen, FinalFive, Contextual }

    private sealed record Diag(
        string Group, string ResearchFamily, string Strategy, string Symbol, string Timeframe,
        string Side, string Phase, int Block, string Vol, string Trend, string Label, FailureFacts Facts);

    private sealed record Phase4Row(string CandidateId, string Phase, string CostLabel, decimal PnL, decimal GrossPnl, decimal FundingPnl);

    private sealed record Phase4Cost(int Trades, decimal GrossExpectancy, decimal NetExpectancy, decimal FundingExpectancy, int CostFlipped);

    private sealed record CoverageFile(IReadOnlyList<CoverageSlice> Coverage);

    private sealed record CoverageSlice(string Symbol, string Timeframe, DateTimeOffset RequestedFrom, DateTimeOffset RequestedTo);
}
