using System.Globalization;
using System.Text;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

internal static class PriceActionTriangleOctoberHoldout
{
    private const string CandidateId = "PA-PA_SYMMETRICAL_TRIANGLE|STF|1h";
    private static readonly DateTimeOffset Start = new(2026, 7, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);

    public static async Task<int> RunAsync(string root, string cacheDir, CancellationToken cancellationToken)
    {
        var symbols = Phase7PriceActionUniverse.Symbols;
        var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<TradingPlatform.Domain.Market.MarketCandle>>();
        var caches = new Dictionary<(string Symbol, string Timeframe), CausalIndicatorCache>();
        var precomputed = new Dictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>>();
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformPriceActionHoldout/1.0");
        var timeframe = "1h";
        var candidates = PriceActionAlphaDiscovery.BuildCandidates();
        var candidate = candidates.Single(row => row.CandidateId == CandidateId);
        Console.WriteLine($"Frozen candidate {CandidateId}; 15 symbols; 1h; forward OOS expected from 2026-10-01.");

        for (var i = 0; i < symbols.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = symbols[i];
            var (candles, _, downloaded) = await ResearchKlineCache.LoadAsync(
                http, cacheDir, symbol, timeframe, Start, End, cancellationToken, strictCoverage: true);
            var closed = candles.Where(row => row.IsClosed).OrderBy(row => row.OpenTime).ToList();
            var cache = new CausalIndicatorCache(closed);
            _ = cache.PriceAction();
            _ = cache.Ema(20);
            _ = cache.Ema(50);
            series[(symbol, timeframe)] = closed;
            caches[(symbol, timeframe)] = cache;
            var engine = new ResearchStrategyEngine(candidate);
            var definition = ResearchRunner.DefinitionFor(candidate, timeframe);
            var signals = new SignalType[closed.Count];
            for (var bar = 0; bar < closed.Count; bar++)
            {
                signals[bar] = engine.EvaluateAt(
                    definition,
                    new StrategyContext { ClosedCandles = closed, CurrentPrice = closed[bar].Close, HasOpenPosition = false },
                    cache,
                    bar,
                    out _);
            }

            precomputed[(CandidateId, symbol, timeframe)] = signals;
            Console.WriteLine($"{symbol} {i + 1}/{symbols.Length}: bars={closed.Count}, downloaded={downloaded}.");
        }

        var rows = ResearchRunner.Evaluate(new ResearchRunRequest
        {
            Phase = ResearchPhases.Oos,
            Candidates = [candidate],
            Symbols = symbols,
            Timeframes = [timeframe],
            Series = series,
            CostLabels = [ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress],
            MaxParallel = 2,
            UseLowIsolated = true,
            SkipWalkForward = true,
            IndicatorCaches = caches,
            PrecomputedSignals = precomputed,
            DataSnapshot = new ResearchDataSnapshot([ResearchDatasets.Ohlcv])
        }).OrderBy(row => row.CostLabel, StringComparer.Ordinal).ThenBy(row => row.Symbol, StringComparer.Ordinal).ToList();

        var allBars = series[(symbols[0], timeframe)];
        var (isEnd, validationEnd) = StrategyValidation.ChronologicalSplitIndices(allBars.Count);
        var report = Render(rows, symbols, allBars[validationEnd].OpenTime, allBars[^1].CloseTime, allBars.Count, isEnd, validationEnd);
        var path = Path.Combine(root, "artifacts", "research", "audit", "price-action-symmetrical-triangle-october-forward-2026-10.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report, cancellationToken);
        Console.WriteLine($"Wrote {path}");
        return 0;
    }

    private static string Render(
        IReadOnlyList<ResearchBookResult> rows,
        IReadOnlyList<string> symbols,
        DateTimeOffset actualStart,
        DateTimeOffset actualEnd,
        int bars,
        int isEnd,
        int validationEnd)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# PA Symmetrical Triangle 1h: October forward holdout");
        sb.AppendLine();
        sb.AppendLine($"Frozen candidate: `{CandidateId}` from the Phase 7 manifest, unmodified. Universe: {symbols.Count} fixed symbols. Risk and entry/exit rules use the existing LOW Isolated research runner.");
        sb.AppendLine($"Input window: {Start:yyyy-MM-dd HH:mm} to {End:yyyy-MM-dd HH:mm} UTC, {bars:N0} bars per reference symbol. Verified chronological OOS: `{actualStart:yyyy-MM-dd HH:mm}` to `{actualEnd:yyyy-MM-dd HH:mm}` UTC (IS={isEnd}, validation end={validationEnd}).");
        sb.AppendLine();
        sb.AppendLine("| Cost | Trades | Symbols | Winners | Net USDT | PF | Expectancy USDT | Max symbol DD % |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress })
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
        sb.AppendLine("## Base-cost symbol results");
        sb.AppendLine();
        sb.AppendLine("| Symbol | Trades | Wins | Net USDT | PF | Max DD % | Status | Notes |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---|");
        foreach (var row in rows.Where(row => row.CostLabel == ResearchCostLabels.Base).OrderBy(row => row.Symbol, StringComparer.Ordinal))
        {
            var pf = row.ProfitFactorState == "NO_LOSSES" ? "∞" : row.ProfitFactor?.ToString("0.000", CultureInfo.InvariantCulture) ?? "n/a";
            sb.AppendLine($"| {row.Symbol} | {row.TradeCount} | {row.WinningTrades} | {row.NetPnl:0.00} | {pf} | {row.MaximumDrawdown ?? 0m:0.00} | {row.Status} | {string.Join("; ", row.Notes)} |");
        }

        sb.AppendLine();
        sb.AppendLine("This is a fresh October time holdout of a previously identified Phase 7 OOS lead. It is an independent per-symbol isolated replay, not shared-capital or live-execution validation. No promotion is made from the holdout alone.");
        return sb.ToString();
    }
}
