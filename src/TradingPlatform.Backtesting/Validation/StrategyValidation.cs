using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Backtesting.Validation;

public sealed record ValidationSlice(
    string Label,
    DateTimeOffset From,
    DateTimeOffset To,
    int Bars,
    ReplayResult Result);

public sealed record SensitivityRow(string Change, decimal BaselinePf, decimal ChangedPf, bool Flipped);

public sealed record RegimeRow(
    string Regime,
    int Windows,
    decimal MedianPf,
    int Trades,
    int WindowsWithTrades = 0,
    int EmptyWindows = 0,
    int NoLossesWindows = 0,
    int NoWinsWindows = 0,
    decimal PositivePnlSum = 0m,
    decimal AbsoluteNegativePnlSum = 0m);

public sealed record TemplateValidationResult(
    string TemplateKey,
    string DisplayName,
    string Status,
    string Implementation,
    string LongLogic,
    string ShortLogic,
    string LookAhead,
    string Repaint,
    DateTimeOffset? PeriodStart,
    DateTimeOffset? PeriodEnd,
    IReadOnlyList<string> Timeframes,
    IReadOnlyList<string> Symbols,
    ReplaySideMetrics? Long,
    ReplaySideMetrics? Short,
    ReplayResult? Combined,
    IReadOnlyList<ValidationSlice> WalkForward,
    ReplayResult? InSample,
    ReplayResult? Validation,
    ReplayResult? OutOfSample,
    IReadOnlyList<SensitivityRow> Sensitivity,
    IReadOnlyList<RegimeRow> Regimes,
    string FailureModes,
    string NextAction,
    string CostNotes,
    IReadOnlyList<string> Notes);

public static class StrategyValidation
{
    public static readonly string[] Timeframes = StrategyTemplateKeys.SupportedTimeframes;

    public const int PreferredWarmupBars = 120;
    public const int MinimumEvaluatedBars = 10;

    public static ReplaySettings FrozenRisk(DateTimeOffset from, DateTimeOffset to, decimal capital = 10_000m) =>
        new(from, to, capital, 1m, 5m, 0.04m, 0.02m, 2m, 4m, 5m, 4m, 2, 5, 30, 1m);

    /// <summary>
    /// Isolated USD-M LOW book used for research screens. Does not replace FrozenRisk.
    /// $1,000, 0.5% risk, 3x, 2%/4% book SL/TP unless HonorSuggestedStops is set on the settings instance.
    /// </summary>
    public static ReplaySettings LowIsolatedRisk(DateTimeOffset from, DateTimeOffset to, decimal capital = 1_000m) =>
        new(from, to, capital, 0.5m, 3m, 0.04m, 0.02m, 2m, 4m, 3m, 4m, 2, 5, 30, 1m);

    public static int RequiredLookback(StrategyDefinition definition)
    {
        var p = definition.Params;
        var q = definition.Quality;
        var emaSlow = Positive(p?.EmaSlow, 50);
        var emaFast = Positive(p?.EmaFast, 20);
        var rsi = Positive(p?.RsiPeriod, 14);
        var macdSlow = Positive(p?.MacdSlow, 26);
        var macdSignal = Positive(p?.MacdSignal, 9);
        var bb = Positive(p?.BbPeriod, 20);
        var donchian = Positive(p?.DonchianLength, 20);
        var volume = Positive(q?.VolumeLookback, 20);
        const int atr = 14;
        var macdReady = macdSlow + macdSignal - 1;
        var entry = Positive(p?.EntryLookback, 20);
        var volLook = Positive(p?.VolatilityLookback, 100);
        var adx = Positive(p?.AdxPeriod, 14) * 2 + 2;
        var st = Positive(p?.SupertrendPeriod, 10) + 5;
        return Math.Max(
            Math.Max(Math.Max(emaSlow, emaFast), rsi + 1),
            Math.Max(Math.Max(macdReady, bb), Math.Max(donchian + 1, Math.Max(volume, Math.Max(atr + 1, Math.Max(entry + 1, Math.Max(volLook, Math.Max(adx, st))))))));
    }

    public static int WarmupBars(StrategyDefinition definition) =>
        Math.Max(PreferredWarmupBars, RequiredLookback(definition));

    public static (int InSampleEnd, int ValidationEnd) ChronologicalSplitIndices(int count)
    {
        var a = Math.Max(1, count * 60 / 100);
        var b = Math.Max(a + 1, count * 80 / 100);
        return (a, b);
    }

    private static int Positive(int? value, int fallback) => value is > 0 ? value.Value : fallback;

    public static StrategyDefinition Definition(string templateKey, string timeframe, StrategyTemplateParams? tweak = null)
    {
        var parameters = (tweak ?? StrategyTemplates.DefaultsFor(templateKey, true)) with
        {
            TemplateKey = templateKey,
            AllowedSide = StrategySides.Both,
            Timeframe = timeframe
        };
        return new StrategyDefinitionValidator().Parse(StrategyTemplates.Build(templateKey, 1, parameters));
    }

    public static (IReadOnlyList<MarketCandle> InSample, IReadOnlyList<MarketCandle> Validation, IReadOnlyList<MarketCandle> OutOfSample)
        ChronologicalSplit(IReadOnlyList<MarketCandle> candles)
    {
        var ordered = Closed(candles);
        var n = ordered.Count;
        var (a, b) = ChronologicalSplitIndices(n);
        return (
            new SliceList<MarketCandle>(ordered, 0, a),
            new SliceList<MarketCandle>(ordered, a, Math.Max(0, b - a)),
            new SliceList<MarketCandle>(ordered, b, Math.Max(0, n - b)));
    }

    public static IReadOnlyList<(int Start, int Length)> WalkForwardWindows(int count, int train, int test, int step)
    {
        var windows = new List<(int Start, int Length)>();
        if (count < train + test)
        {
            return windows;
        }

        for (var start = 0; start + train + test <= count; start += Math.Max(1, step))
        {
            windows.Add((start, train + test));
        }

        return windows;
    }

    public static string ClassifyRegime(IReadOnlyList<MarketCandle> candles)
    {
        var ordered = Closed(candles);
        if (ordered.Count < 10)
        {
            return "insufficient";
        }

        var first = ordered[0].Close;
        var last = ordered[^1].Close;
        if (first <= 0m)
        {
            return "insufficient";
        }

        var ret = (last - first) / first * 100m;
        var mid = ordered[ordered.Count / 2].Close;
        var firstHalf = (mid - first) / first * 100m;
        var secondHalf = (last - mid) / Math.Max(mid, 0.00000001m) * 100m;
        var returns = new List<decimal>();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i - 1].Close > 0m)
            {
                returns.Add((ordered[i].Close - ordered[i - 1].Close) / ordered[i - 1].Close);
            }
        }

        var vol = 0m;
        if (returns.Count > 1)
        {
            var mean = returns.Average();
            vol = (decimal)Math.Sqrt((double)returns.Average(r => (r - mean) * (r - mean)));
        }

        if (firstHalf > 8m && secondHalf < -8m || firstHalf < -8m && secondHalf > 8m)
        {
            return "fast_reversal";
        }

        if (Math.Abs(ret) >= 40m)
        {
            return "extended_trend";
        }

        if (vol >= 0.02m)
        {
            return ret > 8m ? "high_vol_bull" : ret < -8m ? "high_vol_bear" : "high_volatility";
        }

        if (vol <= 0.004m && Math.Abs(ret) < 8m)
        {
            return "low_volatility";
        }

        if (ret > 15m)
        {
            return "strong_bull";
        }

        if (ret < -15m)
        {
            return "strong_bear";
        }

        return "sideways";
    }

    public static string AssignStatus(
        bool implementationOk,
        int bars,
        ReplayResult? oos,
        ReplayResult? insample,
        ReplayResult? combined,
        IReadOnlyList<SensitivityRow> sensitivity,
        ReplaySideMetrics? longSide,
        ReplaySideMetrics? shortSide)
    {
        if (!implementationOk)
        {
            return StrategyValidationStatuses.ImplementationError;
        }

        if (bars < 80)
        {
            return StrategyValidationStatuses.InsufficientData;
        }

        if (sensitivity.Any(row => row.Flipped))
        {
            return StrategyValidationStatuses.Unstable;
        }

        if (insample is { ProfitFactor: > 1.2m, NumberOfTrades: >= 10 }
            && oos is { ProfitFactor: < 1m, NumberOfTrades: >= 5 }
            && oos.ProfitFactor < insample.ProfitFactor * 0.7m)
        {
            return StrategyValidationStatuses.OosDegradation;
        }

        var longOk = longSide is { Trades: >= 5 };
        var shortOk = shortSide is { Trades: >= 5 };
        if (oos is { NumberOfTrades: >= 20, ProfitFactor: >= 1m }
            && combined is { NumberOfTrades: >= 20, ProfitFactor: >= 1m }
            && longOk
            && shortOk)
        {
            return StrategyValidationStatuses.ValidationPending;
        }

        return StrategyValidationStatuses.ValidationPending;
    }

    public static SensitivityRow Compare(string change, ReplayResult baseline, ReplayResult changed)
    {
        var flipped = baseline.ProfitFactor >= 1m && changed.ProfitFactor < 1m
            || baseline.ProfitFactor < 1m && changed.ProfitFactor >= 1.3m
            || Math.Abs(changed.MaximumDrawdown - baseline.MaximumDrawdown) >= 15m;
        return new SensitivityRow(change, baseline.ProfitFactor, changed.ProfitFactor, flipped);
    }

    public static ReplayResult Run(
        StrategyDefinition definition,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache? cache = null,
        int? evaluateFromInclusive = null,
        int? evaluateToExclusive = null)
    {
        IReadOnlyList<MarketCandle> ordered = cache?.Candles ?? Closed(candles);
        if (ordered.Count == 0)
        {
            var emptyFrom = DateTimeOffset.UtcNow.AddDays(-1);
            return new BacktestReplay(new StrategyEngine()).Run(
                definition,
                ordered,
                FrozenRisk(emptyFrom, emptyFrom.AddDays(1)),
                cache,
                0,
                0);
        }

        var warmup = WarmupBars(definition);
        var fromIdx = Math.Clamp(evaluateFromInclusive ?? 0, 0, ordered.Count);
        var toIdx = Math.Clamp(evaluateToExclusive ?? ordered.Count, fromIdx, ordered.Count);
        var signalFrom = Math.Max(fromIdx, warmup);
        if (signalFrom >= toIdx)
        {
            var emptyFrom = ordered[Math.Min(fromIdx, ordered.Count - 1)].OpenTime;
            var emptyTo = ordered[Math.Min(Math.Max(fromIdx, toIdx - 1), ordered.Count - 1)].CloseTime;
            return new BacktestReplay(new StrategyEngine()).Run(
                definition,
                ordered,
                FrozenRisk(emptyFrom, emptyTo),
                cache ?? new CausalIndicatorCache(ordered),
                signalFrom,
                signalFrom);
        }

        var from = ordered[signalFrom].OpenTime;
        var to = ordered[Math.Max(signalFrom, toIdx - 1)].CloseTime;
        return new BacktestReplay(new StrategyEngine()).Run(
            definition,
            ordered,
            FrozenRisk(from, to),
            cache ?? new CausalIndicatorCache(ordered),
            signalFrom,
            toIdx);
    }

    public static string RenderReport(IReadOnlyList<TemplateValidationResult> rows, string extra = "")
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# Strategy audit report — Model B final");
        sb.AppendLine();
        sb.AppendLine("IndicatorModel = B. MetricsVersion = fixed. Funding = EXCLUDING_FUNDING. LIVE = OFF.");
        sb.AppendLine("Factual historical simulation only. **Not** a profit forecast. Nothing here enables LIVE.");
        sb.AppendLine("Execution: signal on closed bar T, fill at **T+1 open**, Isolated Risk book sizing/SL/TP, fees and slippage included.");
        sb.AppendLine("Funding: labeled per template. Paper/LIVE still fill at last price; this report uses the backtest next-open model.");
        sb.AppendLine("Parameters are frozen defaults. No search for maximum historical profit.");
        sb.AppendLine();
        sb.AppendLine(AggregationMethodology());
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(extra))
        {
            sb.AppendLine(extra.Trim());
            sb.AppendLine();
        }

        foreach (var row in rows)
        {
            sb.AppendLine($"## {row.DisplayName}");
            sb.AppendLine();
            sb.AppendLine($"1. Implementation correctness: {row.Implementation}");
            sb.AppendLine($"2. LONG correctness: {row.LongLogic}");
            sb.AppendLine($"3. SHORT correctness: {row.ShortLogic}");
            sb.AppendLine($"4. Look-ahead status: {row.LookAhead}");
            sb.AppendLine($"5. Repainting status: {row.Repaint}");
            sb.AppendLine($"6. Backtest period: {FormatPeriod(row.PeriodStart, row.PeriodEnd)}");
            sb.AppendLine($"7. Timeframes: {string.Join(", ", row.Timeframes)}");
            sb.AppendLine($"8. Symbols: {string.Join(", ", row.Symbols)}");
            WriteCombined(sb, row);
            sb.AppendLine($"18. Walk-forward results: {FormatWalk(row.WalkForward)}");
            sb.AppendLine($"19. Out-of-sample results: {ValidationMetricsAggregator.FormatSlice(row.OutOfSample)}");
            sb.AppendLine($"    IS: {ValidationMetricsAggregator.FormatSlice(row.InSample)}");
            sb.AppendLine($"    Validation: {ValidationMetricsAggregator.FormatSlice(row.Validation)}");
            sb.AppendLine($"20. Parameter stability: {FormatSensitivity(row.Sensitivity)}");
            sb.AppendLine($"21. Regime performance: {FormatRegimes(row.Regimes)}");
            sb.AppendLine($"22. Main failure modes: {row.FailureModes}");
            sb.AppendLine($"23. Current validation status: `{row.Status}`");
            sb.AppendLine($"24. Recommended next action: {row.NextAction}");
            foreach (var note in row.Notes)
            {
                sb.AppendLine($"- {note}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## Disposition");
        sb.AppendLine();
        sb.AppendLine("All five templates remain in the catalog. None were deleted. None are auto-started. LIVE is not enabled by this report.");
        sb.AppendLine("`VALIDATED_FOR_PAPER` means implementation tests passed and the frozen-default OOS/walk-forward slice was not broken under the documented cost model. It is not a guarantee.");
        return sb.ToString();
    }

    private static string AggregationMethodology() =>
        """
        Aggregation (metrics-fixed):
        - Unit of observation for PF, net, fees, expectancy: the closed trade (`ReplayTrade.PnL`, cost-inclusive).
        - PF = sum(gross positive trade PnL) / abs(sum(gross negative trade PnL)). Same formula for LONG, SHORT, Combined, IS, Validation, and OOS. Not an average of per-book PFs.
        - Empty books/windows: PF = N/A. Trades but no losses: PF = Infinity. Trades but no wins: PF = 0. Never PF = 99.
        - Return A: equal-book normalized return = sum(book net) / (included books × initial balance). Empty books are included with 0 return.
        - Return B: per-book mean/median/positive-book %. These are not a single 10,000 USDT portfolio.
        - Drawdown: per-book mean/median/p90/p95/max. Equal-weight normalized portfolio DD is N/A unless aligned equity curves exist (they are stripped after merge). Max-of-max is not labeled as portfolio DD.
        - Sharpe / Sortino / Calmar at universe level: N/A (no common time series after merge). Per-book Sharpe is not copied into the aggregate.
        - Costs: fees and slippage are inside `ReplayTrade.PnL` once. Gross after slippage before fees = Net + Fees. Slippage vs mid cannot be reconstructed from stored trades.
        """;

    private static void WriteCombined(System.Text.StringBuilder sb, TemplateValidationResult row)
    {
        var combined = row.Combined;
        var longSide = row.Long;
        var shortSide = row.Short;
        var totals = combined is null ? PnlTotals.Empty : ValidationMetricsAggregator.ResolveTotals(combined);
        var longPf = ValidationMetricsAggregator.ProfitFactorOf(longSide);
        var shortPf = ValidationMetricsAggregator.ProfitFactorOf(shortSide);
        var combinedPf = combined is null ? ProfitFactorValue.NoTrades : ValidationMetricsAggregator.ProfitFactorOf(combined);
        var returns = combined?.BookReturns is { Count: > 0 } bookReturns
            ? EqualBookAggregation.Returns(bookReturns)
            : null;
        var drawdowns = combined?.BookDrawdowns is { Count: > 0 } dds
            ? EqualBookAggregation.Drawdowns(dds)
            : null;
        var bookCount = returns?.Books ?? 0;
        sb.AppendLine("LONG (trade-weighted sums across books, not an average of per-book PFs):");
        sb.AppendLine($"- Trades: {longSide?.Trades ?? 0} (W {longSide?.WinningTrades ?? 0} / L {longSide?.LosingTrades ?? 0} / Z {longSide?.ZeroPnlTrades ?? 0})");
        sb.AppendLine($"- +W {Num(longSide?.PositivePnlSum)} / |L| {Num(longSide?.AbsoluteNegativePnlSum)}");
        sb.AppendLine($"- Win rate: {Pct(longSide?.WinRate)}");
        sb.AppendLine($"- Profit factor: {longPf.Render()}");
        sb.AppendLine($"- Expectancy: {Num(longSide?.Expectancy)}");
        sb.AppendLine($"- Fees: {Num(longSide?.Fees)}");
        sb.AppendLine($"- Max per-book side DD: {Pct(longSide?.MaximumDrawdown)} (not a portfolio drawdown)");
        sb.AppendLine("SHORT (trade-weighted sums across books, not an average of per-book PFs):");
        sb.AppendLine($"- Trades: {shortSide?.Trades ?? 0} (W {shortSide?.WinningTrades ?? 0} / L {shortSide?.LosingTrades ?? 0} / Z {shortSide?.ZeroPnlTrades ?? 0})");
        sb.AppendLine($"- +W {Num(shortSide?.PositivePnlSum)} / |L| {Num(shortSide?.AbsoluteNegativePnlSum)}");
        sb.AppendLine($"- Win rate: {Pct(shortSide?.WinRate)}");
        sb.AppendLine($"- Profit factor: {shortPf.Render()}");
        sb.AppendLine($"- Expectancy: {Num(shortSide?.Expectancy)}");
        sb.AppendLine($"- Fees: {Num(shortSide?.Fees)}");
        sb.AppendLine($"- Max per-book side DD: {Pct(shortSide?.MaximumDrawdown)} (not a portfolio drawdown)");
        sb.AppendLine("Combined:");
        sb.AppendLine($"- Exact totals: +W {Num(totals.PositivePnlSum)} / |L| {Num(totals.AbsoluteNegativePnlSum)} (W {totals.WinningTrades} / L {totals.LosingTrades} / Z {totals.ZeroPnlTrades})");
        sb.AppendLine($"9. Gross after slippage before fees: {Num(totals.GrossAfterSlippageBeforeFees)} USDT. Slippage vs mid: not reconstructed (embedded in fill prices; not estimated).");
        sb.AppendLine($"10. Net performance: {Num(combined?.NetProfit)} USDT (sum of independent book nets). Equal-book return A: {(returns is null ? "unavailable (per-book returns not stored on this artifact)" : Pct(returns.EqualBookReturn * 100m))} over {bookCount} books × initial. Per-book B: mean {(returns is null ? "unavailable" : Pct(returns.MeanReturn * 100m))}, median {(returns is null ? "unavailable" : Pct(returns.MedianReturn * 100m))}, positive-book {(returns is null ? "unavailable" : Pct(returns.PositiveBookPercent))}. Not a single-account portfolio return.");
        sb.AppendLine($"11. Fees: {Num(totals.Fees)} USDT (once; already inside net)");
        sb.AppendLine($"12. Funding: {row.CostNotes}");
        sb.AppendLine("13. Slippage: included once in entry/exit prices (0.02% default). Not subtracted again.");
        sb.AppendLine($"14. Per-book drawdown: mean {(drawdowns is null ? "unavailable" : Pct(drawdowns.Mean))}, median {(drawdowns is null ? "unavailable" : Pct(drawdowns.Median))}, p90 {(drawdowns is null ? "unavailable" : Pct(drawdowns.P90))}, p95 {(drawdowns is null ? "unavailable" : Pct(drawdowns.P95))}, max {(drawdowns is null ? "unavailable" : Pct(drawdowns.Maximum))}. Equal-weight normalized portfolio DD: N/A (equity curves stripped; no stated portfolio weights beyond equal-book). Do not read max-of-max as portfolio DD.");
        sb.AppendLine($"15. Profit factor: {combinedPf.Render()} (same W/|L| definition as LONG/SHORT)");
        sb.AppendLine($"16. Expectancy: {(totals.Trades > 0 ? Num(totals.Expectancy) : "n/a")} (net / trades)");
        sb.AppendLine($"17. Trade count: {totals.Trades}; Sharpe/Sortino/Calmar (universe): N/A");
    }

    private static string FormatPeriod(DateTimeOffset? from, DateTimeOffset? to) =>
        from is null || to is null ? "insufficient data" : $"{from:yyyy-MM-dd} → {to:yyyy-MM-dd}";

    private static string FormatWalk(IReadOnlyList<ValidationSlice> windows) =>
        ValidationMetricsAggregator.FormatWalk(windows);

    private static string FormatSensitivity(IReadOnlyList<SensitivityRow> rows) =>
        rows.Count == 0
            ? "not computed"
            : string.Join("; ", rows.Select(r => $"{r.Change}: PF {r.BaselinePf:0.00}→{r.ChangedPf:0.00}{(r.Flipped ? " FLIP" : "")}"));

    private static string FormatRegimes(IReadOnlyList<RegimeRow> rows) =>
        rows.Count == 0
            ? "not computed"
            : string.Join("; ", rows.Select(r =>
            {
                var pf = ProfitFactorValue.From(r.PositivePnlSum, r.AbsoluteNegativePnlSum, r.Trades);
                var median = r.MedianPf > 0m
                    ? r.MedianPf.ToString("0.00")
                    : "N/A";
                return $"{r.Regime}: {r.Windows} windows ({r.WindowsWithTrades} with trades, {r.EmptyWindows} empty), {r.Trades} trades, trade PF {pf.Render()}, median finite window PF {median}, no-loss {r.NoLossesWindows}, no-win {r.NoWinsWindows}";
            }));

    private static string Pct(decimal? value) => value is null ? "n/a" : $"{value.Value:0.00}%";

    private static string Num(decimal? value) => value is null ? "n/a" : $"{value.Value:0.00}";

    private static List<MarketCandle> Closed(IReadOnlyList<MarketCandle> candles) =>
        candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
}
