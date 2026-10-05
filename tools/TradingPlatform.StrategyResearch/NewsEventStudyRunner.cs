using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.News;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Event study on the locally stored news: does the classifier direction alone predict the next 15m/1h/4h, and does
/// market confirmation improve it? Every decision is made at the first closed 5m bar after the platform first saw
/// the story, with the event rebuilt from only the copies retrieved by then.
/// </summary>
internal static class NewsEventStudyRunner
{
    private const string BaseTimeframe = "5m";
    private static readonly string[] GlobalProxies = ["BTCUSDT", "ETHUSDT"];

    public static async Task<int> RunAsync(string repoRoot, CancellationToken cancellationToken)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var options = NewsLiveAnalyzer.LoadOptions(repoRoot);
        options.Enabled = true;
        options.Strategy.Enabled = true;
        var anyAge = NewsLiveAnalyzer.LoadOptions(repoRoot);
        anyAge.Enabled = true;
        anyAge.Strategy.Enabled = true;
        anyAge.Strategy.MaxNewsAgeMinutes = 1_000_000;
        var catalog = NewsAssetCatalog.LoadUniverse(repoRoot);
        var store = new FileNewsStore(Path.Combine(repoRoot, options.StorePath));
        var loaded = await store.LoadRawAsync(cancellationToken);
        var raw = loaded
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.OrderBy(item => item.RetrievedAtUtc).First())
            .ToList();
        var pipeline = new NewsPipeline(options, catalog: catalog);
        var clustered = pipeline.Build(raw);
        Console.WriteLine($"News study: {loaded.Count} stored rows, {raw.Count} unique articles, {clustered.Count} events after gates.");

        var events = new List<(NewsEvent Event, DateTimeOffset FirstSeen, List<NewsAssetContext> Targets)>();
        foreach (var item in clustered)
        {
            var seen = NewsEventStudy.AtFirstSighting(item, raw, pipeline);
            if (seen is null)
            {
                continue;
            }

            var targets = NewsLiveAnalyzer.Targets(seen, catalog).ToList();
            if (seen.MarketScope == MarketScope.Global && seen.AffectedAssets.Count == 0)
            {
                targets = targets.Where(target => GlobalProxies.Contains(target.Symbol, StringComparer.OrdinalIgnoreCase)).ToList();
            }

            events.Add((seen, seen.DetectedAtUtc, targets));
        }

        var symbols = events.SelectMany(row => row.Targets).Select(target => target.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var from = events.Count == 0 ? DateTimeOffset.UtcNow : events.Min(row => row.FirstSeen).AddDays(-12);
        var to = DateTimeOffset.UtcNow;
        var frames = new[] { options.Strategy.Timeframes.Execution, options.Strategy.Timeframes.Flow, options.Strategy.Timeframes.Structure, options.Strategy.Timeframes.Trend, BaseTimeframe }
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var market = await LoadMarketAsync(repoRoot, symbols, frames, from, to, cancellationToken);

        var samples = new List<Sample>();
        var rejects = new Dictionary<string, int>(StringComparer.Ordinal);
        var latency = new List<LatencyRow>();
        foreach (var (item, firstSeen, targets) in events)
        {
            foreach (var target in targets)
            {
                if (!market.TryGetValue(target.Symbol, out var book) || !book.TryGetValue(BaseTimeframe, out var bars) || bars.Count < 50)
                {
                    continue;
                }

                if (NewsEventStudy.DecisionTime(bars, firstSeen) is not { } decision)
                {
                    continue;
                }

                latency.Add(new LatencyRow(item.EventId, target.Symbol, item.OriginalArticles.FirstOrDefault()?.Provider ?? "?", item.PublishedAtUtc, firstSeen, decision));
                if (item.Direction is not (EventDirection.Bullish or EventDirection.Bearish))
                {
                    continue;
                }

                var bullish = item.Direction == EventDirection.Bullish;
                var age = (decision - item.PublishedAtUtc).TotalMinutes;
                var newsGates = NewsGates(item, target, options);
                var groups = new List<string> { "A_all_directional" };
                if (newsGates)
                {
                    groups.Add("B_news_gates_any_age");
                    if (age <= options.Strategy.MaxNewsAgeMinutes)
                    {
                        groups.Add("C_news_gates_fresh");
                    }
                }

                var confirmed = NewsMarketConfirmation.Evaluate(item, target, decision, book, options);
                Count(rejects, ReasonKey(confirmed));
                if (confirmed.Signal != NewsMarketSignals.NoTrade)
                {
                    groups.Add("D_news_plus_confirmation");
                }

                var confirmedAnyAge = NewsMarketConfirmation.Evaluate(item, target, decision, book, anyAge);
                if (confirmedAnyAge.Signal != NewsMarketSignals.NoTrade)
                {
                    groups.Add("E_confirmation_any_age");
                }

                foreach (var group in groups)
                {
                    samples.Add(new Sample(group, item.EventId, target.Symbol, bullish, decision, Forward(bars, decision, bullish)));
                }

                if (newsGates)
                {
                    samples.Add(new Sample("P_placebo_minus_24h", item.EventId, target.Symbol, bullish, decision.AddHours(-24), Forward(bars, decision.AddHours(-24), bullish)));
                }
            }
        }

        var summaries = samples
            .GroupBy(sample => sample.Group)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => NewsEventStudy.Horizons.Select(horizon => NewsEventStudy.Summarize(
                group.Key,
                horizon.Label,
                group.Select(sample => sample.Returns.GetValueOrDefault(horizon.Label)).Where(value => value is not null).Select(value => value!.Value))))
            .ToList();

        var outDir = Path.Combine(repoRoot, "artifacts", "research", "news-study");
        Directory.CreateDirectory(outDir);
        var report = new
        {
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            StoredRows = loaded.Count,
            UniqueArticles = raw.Count,
            Events = clustered.Count,
            Symbols = symbols.Count,
            Summaries = summaries,
            Rejects = rejects.OrderByDescending(pair => pair.Value).ToDictionary(),
            Samples = samples
        };
        await File.WriteAllTextAsync(Path.Combine(outDir, "news-event-study.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var markdown = Markdown(loaded, raw, clustered, events.Select(row => row.Event).ToList(), summaries, rejects, latency, options);
        await File.WriteAllTextAsync(Path.Combine(outDir, "news-event-study.md"), markdown, cancellationToken);
        Console.WriteLine(markdown);
        return 0;
    }

    private static bool NewsGates(NewsEvent item, NewsAssetContext target, NewsOptions options) =>
        item.ConfidenceScore >= options.Strategy.MinNewsConfidence
        && item.ImpactScore >= options.Strategy.MinNewsImpact
        && NewsAssetCatalog.Relevance(item, target.BaseAsset) >= options.Strategy.MinRelevance;

    private static Dictionary<string, double?> Forward(IReadOnlyList<MarketCandle> bars, DateTimeOffset decision, bool bullish) =>
        NewsEventStudy.Horizons.ToDictionary(
            horizon => horizon.Label,
            horizon => NewsEventStudy.SignedForwardPercent(bars, decision, horizon.Span, bullish));

    private static string ReasonKey(NewsMarketDecision decision)
    {
        if (decision.Signal != NewsMarketSignals.NoTrade)
        {
            return "signal";
        }

        var text = decision.Reason;
        var at = text.LastIndexOf("Stopped at ", StringComparison.Ordinal);
        if (at < 0)
        {
            return "other";
        }

        var rest = text[(at + "Stopped at ".Length)..];
        var colon = rest.IndexOf(':');
        return colon > 0 ? rest[..colon] : rest;
    }

    private static void Count(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static string Markdown(
        IReadOnlyList<RawNewsItem> loaded,
        IReadOnlyList<RawNewsItem> raw,
        IReadOnlyList<NewsEvent> clustered,
        IReadOnlyList<NewsEvent> seen,
        IReadOnlyList<NewsStudySummary> summaries,
        Dictionary<string, int> rejects,
        IReadOnlyList<LatencyRow> latency,
        NewsOptions options)
    {
        var text = new StringBuilder();
        text.AppendLine("# News event study");
        text.AppendLine();
        text.AppendLine($"Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC. Articles published {raw.Min(item => item.PublishedAtUtc):yyyy-MM-dd} to {raw.Max(item => item.PublishedAtUtc):yyyy-MM-dd}.");
        text.AppendLine();
        text.AppendLine("Decision = first closed 5m bar after the platform first retrieved the story; the event is rebuilt from only the copies retrieved by then. Global events with no named coin are measured on BTCUSDT and ETHUSDT only. Returns are signed by the news direction, in percent; cost is a " + NewsEventStudy.RoundTripCostPercent.ToString("0.00") + "% round trip.");
        text.AppendLine();
        text.AppendLine("## Data and deduplication");
        text.AppendLine();
        var multi = clustered.Count(item => item.OriginalArticles.Count > 1);
        text.AppendLine($"- Stored rows: {loaded.Count}; unique article ids: {raw.Count} ({loaded.Count - raw.Count} re-fetched copies dropped).");
        var batches = raw.Select(item => item.RetrievedAtUtc.ToUnixTimeSeconds() / 600).Distinct().Count();
        var span = (raw.Max(item => item.RetrievedAtUtc) - raw.Min(item => item.RetrievedAtUtc)).TotalDays;
        text.AppendLine($"- Collection cadence: {batches} distinct retrieval batches (10-minute buckets) over {span:0.0} days. Unless this is close to one batch per poll interval, latency below measures how often the collector was run, not the pipeline, and fresh-news groups cannot be tested.");
        text.AppendLine($"- Events after clustering and the pipeline impact/confidence gate: {clustered.Count}; events built from more than one article: {multi}.");
        text.AppendLine("- Directions at first sighting: " + string.Join(", ", seen.GroupBy(item => item.Direction).OrderByDescending(group => group.Count()).Select(group => group.Key + " " + group.Count())) + ".");
        text.AppendLine("- Event types: " + string.Join(", ", seen.GroupBy(item => item.EventType).OrderByDescending(group => group.Count()).Take(10).Select(group => group.Key + " " + group.Count())) + ".");
        text.AppendLine("- Scope: " + string.Join(", ", seen.GroupBy(item => item.MarketScope).Select(group => group.Key + " " + group.Count())) + ".");
        text.AppendLine();
        text.AppendLine("## Latency (per event × coin)");
        text.AppendLine();
        text.AppendLine("| Leg | n | median min | p90 min | share over " + options.Strategy.MaxNewsAgeMinutes + " min |");
        text.AppendLine("|---|---:|---:|---:|---:|");
        LatencyLine(text, "publish → first retrieval", latency.Select(row => (row.FirstSeen - row.Published).TotalMinutes).ToList(), options.Strategy.MaxNewsAgeMinutes);
        LatencyLine(text, "first retrieval → decision bar", latency.Select(row => (row.Decision - row.FirstSeen).TotalMinutes).ToList(), options.Strategy.MaxNewsAgeMinutes);
        LatencyLine(text, "publish → decision", latency.Select(row => (row.Decision - row.Published).TotalMinutes).ToList(), options.Strategy.MaxNewsAgeMinutes);
        text.AppendLine();
        text.AppendLine("Publish → first retrieval by provider (median minutes, n):");
        text.AppendLine();
        foreach (var group in latency.DistinctBy(row => row.EventId).GroupBy(row => row.Provider).OrderByDescending(group => group.Count()).Take(12))
        {
            var values = group.Select(row => (row.FirstSeen - row.Published).TotalMinutes).Order().ToList();
            text.AppendLine($"- {group.Key}: {values[values.Count / 2]:0} ({values.Count})");
        }

        text.AppendLine();
        text.AppendLine("## Forward returns");
        text.AppendLine();
        text.AppendLine("A = every directional event; B = passes the news confidence/impact/relevance gates at any age; C = B and fresh enough for the live age limit; D = the live news + market confirmation rule; E = the confirmation rule with the age limit removed; P = the B set measured 24h earlier (placebo).");
        text.AppendLine();
        text.AppendLine("| Group | Horizon | n | mean % | median % | hit rate | t | mean after cost % |");
        text.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var row in summaries)
        {
            text.AppendLine($"| {row.Group} | {row.Horizon} | {row.Count} | {F(row.MeanPercent, 3)} | {F(row.MedianPercent, 3)} | {F(row.HitRate * 100, 0)}% | {F(row.TStat, 2)} | {F(row.MeanAfterCostPercent, 3)} |");
        }

        text.AppendLine();
        text.AppendLine("## Why the live rule said NO_TRADE");
        text.AppendLine();
        foreach (var pair in rejects.OrderByDescending(pair => pair.Value))
        {
            text.AppendLine($"- {pair.Key}: {pair.Value}");
        }

        return text.ToString();
    }

    private static void LatencyLine(StringBuilder text, string label, List<double> values, int limit)
    {
        if (values.Count == 0)
        {
            text.AppendLine($"| {label} | 0 | - | - | - |");
            return;
        }

        values.Sort();
        var over = values.Count(value => value > limit) / (double)values.Count;
        text.AppendLine($"| {label} | {values.Count} | {values[values.Count / 2]:0} | {values[(int)Math.Min(values.Count - 1, Math.Floor(values.Count * 0.9))]:0} | {over * 100:0}% |");
    }

    private static string F(double? value, int digits) =>
        value is null ? "-" : value.Value.ToString("F" + digits, CultureInfo.InvariantCulture);

    private static async Task<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>> LoadMarketAsync(
        string repoRoot,
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> frames,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var dir = Path.Combine(repoRoot, "artifacts", "research", "news-study", "klines");
        Directory.CreateDirectory(dir);
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(30) };
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            var book = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
            foreach (var frame in frames)
            {
                var path = Path.Combine(dir, symbol + "_" + frame + ".json");
                List<MarketCandle> candles;
                if (File.Exists(path))
                {
                    candles = (JsonSerializer.Deserialize<List<Bar>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? []).Select(bar => bar.ToCandle()).ToList();
                }
                else
                {
                    try
                    {
                        candles = await FetchAsync(http, symbol, frame, from, to, cancellationToken);
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"  {symbol} {frame}: {ex.Message}");
                        continue;
                    }

                    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(candles.Select(Bar.From)), cancellationToken);
                }

                if (candles.Count > 0)
                {
                    book[frame] = candles;
                }
            }

            result[symbol] = book;
        }

        return result;
    }

    private static async Task<List<MarketCandle>> FetchAsync(HttpClient http, string symbol, string frame, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rows = new List<MarketCandle>();
        var cursor = from.ToUnixTimeMilliseconds();
        var end = to.ToUnixTimeMilliseconds();
        var now = DateTimeOffset.UtcNow;
        while (cursor < end)
        {
            using var response = await http.GetAsync($"fapi/v1/klines?symbol={symbol}&interval={frame}&startTime={cursor}&endTime={end}&limit=1500", cancellationToken);
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var page = document.RootElement.EnumerateArray().ToList();
            if (page.Count == 0)
            {
                break;
            }

            foreach (var row in page)
            {
                var close = DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64() + 1);
                if (close > now)
                {
                    continue;
                }

                rows.Add(new MarketCandle
                {
                    OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()),
                    CloseTime = close,
                    Open = decimal.Parse(row[1].GetString()!, CultureInfo.InvariantCulture),
                    High = decimal.Parse(row[2].GetString()!, CultureInfo.InvariantCulture),
                    Low = decimal.Parse(row[3].GetString()!, CultureInfo.InvariantCulture),
                    Close = decimal.Parse(row[4].GetString()!, CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(row[5].GetString()!, CultureInfo.InvariantCulture),
                    TakerBuyVolume = decimal.Parse(row[9].GetString()!, CultureInfo.InvariantCulture),
                    IsClosed = true
                });
            }

            cursor = page[^1][0].GetInt64() + 1;
            await Task.Delay(120, cancellationToken);
        }

        return rows.DistinctBy(row => row.OpenTime).OrderBy(row => row.OpenTime).ToList();
    }

    private sealed record Sample(string Group, string EventId, string Symbol, bool Bullish, DateTimeOffset DecisionUtc, Dictionary<string, double?> Returns);

    private sealed record Bar(DateTimeOffset OpenTime, DateTimeOffset CloseTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal TakerBuyVolume)
    {
        public static Bar From(MarketCandle candle) =>
            new(candle.OpenTime, candle.CloseTime, candle.Open, candle.High, candle.Low, candle.Close, candle.Volume, candle.TakerBuyVolume);

        public MarketCandle ToCandle() => new()
        {
            OpenTime = OpenTime,
            CloseTime = CloseTime,
            Open = Open,
            High = High,
            Low = Low,
            Close = Close,
            Volume = Volume,
            TakerBuyVolume = TakerBuyVolume,
            IsClosed = true
        };
    }

    private sealed record LatencyRow(string EventId, string Symbol, string Provider, DateTimeOffset Published, DateTimeOffset FirstSeen, DateTimeOffset Decision);
}
