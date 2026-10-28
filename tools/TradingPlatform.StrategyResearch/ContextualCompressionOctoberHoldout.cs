using System.Globalization;
using System.Text;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

internal static class ContextualCompressionOctoberHoldout
{
    private const string CompressionCandidateId = "CPA-COMPRESSION|CONTINUATION|5m";
    private const string SweepCandidateId = "CPA-SWEEP|STRICT|5m";
    private static readonly DateTimeOffset Start = new(2026, 7, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

    public static async Task<int> RunAsync(string root, string cacheDir, CancellationToken cancellationToken)
    {
        return await RunCandidateAsync(root, cacheDir, CompressionCandidateId, ["5m"], "contextual-compression-october-forward-2026-10.md", cancellationToken);
    }

    public static async Task<int> RunSweepAsync(string root, string cacheDir, CancellationToken cancellationToken)
    {
        return await RunCandidateAsync(root, cacheDir, SweepCandidateId, ["1m", "5m", "15m", "1h"], "contextual-sweep-strict-october-forward-2026-10.md", cancellationToken);
    }

    private static async Task<int> RunCandidateAsync(
        string root,
        string cacheDir,
        string candidateId,
        IReadOnlyList<string> timeframes,
        string reportName,
        CancellationToken cancellationToken)
    {
        var output = Path.Combine(root, "artifacts", "research", "audit", reportName);
        var symbols = Phase7PriceActionUniverse.Symbols;
        var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<TradingPlatform.Domain.Market.MarketCandle>>();
        var precomputed = new Dictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>>();
        var caches = new Dictionary<(string Symbol, string Timeframe), CausalIndicatorCache>();
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformContextualHoldout/1.0");

        Console.WriteLine($"Frozen candidate {candidateId}; timeframes={string.Join(',', timeframes)}; data {Start:yyyy-MM-dd} to {End:yyyy-MM-dd} UTC; OOS expected from 2026-10-01.");
        for (var i = 0; i < symbols.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = symbols[i];
            foreach (var timeframe in timeframes)
            {
                var (candles, _, downloaded) = await ResearchKlineCache.LoadAsync(
                    http, cacheDir, symbol, timeframe, Start, End, cancellationToken, strictCoverage: true);
                var closed = candles.Where(row => row.IsClosed).OrderBy(row => row.OpenTime).ToList();
                series[(symbol, timeframe)] = closed;
                var cache = new CausalIndicatorCache(closed);
                if (closed.Count > 0)
                {
                    _ = cache.PriceAction();
                    _ = cache.Atr(ContextualPriceActionCatalog.AtrPeriod);
                    _ = cache.RelativeVolume(ContextualPriceActionCatalog.RelativeVolumeLookback);
                }

                caches[(symbol, timeframe)] = cache;
                Console.WriteLine($"{symbol} {timeframe} downloaded={downloaded}, closed={closed.Count}.");
            }

            var cacheMap = caches.Where(pair => pair.Key.Symbol == symbol)
                .ToDictionary(pair => pair.Key.Timeframe, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var signals = ContextualPriceActionSignals.BuildAll(cacheMap).Single(row => row.CandidateId == candidateId);
            var entry = series[(symbol, ContextualPriceActionCatalog.EntryTimeframe)];
            if (signals.SeriesMissing || signals.Signals.Length != entry.Count)
            {
                throw new InvalidOperationException($"Frozen signal series is unavailable for {symbol}.");
            }

            precomputed[(candidateId, symbol, ContextualPriceActionCatalog.EntryTimeframe)] = signals.Signals;
            Console.WriteLine($"{symbol} {i + 1}/{symbols.Length}: {entry.Count} entry bars.");
        }

        var hypothesis = ContextualPriceActionCatalog.ById(candidateId);
        var candidate = new ResearchCandidate(
            candidateId,
            StrategyTemplateKeys.PaStructureBreak,
            1,
            $"{hypothesis.Family} {hypothesis.Variant}. {hypothesis.EventRule} {hypothesis.ConfirmationRule} {hypothesis.ContextRule}",
            ResearchKinds.Native,
            StrategyTemplateKeys.PaStructureBreak,
            "contextual-pa",
            ["closed-OHLCV", "causal-structure", "causal-pattern-events"],
            $"Frozen contextual-pa-v1 {candidateId} rule.",
            "Model B Isolated LOW SL/TP. Completed-candle signal, next-bar open fill. No OOS stop tuning.",
            new ResearchFilters(ConfirmationTimeframe: hypothesis.ConfirmationTimeframe, ContextTimeframe: hypothesis.ContextTimeframe ?? hypothesis.StructureTimeframe),
            new ResearchNativeParams(),
            [ContextualPriceActionCatalog.EntryTimeframe],
            StrategyTemplateKeys.SupportedDirections,
            [],
            DateTimeOffset.Parse("2026-09-23T00:00:00Z", CultureInfo.InvariantCulture),
            "Frozen Sep 23 2026; forward holdout Oct 1-21 2026; fixed 15-symbol Phase 7 universe.",
            ResearchStatuses.Researching,
            ContextualPriceActionCatalog.Version);

        var request = new ResearchRunRequest
        {
            Phase = ResearchPhases.Oos,
            Candidates = [candidate],
            Symbols = symbols,
            Timeframes = [ContextualPriceActionCatalog.EntryTimeframe],
            Series = series,
            CostLabels = ContextualPriceActionCatalog.CostLabels,
            MaxParallel = 2,
            UseLowIsolated = true,
            SkipWalkForward = true,
            IndicatorCaches = caches,
            PrecomputedSignals = precomputed,
            DataSnapshot = new ResearchDataSnapshot([ResearchDatasets.Ohlcv])
        };
        var rows = ResearchRunner.Evaluate(request).OrderBy(row => row.CostLabel, StringComparer.Ordinal).ThenBy(row => row.Symbol, StringComparer.Ordinal).ToList();
        var (inSampleEnd, validationEnd) = StrategyValidation.ChronologicalSplitIndices(series[(symbols[0], ContextualPriceActionCatalog.EntryTimeframe)].Count);
        var allBars = series[(symbols[0], ContextualPriceActionCatalog.EntryTimeframe)];
        var actualStart = allBars[validationEnd].OpenTime;
        var actualEnd = allBars[^1].CloseTime;
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, Render(candidateId, timeframes, rows, symbols, actualStart, actualEnd, allBars.Count, inSampleEnd, validationEnd), cancellationToken);
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    private static string Render(
        string candidateId,
        IReadOnlyList<string> timeframes,
        IReadOnlyList<ResearchBookResult> rows,
        IReadOnlyList<string> symbols,
        DateTimeOffset actualStart,
        DateTimeOffset actualEnd,
        int bars,
        int insEnd,
        int valEnd)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"# {candidateId}: October forward holdout");
        sb.AppendLine();
        sb.AppendLine($"Candidate: `{candidateId}` (frozen rule from contextual-pa-v1; no parameter changes). Universe: {symbols.Count} fixed Phase 7 symbols. Required timeframes: {string.Join(", ", timeframes)}. Fees, slippage, and risk book follow the original LOW Isolated research runner.");
        sb.AppendLine($"Input window: {Start:yyyy-MM-dd HH:mm} to {End:yyyy-MM-dd HH:mm} UTC, {bars:N0} bars per reference symbol. ResearchRunner's chronological 60/20/20 split places this holdout at `{actualStart:yyyy-MM-dd HH:mm}` to `{actualEnd:yyyy-MM-dd HH:mm}` UTC (split indices IS={insEnd}, validation={valEnd}); this is checked from the loaded series, not assumed from calendar labels.");
        sb.AppendLine();
        sb.AppendLine("## Aggregate per-symbol isolated books");
        sb.AppendLine();
        sb.AppendLine("| Cost | Trades | Symbols | Winners | Net USDT | PF | Expectancy USDT | Max single-symbol DD % |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var cost in ContextualPriceActionCatalog.CostLabels)
        {
            var group = rows.Where(row => row.CostLabel == cost && row.TradeCount > 0).ToList();
            var trades = group.Sum(row => row.TradeCount);
            var wins = group.Sum(row => row.WinningTrades);
            var positive = group.Sum(row => row.PositivePnlSum);
            var negative = group.Sum(row => row.AbsoluteNegativePnlSum);
            var net = group.Sum(row => row.NetPnl);
            var pf = negative == 0m ? (positive > 0m ? "∞" : "n/a") : (positive / negative).ToString("0.000", CultureInfo.InvariantCulture);
            var dd = group.Count == 0 ? 0m : group.Max(row => row.MaximumDrawdown ?? 0m);
            sb.AppendLine($"| {cost} | {trades} | {group.Select(row => row.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count()} | {wins} | {net:0.00} | {pf} | {(trades == 0 ? 0m : net / trades):0.000} | {dd:0.00} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Base-cost symbol detail");
        sb.AppendLine();
        sb.AppendLine("| Symbol | Trades | Wins | Net USDT | PF | Max DD % | Status | Notes |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---|");
        foreach (var row in rows.Where(row => row.CostLabel == ResearchCostLabels.Base).OrderBy(row => row.Symbol, StringComparer.Ordinal))
        {
            var pf = row.ProfitFactorState == "NO_LOSSES" ? "∞" : row.ProfitFactor?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a";
            sb.AppendLine($"| {row.Symbol} | {row.TradeCount} | {row.WinningTrades} | {row.NetPnl:0.00} | {pf} | {row.MaximumDrawdown ?? 0m:0.00} | {row.Status} | {string.Join("; ", row.Notes)} |");
        }

        sb.AppendLine();
        sb.AppendLine("No candidate promotion is made from this forward holdout. These are independent per-symbol isolated books; this report does not model shared capital, cross-symbol slot contention, delisted contracts, or live order execution.");
        return sb.ToString();
    }
}
