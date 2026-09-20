using System.Diagnostics;
using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.StrategyResearch;

string[] pilotSymbols =
[
    "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT"
];
var phase = Arg(args, "--phase") ?? ResearchPhases.Pilot;
var candidateId = Arg(args, "--candidate");
var strategy = Arg(args, "--strategy");
var symbolFilter = Arg(args, "--symbol");
var timeframeRaw = Arg(args, "--timeframe");
var force = args.Any(a => string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
var resume = args.Any(a => string.Equals(a, "--resume", StringComparison.OrdinalIgnoreCase)) || !force;
var maxParallel = Math.Max(1, ParseInt(args, "--max-parallel", ParseInt(args, "--max-parallel-datasets", 2)));
var isPhase2 = string.Equals(phase, ResearchPhases.Two, StringComparison.OrdinalIgnoreCase)
    || string.Equals(phase, "2", StringComparison.OrdinalIgnoreCase);
var timeframes = (timeframeRaw ?? (isPhase2 ? "5m,15m,1h" : "1h"))
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var days = ParseInt(args, "--days", isPhase2 ? 365 : 90);
var root = FindRepoRoot();
var candleCacheDir = Path.Combine(root, "artifacts", "strategy-validation-cache");
var outDir = Path.Combine(root, "artifacts", "strategy-research", isPhase2 ? "phase-2" : "phase-1");
Directory.CreateDirectory(outDir);
var checkpointPath = Path.Combine(outDir, "checkpoint.json");
var reportPath = Path.Combine(root, "docs", "strategy-research-report.md");

var candidates = ResearchRegistry.Filter(candidateId, strategy, timeframe: null);
var symbols = string.IsNullOrWhiteSpace(symbolFilter)
    ? pilotSymbols
    : symbolFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (args.Any(a => string.Equals(a, "--universe", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine("Full 528-universe run is blocked. Phase 2 uses representative liquid coins only.");
    return 2;
}

if (args.Any(a => string.Equals(a, "--futures-data", StringComparison.OrdinalIgnoreCase)))
{
    var dataRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
    Directory.CreateDirectory(dataRoot);
    var futNotes = new List<string>
    {
        "Futures historical data ingest. LIVE disabled. Frozen five / Risk Engine / strategy parameters unchanged.",
        "No 528-universe strategy run. No parameter tuning from Phase 1 OOS.",
        "FundingRate from GET /fapi/v1/fundingRate is the settled interval rate at fundingTime, not the next predicted rate.",
        "OI public hist is latest ~30 days only (OI_HISTORICAL_DATA_LIMITATION). Nothing was fabricated."
    };
    var tfs = (timeframeRaw ?? "5m,15m,1h").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var dataDays = ParseInt(args, "--days", 90);
    var dataEnd = DateTimeOffset.UtcNow;
    var dataStart = dataEnd.AddDays(-dataDays);
    using var futHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
    futHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformFuturesData/1.0");
    var client = new BinanceFuturesHistoryClient(futHttp);
    var coverage = new List<DatasetCoverage>();
    foreach (var symbol in symbols)
    {
        try
        {
            var funding = await FuturesHistoryCache.LoadOrFetchFundingAsync(client, dataRoot, symbol, dataStart, dataEnd, force);
            coverage.Add(FuturesDataAudit.Coverage("Funding", symbol, "settlement", funding, x => x.FundingTime,
                "GET /fapi/v1/fundingRate settled rate at fundingTime. Align: fundingTime <= candle.CloseTime."));
            await FuturesHistoryCache.WriteCheckpointAsync(dataRoot, "Funding", symbol, "settlement", dataEnd);
            futNotes.Add($"{symbol} funding n={funding.Count}");
            Console.WriteLine(futNotes[^1]);
        }
        catch (Exception ex)
        {
            coverage.Add(new DatasetCoverage("Funding", symbol, "settlement", ResearchStatuses.ImplementationError, null, null, 0, ex.Message));
            futNotes.Add($"{symbol} funding failed: {ex.Message}");
        }

        foreach (var tf in tfs)
        {
            try
            {
                var mark = await FuturesHistoryCache.LoadOrFetchMarkAsync(client, dataRoot, symbol, tf, dataStart, dataEnd, force);
                var index = await FuturesHistoryCache.LoadOrFetchIndexAsync(client, dataRoot, symbol, tf, dataStart, dataEnd, force);
                var basis = await FuturesHistoryCache.WriteBasisAsync(dataRoot, symbol, tf, mark, index);
                coverage.Add(FuturesDataAudit.Coverage("MarkPrice", symbol, tf, mark, x => x.CloseTime,
                    "GET /fapi/v1/markPriceKlines close. Closed bar only."));
                coverage.Add(FuturesDataAudit.Coverage("IndexPrice", symbol, tf, index, x => x.CloseTime,
                    "GET /fapi/v1/indexPriceKlines close. pair=symbol for USDT-M."));
                coverage.Add(FuturesDataAudit.Coverage("Basis", symbol, tf, basis, x => x.CloseTime,
                    "Derived: (MarkClose-IndexClose)/IndexClose on matching closeTime. Not mixed with last-trade kline close."));
                await FuturesHistoryCache.WriteCheckpointAsync(dataRoot, "MarkPrice", symbol, tf, dataEnd);
                futNotes.Add($"{symbol} {tf} mark={mark.Count} index={index.Count} basis={basis.Count}");
                Console.WriteLine(futNotes[^1]);
            }
            catch (Exception ex)
            {
                coverage.Add(new DatasetCoverage("MarkPrice", symbol, tf, ResearchStatuses.ImplementationError, null, null, 0, ex.Message));
                futNotes.Add($"{symbol} {tf} mark/index failed: {ex.Message}");
            }

            try
            {
                var oi = await FuturesHistoryCache.LoadOrFetchOpenInterestAsync(client, dataRoot, symbol, tf, dataStart, dataEnd, force);
                var spanDays = oi.Count < 2 ? 0 : (oi[^1].Timestamp - oi[0].Timestamp).TotalDays;
                var coversWindow = spanDays + 2 >= dataDays;
                var status = oi.Count == 0 || !coversWindow
                    ? ResearchStatuses.DataUnavailable
                    : "AVAILABLE";
                var oiNote = $"{BinanceFuturesHistoryClient.OpenInterestLimitation}. GET /futures/data/openInterestHist latest ~30 days. Requested {dataDays}d; returned n={oi.Count} span={spanDays.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}d. Not fabricated.";
                coverage.Add(new DatasetCoverage(
                    "OpenInterest",
                    symbol,
                    tf,
                    status,
                    oi.Count == 0 ? null : oi[0].Timestamp,
                    oi.Count == 0 ? null : oi[^1].Timestamp,
                    oi.Count,
                    oiNote));
                futNotes.Add($"{symbol} {tf} oi n={oi.Count} {status}");
                Console.WriteLine(futNotes[^1]);
            }
            catch (Exception ex)
            {
                coverage.Add(new DatasetCoverage("OpenInterest", symbol, tf, ResearchStatuses.DataUnavailable, null, null, 0,
                    $"{BinanceFuturesHistoryClient.OpenInterestLimitation}. {ex.Message}"));
                futNotes.Add($"{symbol} {tf} oi failed: {ex.Message}");
            }
        }

        coverage.Add(new DatasetCoverage("Liquidation", symbol, "n/a", ResearchStatuses.DataUnavailable, null, null, 0,
            "No reliable public full-history liquidation series ingested. Not fabricated."));
        coverage.Add(new DatasetCoverage("CausalPairUniverse", symbol, "n/a", ResearchStatuses.DataUnavailable, null, null, 0,
            "EXTERNAL_DATA_SOURCE_REQUIRED. Not a Binance historical endpoint. Not fabricated."));
    }

    var phase1 = symbols.ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var symbol in symbols)
    {
        foreach (var tf in tfs)
        {
            var klinePath = Path.Combine(candleCacheDir, $"{symbol}_{tf}.json");
            var extracted = FuturesDataAudit.ExtractTaker(klinePath, symbol)
                .Where(x => x.CloseTime >= dataStart && x.CloseTime <= dataEnd)
                .ToList();
            await FuturesHistoryCache.WriteTakerAsync(dataRoot, symbol, tf, extracted);
            var valid = extracted.Count(x => x.Imbalance is not null);
            futNotes.Add($"{symbol} {tf} taker-cache n={extracted.Count} validImbalance={valid}");
            Console.WriteLine(futNotes[^1]);
        }
    }

    var taker = FuturesDataAudit.AuditTakerCache(candleCacheDir, phase1);
    coverage.AddRange(taker.Where(t => phase1.Contains(t.Symbol) || string.Equals(t.Symbol, "UNIVERSE", StringComparison.OrdinalIgnoreCase)));
    futNotes.Add($"Taker audit rows kept: {taker.Count(t => phase1.Contains(t.Symbol) || t.Symbol == "UNIVERSE")}. Full bar scan limited to Phase 1 coins.");

    var report = Path.Combine(root, "docs", "futures-data-availability-report.md");
    File.WriteAllText(report, FuturesDataAudit.RenderMarkdown(coverage, futNotes));
    var jsonPath = Path.Combine(root, "artifacts", "data", "futures-data-availability.json");
    FuturesDataAudit.WriteJson(jsonPath, coverage);
    Console.WriteLine($"Wrote {report}");
    Console.WriteLine($"Wrote {jsonPath}");
    Console.WriteLine("LIVE was not changed. Risk Engine was not changed. No strategy optimization.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--futures-alpha", StringComparison.OrdinalIgnoreCase)))
{
    var phase3Dir = Path.Combine(root, "artifacts", "strategy-research", "futures-alpha-phase3");
    Directory.CreateDirectory(phase3Dir);
    var phase3Notes = new List<string>
    {
        "Phase 3 futures-alpha pilot. LIVE disabled. Frozen five / Risk Engine unchanged. No 528-universe run.",
        "No OOS parameter tuning. No VALIDATED_FOR_PAPER. Taker flow not used (INSUFFICIENT_DATA).",
        "OI books use the last 29 days and are labeled OI_SAMPLE_LIMITED."
    };
    var p3Tfs = (timeframeRaw ?? "5m,15m,1h").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var p3Days = ParseInt(args, "--days", 90);
    var p3End = DateTimeOffset.UtcNow;
    var p3Start = p3End.AddDays(-p3Days);
    var dataRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
    using var p3Http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
    p3Http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformFuturesAlpha/1.0");
    var p3Series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    var p3Futures = new Dictionary<(string Symbol, string Timeframe), StrategyFuturesSeries>();
    var p3Funding = new Dictionary<string, IReadOnlyList<FundingPoint>>(StringComparer.OrdinalIgnoreCase);
    foreach (var symbol in symbols)
    {
        var funding = FuturesHistoryCache.ReadFunding(dataRoot, symbol, p3Start, p3End);
        p3Funding[symbol] = funding;
        phase3Notes.Add($"{symbol} funding n={funding.Count}");
        Console.WriteLine(phase3Notes[^1]);
        foreach (var tf in p3Tfs)
        {
            var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(p3Http, candleCacheDir, symbol, tf, p3Start, p3End);
            var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
            p3Series[(symbol, tf)] = closed;
            var mark = FuturesHistoryCache.ReadMark(dataRoot, symbol, tf, p3Start, p3End);
            var index = FuturesHistoryCache.ReadIndex(dataRoot, symbol, tf, p3Start, p3End);
            var basis = FuturesHistoryCache.ReadBasis(dataRoot, symbol, tf, p3Start, p3End);
            var oi = FuturesHistoryCache.ReadOpenInterest(dataRoot, symbol, tf, p3End.AddDays(-FuturesAlphaPilot.OiLookbackDays), p3End);
            p3Futures[(symbol, tf)] = FuturesHistoryCache.Align(closed, funding, oi, mark, index, basis);
            phase3Notes.Add($"{symbol} {tf}: bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got} mark={mark.Count} basis={basis.Count} oi={oi.Count}");
            Console.WriteLine(phase3Notes[^1]);
        }
    }

    var p3Books = FuturesAlphaPilot.Evaluate(symbols, p3Tfs, p3Series, p3Futures, p3Funding, p3End, maxParallel);
    var report = Path.Combine(root, "docs", "futures-alpha-phase3-report.md");
    File.WriteAllText(report, FuturesAlphaPilot.RenderMarkdown(p3Books, phase3Notes));
    var jsonPath = Path.Combine(root, "artifacts", "strategy-research", "futures-alpha-phase3-results.json");
    FuturesAlphaPilot.WriteJson(jsonPath, p3Books);
    File.WriteAllText(Path.Combine(phase3Dir, "books.json"), JsonSerializer.Serialize(p3Books));
    Console.WriteLine($"Wrote {report}");
    Console.WriteLine($"Wrote {jsonPath}");
    Console.WriteLine("LIVE was not changed. Risk Engine was not changed. No 528-universe run.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--futures-alpha-phase4-render", StringComparison.OrdinalIgnoreCase)))
{
    var art = Path.Combine(root, "artifacts", "strategy-research");
    var p4RenderBooks = JsonSerializer.Deserialize<List<ResearchBookResult>>(File.ReadAllText(Path.Combine(art, "futures-alpha-phase4-results.json"))) ?? [];
    var p4RenderTrades = JsonSerializer.Deserialize<List<Phase4TradeRow>>(File.ReadAllText(Path.Combine(art, "futures-alpha-phase4", "trades.json"))) ?? [];
    var notesPath = Path.Combine(art, "futures-alpha-phase4", "notes.json");
    var p4Report = Path.Combine(root, "docs", "futures-alpha-phase4-report.md");
    List<string> p4RenderNotes;
    if (File.Exists(notesPath))
    {
        p4RenderNotes = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(notesPath)) ?? [];
    }
    else if (File.Exists(p4Report))
    {
        p4RenderNotes = File.ReadAllLines(p4Report)
            .SkipWhile(l => l != "## Run notes")
            .Skip(1)
            .Where(l => l.StartsWith("- ", StringComparison.Ordinal))
            .Select(l => l[2..])
            .ToList();
    }
    else
    {
        p4RenderNotes = ["Phase 4 re-render from artifacts. LIVE disabled."];
    }
    var p4RenderBootstrap = FuturesAlphaPhase4.Bootstrap(p4RenderTrades);
    File.WriteAllText(p4Report, FuturesAlphaPhase4.RenderMarkdown(p4RenderBooks, p4RenderTrades, p4RenderNotes, p4RenderBootstrap));
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-walkforward.json"), FuturesAlphaPhase4.WalkForwardPayload(p4RenderBooks));
    Console.WriteLine($"Wrote {p4Report}");
    Console.WriteLine("LIVE was not changed. Risk Engine was not changed. No 528-universe run.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--futures-alpha-phase4", StringComparison.OrdinalIgnoreCase)))
{
    var phase4Dir = Path.Combine(root, "artifacts", "strategy-research", "futures-alpha-phase4");
    Directory.CreateDirectory(phase4Dir);
    var phase4Notes = new List<string>
    {
        "Phase 4 deep validation. LIVE disabled. Frozen five / Risk Engine unchanged. No 528-universe run.",
        "Frozen Phase 3 continuation parameters. No OOS tuning. No OI. No VALIDATED_FOR_PAPER.",
        $"Phase 3 tested {FuturesAlphaPhase4.Phase3EnhancedHypotheses} enhanced hypotheses; {FuturesAlphaPhase4.Phase3Survivors} entered Phase 4."
    };
    var p4Tfs = (timeframeRaw ?? "5m,15m,1h").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var p4Days = ParseInt(args, "--days", 730);
    var p4End = DateTimeOffset.UtcNow;
    var p4Start = p4End.AddDays(-p4Days);
    var dataRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
    Directory.CreateDirectory(dataRoot);
    using var p4Http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
    p4Http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformFuturesAlphaPhase4/1.0");
    var p4Client = new BinanceFuturesHistoryClient(p4Http);
    var p4Series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    var p4Futures = new Dictionary<(string Symbol, string Timeframe), StrategyFuturesSeries>();
    var p4Funding = new Dictionary<string, IReadOnlyList<FundingPoint>>(StringComparer.OrdinalIgnoreCase);
    DateTimeOffset? minFunding = null;
    DateTimeOffset? maxFunding = null;
    foreach (var symbol in symbols)
    {
        var funding = await FuturesHistoryCache.LoadOrFetchFundingAsync(p4Client, dataRoot, symbol, p4Start, p4End, force);
        p4Funding[symbol] = funding;
        if (funding.Count > 0)
        {
            minFunding = minFunding is { } a && a < funding[0].FundingTime ? a : funding[0].FundingTime;
            maxFunding = maxFunding is { } b && b > funding[^1].FundingTime ? b : funding[^1].FundingTime;
        }

        phase4Notes.Add($"{symbol} funding n={funding.Count} first={(funding.Count == 0 ? "n/a" : funding[0].FundingTime.UtcDateTime.ToString("yyyy-MM-dd"))} last={(funding.Count == 0 ? "n/a" : funding[^1].FundingTime.UtcDateTime.ToString("yyyy-MM-dd"))}");
        Console.WriteLine(phase4Notes[^1]);
        foreach (var tf in p4Tfs)
        {
            var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(p4Http, candleCacheDir, symbol, tf, p4Start, p4End);
            var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
            p4Series[(symbol, tf)] = closed;
            var mark = await FuturesHistoryCache.LoadOrFetchMarkAsync(p4Client, dataRoot, symbol, tf, p4Start, p4End, force);
            var index = await FuturesHistoryCache.LoadOrFetchIndexAsync(p4Client, dataRoot, symbol, tf, p4Start, p4End, force);
            var basis = await FuturesHistoryCache.WriteBasisAsync(dataRoot, symbol, tf, mark, index);
            p4Futures[(symbol, tf)] = FuturesHistoryCache.Align(closed, funding, oi: null, mark, index, basis);
            phase4Notes.Add($"{symbol} {tf}: bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got} mark={mark.Count} index={index.Count} basis={basis.Count} firstBar={(closed.Count == 0 ? "n/a" : closed[0].OpenTime.UtcDateTime.ToString("yyyy-MM-dd"))}");
            Console.WriteLine(phase4Notes[^1]);
        }
    }

    phase4Notes.Insert(0, $"COVERAGE requestedDays={p4Days} fundingSpan={(minFunding is { } f0 && maxFunding is { } f1 ? $"{f0.UtcDateTime:yyyy-MM-dd}..{f1.UtcDateTime:yyyy-MM-dd}" : "none")}. OI not loaded.");
    var (p4Books, p4Trades) = FuturesAlphaPhase4.Evaluate(symbols, p4Tfs, p4Series, p4Futures, p4Funding, maxParallel);
    var bootstrap = FuturesAlphaPhase4.Bootstrap(p4Trades);
    var report = Path.Combine(root, "docs", "futures-alpha-phase4-report.md");
    File.WriteAllText(report, FuturesAlphaPhase4.RenderMarkdown(p4Books, p4Trades, phase4Notes, bootstrap));
    var art = Path.Combine(root, "artifacts", "strategy-research");
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-results.json"), p4Books);
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-walkforward.json"), FuturesAlphaPhase4.WalkForwardPayload(p4Books));
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-symbols.json"), FuturesAlphaPhase4.SymbolsPayload(p4Books));
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-regimes.json"), FuturesAlphaPhase4.RegimePayload(p4Trades));
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-costs.json"), FuturesAlphaPhase4.CostPayload(p4Books));
    FuturesAlphaPhase4.WriteJson(Path.Combine(art, "futures-alpha-phase4-bootstrap.json"), bootstrap);
    File.WriteAllText(Path.Combine(phase4Dir, "trades.json"), JsonSerializer.Serialize(p4Trades));
    File.WriteAllText(Path.Combine(phase4Dir, "notes.json"), JsonSerializer.Serialize(phase4Notes));
    Console.WriteLine($"Wrote {report}");
    Console.WriteLine("LIVE was not changed. Risk Engine was not changed. No 528-universe run.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--advanced-six", StringComparison.OrdinalIgnoreCase)))
{
    var advDir = Path.Combine(root, "artifacts", "strategy-research", "advanced-six");
    Directory.CreateDirectory(advDir);
    var advNotes = new List<string>
    {
        "Advanced six pilot. LIVE disabled. Frozen five templates unchanged. No Paper/LIVE promotion.",
        $"Symbols {symbols.Length}. Timeframes {string.Join(",", isPhase2 || timeframeRaw is null ? new[] { "5m", "15m", "1h" } : timeframes)}.",
        "OI and funding historical series are not loaded; those two templates stay DATA_UNAVAILABLE."
    };
    var advTfs = (timeframeRaw ?? "5m,15m,1h").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var advDays = ParseInt(args, "--days", 90);
    var advEnd = DateTimeOffset.UtcNow;
    var advStart = advEnd.AddDays(-advDays);
    using var advHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(30) };
    advHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformAdvancedSix/1.0");
    var advSeries = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    foreach (var symbol in symbols)
    {
        foreach (var tf in advTfs)
        {
            IReadOnlyList<MarketCandle> candles = [];
            Exception? last = null;
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(advHttp, candleCacheDir, symbol, tf, advStart, advEnd);
                    candles = loaded;
                    advNotes.Add($"{symbol} {tf}: {loaded.Count} bars cache {(hit ? "hit" : "miss")} downloaded {got}");
                    Console.WriteLine(advNotes[^1]);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(400 * attempt);
                }
            }

            if (last is not null)
            {
                advNotes.Add($"{symbol} {tf}: load failed ({last.Message})");
                continue;
            }

            advSeries[(symbol, tf)] = candles;
        }
    }

    var advBooks = AdvancedSixPilot.Evaluate(symbols, advTfs, advSeries, maxParallel);
    var advMarkdown = AdvancedSixPilot.RenderMarkdown(advBooks, advNotes);
    var advReport = Path.Combine(root, "docs", "advanced-six-strategy-pilot.md");
    File.WriteAllText(advReport, advMarkdown);
    AdvancedSixPilot.WriteJson(Path.Combine(advDir, "advanced-six-strategy-pilot.json"), advBooks);
    File.WriteAllText(Path.Combine(root, "artifacts", "strategy-research", "advanced-six-strategy-pilot.json"), JsonSerializer.Serialize(advBooks, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Wrote {advReport}");
    Console.WriteLine($"Books {advBooks.Count}. LIVE was not changed.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--advanced-alpha", StringComparison.OrdinalIgnoreCase)))
{
    var alphaDir = Path.Combine(root, "artifacts", "strategy-research", "advanced-alpha");
    Directory.CreateDirectory(alphaDir);
    var alphaNotes = new List<string>
    {
        "Advanced alpha Phase 1 pilot. LIVE disabled. Frozen five and advanced six unchanged. No Paper/LIVE promotion.",
        "OI, funding, basis, pair-universe, and cross-section snapshots are not fabricated.",
        "Regime router is deferred and not fit on OOS."
    };
    var alphaTfs = (timeframeRaw ?? "5m,15m,1h").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var alphaDays = ParseInt(args, "--days", 90);
    var alphaEnd = DateTimeOffset.UtcNow;
    var alphaStart = alphaEnd.AddDays(-alphaDays);
    using var alphaHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(30) };
    alphaHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformAdvancedAlpha/1.0");
    var alphaSeries = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    foreach (var symbol in symbols)
    {
        foreach (var tf in alphaTfs)
        {
            IReadOnlyList<MarketCandle> candles = [];
            Exception? last = null;
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(alphaHttp, candleCacheDir, symbol, tf, alphaStart, alphaEnd);
                    candles = loaded;
                    alphaNotes.Add($"{symbol} {tf}: {loaded.Count} bars cache {(hit ? "hit" : "miss")} downloaded {got}");
                    Console.WriteLine(alphaNotes[^1]);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(400 * attempt);
                }
            }

            if (last is not null)
            {
                alphaNotes.Add($"{symbol} {tf}: load failed ({last.Message})");
                continue;
            }

            alphaSeries[(symbol, tf)] = candles;
        }
    }

    var alphaBooks = AdvancedAlphaPilot.Evaluate(symbols, alphaTfs, alphaSeries, maxParallel);
    var alphaMarkdown = AdvancedAlphaPilot.RenderMarkdown(alphaBooks, alphaNotes);
    var alphaReport = Path.Combine(root, "docs", "advanced-alpha-research-report.md");
    File.WriteAllText(alphaReport, alphaMarkdown);
    AdvancedAlphaPilot.WriteArtifacts(alphaDir, alphaBooks);
    File.WriteAllText(alphaReport, alphaMarkdown);
    Console.WriteLine($"Wrote {alphaReport}");
    Console.WriteLine($"Books {alphaBooks.Count}. LIVE was not changed. Full 528 universe was not launched.");
    return 0;
}

var end = DateTimeOffset.UtcNow;
var start = end.AddDays(-days);
var done = LoadDone(checkpointPath, resume && !force);
var notes = new List<string>
{
    $"Strategy research. LIVE disabled. Frozen catalog templates unchanged. Phase={phase}. IndicatorModel=B windows. MetricsVersion=fixed PF=W/|L|. CodeVersion={ResearchCandidate.EngineVersion}.",
    $"Checkpoint {outDir}. Candle cache {candleCacheDir}. MaxParallel={maxParallel}. Window {days} days. Timeframes {string.Join("/", timeframes)}.",
    "OOS is labeled and not used to retune candidates. This run does not freeze or promote VALIDATED_FOR_PAPER."
};

Console.WriteLine(notes[0]);
Console.WriteLine($"Candidates {candidates.Count}, symbols {symbols.Length}, timeframes {string.Join(",", timeframes)}, checkpoint keys {done.Count}.");

if (args.Any(a => string.Equals(a, "--report-only", StringComparison.OrdinalIgnoreCase)))
{
    var booksPath = Path.Combine(outDir, "candidate-results.json");
    if (!File.Exists(booksPath))
    {
        Console.WriteLine($"Missing {booksPath}. Run Phase 2 without --report-only first.");
        return 2;
    }

    var existing = JsonSerializer.Deserialize<List<ResearchBookResult>>(File.ReadAllText(booksPath)) ?? [];
    var rendered = ResearchReport.RenderMarkdown(candidates, existing, notes);
    File.WriteAllText(Path.Combine(outDir, "strategy-research-report.md"), rendered);
    File.WriteAllText(reportPath, rendered);
    Console.WriteLine($"Rewrote {reportPath} from {existing.Count} books. LIVE was not changed.");
    return 0;
}

using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformStrategyResearch/1.0");

var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
var htfNeeded = timeframes
    .SelectMany(tf => candidates.Select(c => ResearchRunner.ResolveHigherTimeframe(c, tf)))
    .Where(tf => !string.IsNullOrWhiteSpace(tf))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
var loadTfs = timeframes.Concat(htfNeeded).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
var sw = Stopwatch.StartNew();
foreach (var symbol in symbols)
{
    foreach (var tf in loadTfs)
    {
        IReadOnlyList<MarketCandle> candles = [];
        Exception? last = null;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(http, candleCacheDir, symbol, tf, start, end);
                candles = loaded;
                notes.Add($"{symbol} {tf}: {loaded.Count} bars cache {(hit ? "hit" : "miss")} downloaded {got}");
                Console.WriteLine(notes[^1]);
                last = null;
                break;
            }
            catch (Exception ex)
            {
                last = ex;
                await Task.Delay(400 * attempt);
            }
        }

        if (last is not null)
        {
            notes.Add($"{symbol} {tf}: load failed ({last.Message})");
            Console.WriteLine(notes[^1]);
            continue;
        }

        series[(symbol, tf)] = candles;
    }
}

var books = ResearchRunner.Evaluate(new ResearchRunRequest
{
    Phase = phase,
    Candidates = candidates,
    Symbols = symbols,
    Timeframes = timeframes,
    Series = series,
    Done = force ? [] : done,
    Force = force,
    MaxParallel = maxParallel
});

foreach (var row in books)
{
    done.Add($"{row.CandidateId}|{row.Symbol}|{row.Timeframe}|{row.Phase}|{row.CostLabel}");
}

File.WriteAllText(checkpointPath, JsonSerializer.Serialize(new Checkpoint { Done = done.ToArray(), CodeVersion = ResearchCandidate.EngineVersion, Phase = phase }));
ResearchReport.WriteJson(outDir, candidates, books);
var markdown = ResearchReport.RenderMarkdown(candidates, books, notes);
File.WriteAllText(Path.Combine(outDir, "strategy-research-report.md"), markdown);
File.WriteAllText(reportPath, markdown);
sw.Stop();
Console.WriteLine($"Wrote {reportPath}");
Console.WriteLine($"Books {books.Count}. Elapsed {sw.Elapsed}. LIVE was not changed.");
return 0;

static string? Arg(string[] args, string key)
{
    var i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

static int ParseInt(string[] args, string key, int fallback)
{
    var raw = Arg(args, key);
    return int.TryParse(raw, out var value) && value > 0 ? value : fallback;
}

static HashSet<string> LoadDone(string path, bool resume)
{
    if (!resume || !File.Exists(path))
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    try
    {
        var loaded = JsonSerializer.Deserialize<Checkpoint>(File.ReadAllText(path));
        if (loaded?.CodeVersion != ResearchCandidate.EngineVersion)
        {
            Console.WriteLine("Ignoring stale research checkpoint (CodeVersion mismatch).");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(loaded.Done ?? [], StringComparer.OrdinalIgnoreCase);
    }
    catch
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "TradingPlatform.slnx")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return Directory.GetCurrentDirectory();
}

sealed class Checkpoint
{
    public string[] Done { get; set; } = [];
    public string CodeVersion { get; set; } = ResearchCandidate.EngineVersion;
    public string Phase { get; set; } = ResearchPhases.Pilot;
}
