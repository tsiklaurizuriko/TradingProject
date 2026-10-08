using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.News;

public static class NewsMarketSignals
{
    public const string LongCandidate = "LONG_CANDIDATE";
    public const string ShortCandidate = "SHORT_CANDIDATE";
    public const string NoTrade = "NO_TRADE";
}

public sealed record NewsMarketDecision(
    string Signal,
    string Symbol,
    string Reason,
    double NewsScore,
    double MarketScore,
    double FinalScore,
    StrategySignalDetail Detail,
    LiveSignalRecord? Record,
    string? RejectionCode = null,
    string? EventId = null);

public sealed class LiveSignalRecord
{
    public string Symbol { get; set; } = string.Empty;
    public DateTimeOffset SignalTimestamp { get; set; }
    public string Direction { get; set; } = string.Empty;
    public decimal ReferencePrice { get; set; }
    /// <summary>Close time of the execution candle that gave <see cref="ReferencePrice"/>.</summary>
    public DateTimeOffset? ReferencePriceAt { get; set; }
    public string EventId { get; set; } = string.Empty;
    public double NewsScore { get; set; }
    public double MarketScore { get; set; }
    public double FinalScore { get; set; }
    public Dictionary<string, decimal?> ForwardReturn { get; set; } = [];
}

public static class NewsMarketConfirmation
{
    public static NewsMarketDecision Evaluate(
        NewsEvent item,
        NewsAssetContext asset,
        DateTimeOffset decisionTime,
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> candlesByTimeframe,
        NewsOptions options)
    {
        var strategy = options.Strategy;
        var relevance = NewsAssetCatalog.Relevance(item, asset.BaseAsset);
        var age = (decisionTime - item.PublishedAtUtc).TotalMinutes;
        if (item.PublishedAtUtc > decisionTime)
        {
            return Reject(asset, item, "Stopped at time: Future news is not visible at the decision time.");
        }

        candlesByTimeframe = ClosedBy(candlesByTimeframe, decisionTime);
        if (StaleExecution(candlesByTimeframe, strategy.Timeframes.Execution, decisionTime) is { } stale)
        {
            return Reject(asset, item, stale);
        }

        if (age > strategy.MaxNewsAgeMinutes)
        {
            return Reject(asset, item, "Stopped at age: " + age.ToString("0", CultureInfo.InvariantCulture) + "m exceeds " + strategy.MaxNewsAgeMinutes.ToString(CultureInfo.InvariantCulture) + "m.");
        }

        if (item.Direction is EventDirection.Neutral or EventDirection.Unknown or EventDirection.Mixed)
        {
            return Reject(asset, item, "Stopped at direction: " + item.Direction + " is ignored.");
        }

        if (item.ConfidenceScore < strategy.MinNewsConfidence)
        {
            return Reject(asset, item, "Stopped at confidence: " + item.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture) + " is below " + strategy.MinNewsConfidence.ToString("0.00", CultureInfo.InvariantCulture) + ".");
        }

        if (item.ImpactScore < strategy.MinNewsImpact)
        {
            return Reject(asset, item, "Stopped at impact: " + item.ImpactScore.ToString("0.00", CultureInfo.InvariantCulture) + " is below " + strategy.MinNewsImpact.ToString("0.00", CultureInfo.InvariantCulture) + ".");
        }

        if (relevance < strategy.MinRelevance)
        {
            return Reject(asset, item, "Stopped at relevance: " + relevance.ToString("0.00", CultureInfo.InvariantCulture) + " is below " + strategy.MinRelevance.ToString("0.00", CultureInfo.InvariantCulture) + ".");
        }

        var decay = NewsFeatureProvider.DecayWeight(options.DecayLambdaPerMinute, item.PublishedAtUtc, decisionTime);
        var newsScore = strategy.Weights.News * ((item.ImpactScore + item.ConfidenceScore) / 2d) * decay;
        var bullish = item.Direction == EventDirection.Bullish;
        var notes = new List<string>();
        var scores = new List<string> { ScoreLine("News", newsScore, strategy.Weights.News) };
        var aligned = 0d;
        var opposed = 0d;
        Take(strategy.Weights.Trend, Trend(candlesByTimeframe, strategy.Timeframes.Trend, bullish, notes), "Trend", scores, ref aligned, ref opposed);
        Take(strategy.Weights.Volume, Volume(candlesByTimeframe, strategy.Timeframes.Flow, bullish, notes), "Volume", scores, ref aligned, ref opposed);
        Take(strategy.Weights.TakerImbalance, Taker(candlesByTimeframe, strategy.Timeframes.Flow, bullish, notes), "Taker", scores, ref aligned, ref opposed);
        Take(strategy.Weights.Structure, Structure(candlesByTimeframe, strategy.Timeframes.Structure, bullish, notes), "Structure", scores, ref aligned, ref opposed);
        var volatility = Volatility(candlesByTimeframe, strategy.Timeframes.Execution, notes);
        var volatilityPoints = strategy.Weights.Volatility * volatility;
        scores.Add(ScoreLine("Volatility", volatilityPoints, strategy.Weights.Volatility));
        var marketScore = aligned + volatilityPoints;
        var total = newsScore + marketScore;
        scores.Add(ScoreLine("Total", total, strategy.Weights.News + strategy.Weights.Trend + strategy.Weights.Volume + strategy.Weights.TakerImbalance + strategy.Weights.Structure + strategy.Weights.Volatility));
        var header = Describe(item, asset, age, decay, scores, notes);
        if (opposed >= strategy.ConflictMarketScore)
        {
            return Reject(asset, item, Stop(header, "Stopped at market conflict: " + (bullish ? "Bullish" : "Bearish") + " news but strong opposing market confirmation. Opposing score " + opposed.ToString("0.0", CultureInfo.InvariantCulture) + "."));
        }

        if (newsScore < strategy.MinNewsScore)
        {
            return Reject(asset, item, Stop(header, "Stopped at news score: " + newsScore.ToString("0.0", CultureInfo.InvariantCulture) + " is below the configured minimum of " + strategy.MinNewsScore.ToString(CultureInfo.InvariantCulture) + "."));
        }

        if (marketScore < strategy.MinMarketScore)
        {
            return Reject(asset, item, Stop(header, "Stopped at market score: " + marketScore.ToString("0.0", CultureInfo.InvariantCulture) + " is below the configured minimum of " + strategy.MinMarketScore.ToString(CultureInfo.InvariantCulture) + "."));
        }

        if (total < strategy.MinTotalScore)
        {
            return Reject(asset, item, Stop(header, "Stopped at total score: " + total.ToString("0.0", CultureInfo.InvariantCulture) + " is below the configured minimum of " + strategy.MinTotalScore.ToString(CultureInfo.InvariantCulture) + ". News " + newsScore.ToString("0.0", CultureInfo.InvariantCulture) + ", market " + marketScore.ToString("0.0", CultureInfo.InvariantCulture) + "."));
        }

        var signal = bullish ? NewsMarketSignals.LongCandidate : NewsMarketSignals.ShortCandidate;
        var price = ReferencePrice(candlesByTimeframe, strategy.Timeframes.Execution);
        var execution = candlesByTimeframe.TryGetValue(strategy.Timeframes.Execution, out var executionRows) ? executionRows : [];
        var record = new LiveSignalRecord
        {
            Symbol = asset.Symbol,
            SignalTimestamp = decisionTime,
            Direction = signal,
            ReferencePrice = price,
            ReferencePriceAt = execution.Count > 0 ? execution[^1].CloseTime : null,
            EventId = item.EventId,
            NewsScore = newsScore,
            MarketScore = marketScore,
            FinalScore = total,
            ForwardReturn = ForwardReturns(execution, decisionTime, price)
        };
        var reason = header + "SIGNAL:\n" + signal;
        return new NewsMarketDecision(
            signal,
            asset.Symbol,
            reason,
            newsScore,
            marketScore,
            total,
            new StrategySignalDetail(bullish ? SignalType.Buy : SignalType.Sell, reason, decisionTime, Snapshot: Snapshot(newsScore, marketScore, total), Status: "RESEARCHING"),
            record,
            null,
            item.EventId);
    }

    private static void Take(double weight, double signed, string name, List<string> scores, ref double aligned, ref double opposed)
    {
        var points = signed > 0 ? weight * signed : 0;
        scores.Add(ScoreLine(name, points, weight));
        if (signed >= 0)
        {
            aligned += points;
        }
        else
        {
            opposed += weight * -signed;
        }
    }

    private static string ScoreLine(string name, double points, double weight) =>
        name + "=" + points.ToString("0.0", CultureInfo.InvariantCulture) + "/" + weight.ToString("0", CultureInfo.InvariantCulture);

    public static void FillForwardReturns(LiveSignalRecord record, IReadOnlyList<MarketCandle> executionCandles)
    {
        if (record.ReferencePrice <= 0)
        {
            return;
        }

        var filled = ForwardReturns(executionCandles, record.SignalTimestamp, record.ReferencePrice);
        foreach (var pair in filled)
        {
            if (pair.Value is not null)
            {
                record.ForwardReturn[pair.Key] = pair.Value;
            }
        }
    }

    private static double Trend(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, bool bullish, List<string> notes)
    {
        if (!TryLast(book, timeframe, 50, out var candles, out var index))
        {
            notes.Add("Trend=" + timeframe + " unavailable");
            return 0;
        }

        var cache = new CausalIndicatorCache(candles);
        var fast = cache.Ema(20)[index];
        var slow = cache.Ema(50)[index];
        if (fast is null || slow is null)
        {
            notes.Add("Trend=unavailable");
            return 0;
        }

        var up = fast > slow && candles[index].Close > slow;
        notes.Add("Trend=" + (up ? "Bullish" : "Bearish"));
        return Agree(bullish, up) ? 1 : -1;
    }

    private static double Volume(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, bool bullish, List<string> notes)
    {
        if (!TryLast(book, timeframe, 21, out var candles, out var index))
        {
            notes.Add("Volume=" + timeframe + " unavailable");
            return 0;
        }

        var rvol = new CausalIndicatorCache(candles).RelativeVolume(20)[index];
        if (rvol is null)
        {
            notes.Add("Volume=unavailable");
            return 0;
        }

        notes.Add("VolumeExpansion=" + rvol.Value.ToString("0.00", CultureInfo.InvariantCulture) + "x");
        var expanded = Math.Clamp((double)(rvol.Value - 1m) / 1.5d, 0d, 1d);
        var priceAgrees = bullish ? candles[index].Close >= candles[index - 1].Close : candles[index].Close <= candles[index - 1].Close;
        return priceAgrees ? expanded : -expanded;
    }

    private static double Taker(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, bool bullish, List<string> notes)
    {
        if (!TryLast(book, timeframe, 1, out var candles, out var index))
        {
            notes.Add("TakerImbalance=" + timeframe + " unavailable");
            return 0;
        }

        var imbalance = TakerFlow.Imbalance(candles[index]);
        if (imbalance is null)
        {
            notes.Add("TakerImbalance=unavailable");
            return 0;
        }

        notes.Add("TakerImbalance=" + imbalance.Value.ToString("+0.00;-0.00;0", CultureInfo.InvariantCulture));
        var signed = (double)imbalance.Value;
        return bullish ? signed : -signed;
    }

    private static double Structure(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, bool bullish, List<string> notes)
    {
        if (!TryLast(book, timeframe, 20, out var candles, out var index))
        {
            notes.Add("Structure=" + timeframe + " unavailable");
            return 0;
        }

        var price = new CausalIndicatorCache(candles).PriceAction();
        var bar = price.Structure[index];
        var sweepLow = price.ConfirmedAt(index, PatternKinds.LiquiditySweepLow).Any();
        var sweepHigh = price.ConfirmedAt(index, PatternKinds.LiquiditySweepHigh).Any();
        var up = bar.Bias > 0 || bar.BosBull || sweepLow;
        var down = bar.Bias < 0 || bar.BosBear || sweepHigh;
        notes.Add("Structure=" + (up && !down ? "Bullish" : down && !up ? "Bearish" : "Mixed"));
        if (up == down)
        {
            return 0;
        }

        return Agree(bullish, up) ? 1 : -1;
    }

    private static double Volatility(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, List<string> notes)
    {
        if (!TryLast(book, timeframe, 15, out var candles, out var index))
        {
            notes.Add("Volatility=" + timeframe + " unavailable");
            return 0;
        }

        var atr = new CausalIndicatorCache(candles).AtrPercent(14)[index];
        if (atr is null)
        {
            notes.Add("Volatility=unavailable");
            return 0;
        }

        var acceptable = atr.Value is > 0m and < 15m;
        notes.Add("Volatility=" + (acceptable ? "Acceptable" : "Extreme"));
        return acceptable ? 0.8 : 0;
    }

    private static bool TryLast(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, int minimum, out IReadOnlyList<MarketCandle> candles, out int index)
    {
        if (!book.TryGetValue(timeframe, out candles!) || candles.Count < minimum)
        {
            candles = [];
            index = -1;
            return false;
        }

        index = candles.Count - 1;
        return true;
    }

    private static bool Agree(bool bullish, bool up) => bullish == up;

    /// <summary>Only candles closed at or before the decision are visible, whatever the caller loaded.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> ClosedBy(
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book,
        DateTimeOffset decisionTime)
    {
        if (book.Values.All(rows => rows.Count == 0 || rows[^1].CloseTime <= decisionTime))
        {
            return book;
        }

        return book.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<MarketCandle>)pair.Value.Where(candle => candle.CloseTime <= decisionTime).ToList(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? StaleExecution(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe, DateTimeOffset decisionTime)
    {
        if (!book.TryGetValue(timeframe, out var rows) || rows.Count == 0
            || !TimeframeExtensions.TryParseInterval(timeframe, out var parsed))
        {
            return null;
        }

        var lag = decisionTime - rows[^1].CloseTime;
        return lag > parsed.ToDuration() * 2
            ? "Stopped at market data: the last closed " + timeframe + " candle is " + lag.TotalMinutes.ToString("0", CultureInfo.InvariantCulture) + "m older than the decision."
            : null;
    }

    private static decimal ReferencePrice(IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> book, string timeframe) =>
        book.TryGetValue(timeframe, out var candles) && candles.Count > 0 ? candles[^1].Close : 0m;

    private static Dictionary<string, decimal?> ForwardReturns(IReadOnlyList<MarketCandle> candles, DateTimeOffset at, decimal reference)
    {
        var result = new Dictionary<string, decimal?>();
        foreach (var minutes in new[] { 5, 15, 30, 60, 240 })
        {
            var key = minutes < 60 ? minutes + "m" : (minutes / 60) + "h";
            result[key] = reference <= 0
                ? null
                : candles.FirstOrDefault(candle => candle.CloseTime >= at.AddMinutes(minutes)) is { Close: > 0 } future
                    ? (future.Close - reference) / reference
                    : null;
        }

        return result;
    }

    private static string Describe(NewsEvent item, NewsAssetContext asset, double age, double decay, List<string> scores, List<string> notes)
    {
        var text = new StringBuilder();
        text.AppendLine("NEWS:");
        text.AppendLine(item.Direction.ToString());
        text.AppendLine("Impact=" + item.ImpactScore.ToString("0.00", CultureInfo.InvariantCulture));
        text.AppendLine("Confidence=" + item.ConfidenceScore.ToString("0.00", CultureInfo.InvariantCulture));
        text.AppendLine("Age=" + age.ToString("0", CultureInfo.InvariantCulture) + "m");
        text.AppendLine("Decay=" + decay.ToString("0.00", CultureInfo.InvariantCulture));
        text.AppendLine("EventType=" + item.EventType);
        text.AppendLine("AffectedAsset=" + asset.BaseAsset);
        text.AppendLine("Relevance=" + NewsAssetCatalog.Relevance(item, asset.BaseAsset).ToString("0.00", CultureInfo.InvariantCulture));
        text.AppendLine("OI=Unavailable");
        text.AppendLine();
        text.AppendLine("MARKET:");
        foreach (var note in notes)
        {
            text.AppendLine(note);
        }

        text.AppendLine();
        text.AppendLine("SCORE:");
        foreach (var score in scores)
        {
            text.AppendLine(score);
        }

        text.AppendLine();
        return text.ToString();
    }

    private static Dictionary<string, decimal?> Snapshot(double news, double market, double total) =>
        new()
        {
            ["newsScore"] = (decimal)news,
            ["marketScore"] = (decimal)market,
            ["finalScore"] = (decimal)total
        };

    private static string Stop(string header, string reason) =>
        header + "RESULT:\nNO_TRADE\n\nReason:\n" + reason;

    private static NewsMarketDecision Reject(NewsAssetContext asset, NewsEvent item, string reason) =>
        new(
            NewsMarketSignals.NoTrade,
            asset.Symbol,
            reason,
            0,
            0,
            0,
            new StrategySignalDetail(SignalType.NoAction, reason, item.PublishedAtUtc, Status: "RESEARCHING"),
            null,
            null,
            item.EventId);
}

public sealed class NewsLiveReport
{
    public bool Enabled { get; set; }
    public bool SessionRunning { get; set; }
    public int UniverseCount { get; set; }
    public int Events { get; set; }
    public List<string> Errors { get; set; } = [];
    public List<NewsMarketDecision> Decisions { get; set; } = [];
}

public static class NewsLiveAnalyzer
{
    public static NewsLiveReport Analyze(
        NewsOptions options,
        NewsAssetCatalog catalog,
        IReadOnlyList<NewsEvent> events,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>> market,
        DateTimeOffset decisionTime)
    {
        var report = new NewsLiveReport { Enabled = options.Enabled, UniverseCount = catalog.Identities.Count, Events = events.Count };
        if (!options.Enabled || !options.Strategy.Enabled)
        {
            report.Errors.Add("News live analysis is disabled.");
            return report;
        }

        report.Errors.AddRange(options.Strategy.Validate());
        if (report.Errors.Count > 0)
        {
            return report;
        }

        foreach (var item in events)
        {
            foreach (var asset in Targets(item, catalog))
            {
                market.TryGetValue(asset.Symbol, out var frames);
                report.Decisions.Add(NewsMarketConfirmation.Evaluate(item, asset, decisionTime, frames ?? new Dictionary<string, IReadOnlyList<MarketCandle>>(), options));
            }
        }

        return report;
    }

    public static async Task WriteAsync(string repoRoot, NewsLiveReport report, string executionTimeframe, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(repoRoot, "artifacts", "data", "news", "live");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "signals.json");
        var records = new List<LiveSignalRecord>();
        if (File.Exists(path))
        {
            records = JsonSerializer.Deserialize<List<LiveSignalRecord>>(await File.ReadAllTextAsync(path, cancellationToken)) ?? [];
        }

        foreach (var record in report.Decisions.Where(item => item.Record is not null).Select(item => item.Record!))
        {
            var index = records.FindIndex(item => item.EventId == record.EventId && string.Equals(item.Symbol, record.Symbol, StringComparison.OrdinalIgnoreCase) && item.SignalTimestamp == record.SignalTimestamp);
            if (index >= 0)
            {
                foreach (var pair in records[index].ForwardReturn)
                {
                    if (pair.Value is not null && record.ForwardReturn.GetValueOrDefault(pair.Key) is null)
                    {
                        record.ForwardReturn[pair.Key] = pair.Value;
                    }
                }

                records[index] = record;
            }
            else
            {
                records.Add(record);
            }
        }

        foreach (var record in records)
        {
            var candles = await ReadCandlesAsync(Path.Combine(repoRoot, "artifacts", "strategy-validation-cache", record.Symbol + "_" + executionTimeframe + ".json"), cancellationToken);
            NewsMarketConfirmation.FillForwardReturns(record, candles);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var text = new StringBuilder();
        text.AppendLine(report.SessionRunning ? "LIVE=ON" : "LIVE=OFF");
        text.AppendLine("Universe=" + report.UniverseCount);
        text.AppendLine("Events=" + report.Events);
        text.AppendLine("Decisions=" + report.Decisions.Count);
        foreach (var error in report.Errors)
        {
            text.AppendLine("Error=" + error);
        }

        foreach (var decision in report.Decisions.Take(20))
        {
            text.AppendLine("---");
            text.AppendLine(decision.Symbol + " " + decision.Signal);
            text.AppendLine(decision.Reason);
        }

        await File.WriteAllTextAsync(Path.Combine(dir, "latest.txt"), text.ToString(), cancellationToken);
    }

    public static IEnumerable<NewsAssetContext> Targets(NewsEvent item, NewsAssetCatalog catalog)
    {
        if (item.MarketScope == MarketScope.Global && item.AffectedAssets.Count == 0)
        {
            return catalog.Identities.Select(identity => new NewsAssetContext(identity.Symbol, identity.BaseAsset, identity.ProviderAssetId));
        }

        var symbols = new List<NewsAssetContext>();
        foreach (var link in item.AffectedAssets)
        {
            var identity = link.Symbol is not null ? catalog.FindSymbol(link.Symbol) : null;
            identity ??= catalog.Identities.FirstOrDefault(row => string.Equals(row.BaseAsset, link.BaseAsset, StringComparison.OrdinalIgnoreCase));
            if (identity is not null)
            {
                symbols.Add(new NewsAssetContext(identity.Symbol, identity.BaseAsset, identity.ProviderAssetId));
            }
        }

        if (symbols.Count == 0)
        {
            foreach (var asset in item.Assets)
            {
                var identity = catalog.Identities.FirstOrDefault(row => string.Equals(row.BaseAsset, asset, StringComparison.OrdinalIgnoreCase));
                if (identity is not null)
                {
                    symbols.Add(new NewsAssetContext(identity.Symbol, identity.BaseAsset, identity.ProviderAssetId));
                }
            }
        }

        return symbols.DistinctBy(item => item.Symbol);
    }

    public static async Task<int> RunAsync(string repoRoot, CancellationToken cancellationToken)
    {
        var options = LoadOptions(repoRoot);
        options.Enabled = true;
        options.Mode = "Live";
        options.Strategy.Enabled = true;
        var catalog = NewsAssetCatalog.LoadUniverse(repoRoot);
        var store = new FileNewsStore(Path.Combine(repoRoot, options.StorePath));
        var raw = await store.LoadRawAsync(cancellationToken);
        var events = raw.Count == 0 ? [] : new NewsPipeline(options, catalog: catalog).Build(raw);
        var market = await LoadMarketAsync(repoRoot, options, catalog, events, cancellationToken);
        var report = Analyze(options, catalog, events, market, DateTimeOffset.UtcNow);
        foreach (var decision in report.Decisions)
        {
            if (decision.Record is not null
                && market.TryGetValue(decision.Symbol, out var frames)
                && frames.TryGetValue(options.Strategy.Timeframes.Execution, out var candles))
            {
                NewsMarketConfirmation.FillForwardReturns(decision.Record, candles);
            }
        }

        if (events.Count == 0)
        {
            report.Errors.Add("No new articles are in the local store. Providers were not called per coin, and no history was invented.");
        }

        await WriteAsync(repoRoot, report, options.Strategy.Timeframes.Execution, cancellationToken);
        Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(repoRoot, "artifacts", "data", "news", "live", "latest.txt"), cancellationToken));
        return 0;
    }

    private static async Task<Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>> LoadMarketAsync(
        string repoRoot,
        NewsOptions options,
        NewsAssetCatalog catalog,
        IReadOnlyList<NewsEvent> events,
        CancellationToken cancellationToken)
    {
        var frames = new[] { options.Strategy.Timeframes.Execution, options.Strategy.Timeframes.Flow, options.Strategy.Timeframes.Structure, options.Strategy.Timeframes.Trend };
        var result = new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in events.SelectMany(item => Targets(item, catalog)).DistinctBy(item => item.Symbol))
        {
            var book = new Dictionary<string, IReadOnlyList<MarketCandle>>();
            foreach (var timeframe in frames.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var path = Path.Combine(repoRoot, "artifacts", "strategy-validation-cache", asset.Symbol + "_" + timeframe + ".json");
                var candles = await ReadCandlesAsync(path, cancellationToken);
                if (candles.Count > 0)
                {
                    book[timeframe] = candles;
                }
            }

            result[asset.Symbol] = book;
        }

        return result;
    }

    private static async Task<IReadOnlyList<MarketCandle>> ReadCandlesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var bars = JsonSerializer.Deserialize<List<CachedBar>>(await File.ReadAllTextAsync(path, cancellationToken));
        return bars?.Select(bar => new MarketCandle
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
        }).OrderBy(bar => bar.OpenTime).ToList() ?? [];
    }

    public static NewsOptions LoadOptions(string repoRoot)
    {
        var path = Path.Combine(repoRoot, "src", "TradingPlatform.Api", "appsettings.json");
        if (!File.Exists(path))
        {
            return new NewsOptions();
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!document.RootElement.TryGetProperty("News", out var news))
        {
            return new NewsOptions();
        }

        return JsonSerializer.Deserialize<NewsOptions>(news.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new NewsOptions();
    }

    private sealed record CachedBar(DateTimeOffset OpenTime, DateTimeOffset CloseTime, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume, decimal TakerBuyVolume = 0);
}
