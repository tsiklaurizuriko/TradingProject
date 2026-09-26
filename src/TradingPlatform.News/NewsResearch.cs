using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.News;

public static class NewsResearch
{
    public static async Task<int> RunAsync(string repoRoot, string? symbol, string timeframe, string stage, CancellationToken cancellationToken)
    {
        var options = new NewsOptions { Enabled = true, Mode = "Historical" };
        var catalog = NewsAssetCatalog.LoadUniverse(repoRoot);
        var store = new FileNewsStore(Path.Combine(repoRoot, options.StorePath));
        var raw = await store.LoadRawAsync(cancellationToken);
        var events = await store.LoadEventsAsync(cancellationToken);
        if (events.Count == 0 && raw.Count > 0)
        {
            events = new NewsPipeline(options, catalog: catalog).Build(raw);
        }

        var universe = string.IsNullOrWhiteSpace(symbol)
            ? catalog.Identities
            : catalog.Identities.Where(item => string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase)).ToList();
        var stageA = StageSymbols(repoRoot, timeframe, universe, stage);
        var reportSymbol = stageA.FirstOrDefault()?.Symbol ?? symbol ?? "(universe)";
        var cachePath = Path.Combine(repoRoot, "artifacts", "strategy-validation-cache", reportSymbol + "_" + timeframe + ".json");
        var candles = await ReadCandlesAsync(cachePath, cancellationToken);
        var report = new StringBuilder();
        report.AppendLine("# News strategy research");
        report.AppendLine();
        report.AppendLine("Historical result, robustness, out-of-sample result, and validated result are separate. A profitable backtest is not a validated strategy. This run does not assign `VALIDATED_FOR_PAPER`.");
        report.AppendLine();
        AppendSources(report);
        AppendUniverse(report, catalog, stage, stageA);
        AppendStatistics(report, raw, events);
        report.AppendLine("## Strategy Results");
        report.AppendLine();
        report.AppendLine("Sortino and Calmar are N/A. The existing metrics stack does not calculate them.");
        report.AppendLine();
        report.AppendLine("| Strategy | Timeframe | Asset | Trades | Win Rate | PF | Expectancy | Sharpe | Max DD | OOS PF | OOS Expectancy | Status |");
        report.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");

        var subjects = stageA.Count == 0
            ? new List<NewsAssetIdentity> { new(reportSymbol, NewsAssetCatalog.BaseFromSymbol(reportSymbol), "USDT", null) }
            : stageA;
        var rows = new List<(string Name, string Status)>();
        if (events.Count == 0 || candles.Count < 160)
        {
            foreach (var asset in subjects)
            {
                foreach (var hypothesis in NewsHypotheses.All)
                {
                    var status = ResearchStatuses.DataUnavailable;
                    rows.Add((asset.Symbol + " " + hypothesis, status));
                    report.AppendLine(Row(hypothesis, timeframe, asset.Symbol, null, null, status));
                }
            }

            report.AppendLine();
            report.AppendLine(events.Count == 0
                ? "No local historical news dataset is stored. Live API responses were not used to invent a past archive."
                : "Closed candles for this coin and timeframe were not in the local research cache. No download was started.");
        }
        else
        {
            var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
            var buyHold = BuyAndHold(candles, valEnd);
            report.AppendLine();
            report.AppendLine("Buy and hold on the out-of-sample window, price return only: " + buyHold.ToString("P2", CultureInfo.InvariantCulture) + ". This is not a profit factor.");
            foreach (var hypothesis in NewsHypotheses.All)
            {
                var outcome = Evaluate(hypothesis, reportSymbol, timeframe, candles, events, options, NewsAblation.Full, insEnd, valEnd);
                rows.Add((hypothesis, outcome.Status));
                report.AppendLine(Row(hypothesis, timeframe, reportSymbol, outcome.Full, outcome.Oos, outcome.Status));
            }

            report.AppendLine();
            report.AppendLine("## Ablation");
            report.AppendLine();
            report.AppendLine("Published momentum rule with one input removed. Parameters were not searched.");
            report.AppendLine();
            report.AppendLine("| Variant | Trades | PF | OOS PF | Status |");
            report.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var ablation in Ablations())
            {
                var outcome = Evaluate(NewsHypotheses.Momentum, reportSymbol, timeframe, candles, events, options, ablation, insEnd, valEnd);
                var pf = FormatPf(outcome.Full);
                var oos = FormatPf(outcome.Oos);
                report.AppendLine($"| {ablation.Label} | {outcome.Full?.Trades.Count ?? 0} | {pf} | {oos} | {outcome.Status} |");
            }
        }

        report.AppendLine();
        report.AppendLine("## Cross-asset research");
        report.AppendLine();
        report.AppendLine("DATASET_SCOPE = " + DatasetScope(events) + ".");
        report.AppendLine("Q1 affected-asset prediction, Q2 spillover, Q3 BTC-to-alt, Q4 ETH-versus-BTC, Q5 macro differences, Q6 liquidity, and Q7 volatility regime were not measured on this run.");
        report.AppendLine("Leader/follower forward returns at 5m, 15m, 30m, 1h, and 4h were not measured. Status: DATA_UNAVAILABLE. No returns were fabricated, and none of these questions were turned into orders.");
        report.AppendLine("The universe file has no large-cap, mid-cap, small-cap, meme, DeFi, or L1 labels. Those aggregates were not invented. All-assets status follows the rows above.");
        report.AppendLine();
        report.AppendLine("## Best Candidate Rules");
        report.AppendLine();
        report.AppendLine("No rule is called profitable. No rule is validated for paper.");
        foreach (var row in rows)
        {
            report.AppendLine("- `" + row.Name + "`: " + row.Status + ". Historical, robust, out-of-sample, and validated are not the same claim.");
        }

        report.AppendLine();
        report.AppendLine("Open interest is DATA_UNAVAILABLE unless an aligned series is already present. It was not fabricated. Liquidation history remains DATA_UNAVAILABLE. Taker imbalance is the existing order-flow field. CVD was not added.");
        var path = Path.Combine(repoRoot, "NEWS_STRATEGY_RESEARCH.md");
        await File.WriteAllTextAsync(path, report.ToString(), cancellationToken);
        return 0;
    }

    private static NewsOutcome Evaluate(
        string hypothesis,
        string symbol,
        string timeframe,
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<NewsEvent> events,
        NewsOptions options,
        NewsAblation ablation,
        int insEnd,
        int valEnd)
    {
        if (hypothesis == NewsHypotheses.Liquidation
            || hypothesis is NewsHypotheses.OiContinuation or NewsHypotheses.OiShortCover or NewsHypotheses.OiLongLiquidation)
        {
            return new NewsOutcome(null, null, ResearchStatuses.DataUnavailable);
        }

        if (hypothesis == NewsHypotheses.Taker && candles.All(candle => candle.TakerBuyVolume <= 0))
        {
            return new NewsOutcome(null, null, ResearchStatuses.DataUnavailable);
        }

        var engine = new NewsStrategyEngine(hypothesis, symbol, events, options, ablation);
        var replay = new BacktestReplay(engine);
        var definition = new StrategyDefinition
        {
            Name = hypothesis,
            Template = hypothesis,
            Symbol = symbol,
            Timeframe = timeframe
        };
        var settings = StrategyValidation.FrozenRisk(candles[0].OpenTime, candles[^1].CloseTime) with { MaxHoldBars = 24 };
        var cache = new CausalIndicatorCache(candles);
        var full = replay.Run(definition, candles, settings, cache, 120, candles.Count);
        var oos = replay.Run(definition, candles, settings, cache, valEnd, candles.Count);
        var validation = replay.Run(definition, candles, settings, cache, insEnd, valEnd);
        return new NewsOutcome(full, oos, Assign(full, validation, oos));
    }

    private static string Assign(ReplayResult full, ReplayResult validation, ReplayResult oos)
    {
        if (full.Trades.Count == 0)
        {
            return ResearchStatuses.NoTrades;
        }

        var validationPf = validation.Totals?.ProfitFactor ?? ProfitFactorValue.NoTrades;
        if (validation.Trades.Count > 0 && validationPf.Kind == ProfitFactorKind.Finite && validationPf.Ratio < 1m)
        {
            return ResearchStatuses.ValidationFailed;
        }

        var oosPf = oos.Totals?.ProfitFactor ?? ProfitFactorValue.NoTrades;
        if (oos.Trades.Count > 0 && oosPf.Kind == ProfitFactorKind.Finite && oosPf.Ratio < 1m)
        {
            return ResearchStatuses.OosFailed;
        }

        return ResearchStatuses.Researching;
    }

    private static IEnumerable<NewsAblation> Ablations()
    {
        yield return NewsAblation.Full with { Label = "news+volume+vwap+taker" };
        yield return NewsAblation.Full with { UseNews = false, Label = "remove news" };
        yield return NewsAblation.Full with { UseVolume = false, UseRelativeVolume = false, Label = "remove volume" };
        yield return NewsAblation.Full with { UseVwap = false, Label = "remove vwap" };
        yield return NewsAblation.Full with { UseTaker = false, Label = "remove taker" };
        yield return new NewsAblation(UseVolume: false, UseRelativeVolume: false, UseVwap: false, UseTaker: false, UseOpenInterest: false, UseStructure: false) { Label = "news only" };
    }

    private static decimal BuyAndHold(IReadOnlyList<MarketCandle> candles, int oosStart)
    {
        if (oosStart >= candles.Count - 1 || candles[oosStart].Open == 0)
        {
            return 0;
        }

        return (candles[^1].Close - candles[oosStart].Open) / candles[oosStart].Open;
    }

    private static string Row(string hypothesis, string timeframe, string symbol, ReplayResult? full, ReplayResult? oos, string status)
    {
        var totals = full?.Totals;
        var oosTotals = oos?.Totals;
        return "| " + string.Join(" | ",
        [
            hypothesis,
            timeframe,
            symbol,
            (full?.Trades.Count ?? 0).ToString(CultureInfo.InvariantCulture),
            totals is null ? "N/A" : totals.WinRate.ToString("0.00", CultureInfo.InvariantCulture),
            FormatPf(full),
            totals is null ? "N/A" : totals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture),
            full?.SharpeRatio?.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A",
            full?.MaximumDrawdown.ToString("0.00", CultureInfo.InvariantCulture) ?? "N/A",
            FormatPf(oos),
            oosTotals is null ? "N/A" : oosTotals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture),
            status
        ]) + " |";
    }

    private static string FormatPf(ReplayResult? result)
    {
        var pf = result?.Totals?.ProfitFactor ?? ProfitFactorValue.NoTrades;
        return pf.Kind == ProfitFactorKind.Finite
            ? pf.Ratio.ToString("0.00", CultureInfo.InvariantCulture)
            : pf.Kind.ToString();
    }

    private static void AppendUniverse(StringBuilder report, NewsAssetCatalog catalog, string stage, IReadOnlyList<NewsAssetIdentity> stageSymbols)
    {
        report.AppendLine("## Universe");
        report.AppendLine();
        report.AppendLine("- Active USD-M USDT perpetuals discovered from the existing universe cache: " + catalog.Identities.Count.ToString(CultureInfo.InvariantCulture));
        report.AppendLine("- Research stage: " + stage);
        var shown = stageSymbols.Take(12).Select(item => item.Symbol).ToList();
        var suffix = stageSymbols.Count > shown.Count
            ? " +" + (stageSymbols.Count - shown.Count).ToString(CultureInfo.InvariantCulture) + " more"
            : string.Empty;
        report.AppendLine("- Symbols considered this pass: " + (shown.Count == 0 ? "none with a local candle cache" : string.Join(", ", shown) + suffix));
        report.AppendLine("- Stage A sample is the first 20 discovered contracts that already have a local candle file, in universe-cache order. That order is not 24h quote-volume rank. The scanner rank is not stored in the universe file, so large-cap, mid-cap, meme, DeFi, and L1 groups were not invented.");
        report.AppendLine("- News is collected once and mapped onto affected assets. The runner does not call a news API per symbol.");
        report.AppendLine();
    }

    private static List<NewsAssetIdentity> StageSymbols(string repoRoot, string timeframe, IReadOnlyList<NewsAssetIdentity> universe, string stage)
    {
        var cached = universe.Where(item => File.Exists(Path.Combine(repoRoot, "artifacts", "strategy-validation-cache", item.Symbol + "_" + timeframe + ".json"))).ToList();
        if (string.Equals(stage, "B", StringComparison.OrdinalIgnoreCase))
        {
            return universe.ToList();
        }

        return cached.Take(20).ToList();
    }

    private static string DatasetScope(IReadOnlyList<NewsEvent> events)
    {
        var assets = events.SelectMany(item => item.Assets).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (events.Count == 0)
        {
            return "EMPTY";
        }

        if (assets.Count == 1
            && string.Equals(assets[0], "BTC", StringComparison.OrdinalIgnoreCase)
            && events.All(item => item.MarketScope != MarketScope.Global))
        {
            return "BTC_ONLY";
        }

        return "MULTI_ASSET";
    }

    private static void AppendSources(StringBuilder report)
    {
        report.AppendLine("## Data Sources");
        report.AppendLine();
        report.AppendLine("- CoinGecko news: recent pages when a demo key is configured. Not a guaranteed historical archive. No key is stored in source.");
        report.AppendLine("- CryptoPanic: token required. Free history is limited. No token is stored in source.");
        report.AppendLine("- GDELT DOC 2.0: historical article lists. Seen time is not always the original publication time. Asset mapping is title-based.");
        report.AppendLine("- FRED: CPI, PPI, payrolls, and fed funds actuals. Observation dates are date-only, so they are excluded from intraday bars. Consensus is not provided and is not invented.");
        report.AppendLine();
    }

    private static void AppendStatistics(StringBuilder report, IReadOnlyList<RawNewsItem> raw, IReadOnlyList<NewsEvent> events)
    {
        var articles = raw.Count == 0 ? events.Sum(item => item.OriginalArticles.Count) : raw.Count;
        var ratio = articles == 0 ? 0 : 1d - ((double)events.Count / articles);
        report.AppendLine("## News Event Statistics");
        report.AppendLine();
        report.AppendLine("- Total articles: " + articles.ToString(CultureInfo.InvariantCulture));
        report.AppendLine("- Unique events: " + events.Count.ToString(CultureInfo.InvariantCulture));
        report.AppendLine("- Duplicate ratio: " + ratio.ToString("0.00", CultureInfo.InvariantCulture));
        report.AppendLine("- DATASET_SCOPE: " + DatasetScope(events));
        report.AppendLine("- Assets: " + ListOrNone(events.SelectMany(item => item.Assets).Distinct(StringComparer.OrdinalIgnoreCase)));
        report.AppendLine("- Categories: " + ListOrNone(events.Select(item => item.EventType.ToString()).Distinct()));
        report.AppendLine("- Sources: " + ListOrNone(events.SelectMany(item => item.OriginalArticles).Select(article => article.Source).Distinct(StringComparer.OrdinalIgnoreCase)));
        report.AppendLine();
    }

    private static string ListOrNone(IEnumerable<string> values)
    {
        var list = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        return list.Count == 0 ? "none" : string.Join(", ", list);
    }

    private static async Task<IReadOnlyList<MarketCandle>> ReadCandlesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var bars = JsonSerializer.Deserialize<List<CachedBar>>(await File.ReadAllTextAsync(path, cancellationToken));
        return bars?
            .Select(bar => new MarketCandle
            {
                OpenTime = bar.OpenTime,
                CloseTime = bar.CloseTime,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                TakerBuyVolume = bar.TakerBuyVolume,
                IsClosed = true
            })
            .OrderBy(bar => bar.OpenTime)
            .ToList() ?? [];
    }

    private sealed record CachedBar(
        DateTimeOffset OpenTime,
        DateTimeOffset CloseTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume,
        decimal TakerBuyVolume = 0);

    private sealed record NewsOutcome(ReplayResult? Full, ReplayResult? Oos, string Status);
}
