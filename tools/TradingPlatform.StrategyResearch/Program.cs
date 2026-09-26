using System.Diagnostics;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.News;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
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
if (args.Any(a => string.Equals(a, "--news-research", StringComparison.OrdinalIgnoreCase)))
{
    return await NewsResearch.RunAsync(root, Arg(args, "--symbol"), Arg(args, "--timeframe") ?? "1h", Arg(args, "--news-stage") ?? "A", CancellationToken.None);
}
if (args.Any(a => string.Equals(a, "--news-live", StringComparison.OrdinalIgnoreCase)))
{
    return await NewsLiveAnalyzer.RunAsync(root, CancellationToken.None);
}
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

if (args.Any(a => string.Equals(a, "--imported-rules", StringComparison.OrdinalIgnoreCase)))
{
    return await ImportedRuleMeasure.RunAsync(candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--price-action-alpha-audit", StringComparison.OrdinalIgnoreCase)))
{
    return await PriceActionAlphaData.AuditAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--price-action-alpha-expand", StringComparison.OrdinalIgnoreCase)))
{
    return await PriceActionAlphaData.ExpandAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--price-action-alpha-freeze", StringComparison.OrdinalIgnoreCase)))
{
    return PriceActionAlphaDiscovery.Freeze(root);
}

if (args.Any(a => string.Equals(a, "--price-action-alpha-discover", StringComparison.OrdinalIgnoreCase)))
{
    return await PriceActionAlphaDiscovery.RunAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--price-action-alpha-render", StringComparison.OrdinalIgnoreCase)))
{
    return PriceActionAlphaDiscovery.Render(root);
}

if (args.Any(a => string.Equals(a, "--contextual-pa-freeze", StringComparison.OrdinalIgnoreCase)))
{
    return ContextualPriceActionDiscovery.Freeze(root);
}

if (args.Any(a => string.Equals(a, "--cross-section", StringComparison.OrdinalIgnoreCase)))
{
    return await CrossSectionAudit.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--microstructure", StringComparison.OrdinalIgnoreCase)))
{
    return await MicrostructureAudit.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--signal-quality", StringComparison.OrdinalIgnoreCase)))
{
    return await SignalQualityAudit.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--trade-failure", StringComparison.OrdinalIgnoreCase)))
{
    return await TradeFailureAnalysis.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--edge-discovery", StringComparison.OrdinalIgnoreCase)))
{
    return await EdgeDiscovery.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--extreme-move", StringComparison.OrdinalIgnoreCase)))
{
    return await ExtremeMoveStudy.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--extreme-move-signals", StringComparison.OrdinalIgnoreCase)))
{
    return await ExtremeMoveSignals.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--exhaustion-path", StringComparison.OrdinalIgnoreCase)))
{
    return ExhaustionPathStudy.Run(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--ts-momentum", StringComparison.OrdinalIgnoreCase)))
{
    return TimeSeriesMomentumStudy.Run(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--btc-daily-max", StringComparison.OrdinalIgnoreCase)))
{
    return BtcDailyMaxStudy.Run(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--flat-range", StringComparison.OrdinalIgnoreCase)))
{
    return FlatRangeStudy.Run(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--flat-now", StringComparison.OrdinalIgnoreCase)))
{
    return await FlatRangeStudy.ScanNow(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--flat-risk", StringComparison.OrdinalIgnoreCase)))
{
    return FlatRangeStudy.RunRisk(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--combined-trader", StringComparison.OrdinalIgnoreCase)))
{
    return CombinedTrader.Run(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--liquid-reversal", StringComparison.OrdinalIgnoreCase)))
{
    return CombinedTrader.RunLiquid(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--fit-sweep", StringComparison.OrdinalIgnoreCase)))
{
    return FitSweep.Run(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--btc-hf", StringComparison.OrdinalIgnoreCase)))
{
    return await BtcHfSearch.RunAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--btc-hedge", StringComparison.OrdinalIgnoreCase)))
{
    return await BtcHfSearch.RunHedgeAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--ema-cross-top", StringComparison.OrdinalIgnoreCase)))
{
    return await BtcHfSearch.RunTopAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--ema-cross-universe", StringComparison.OrdinalIgnoreCase)))
{
    return await BtcHfSearch.RunUniverseAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--final-five", StringComparison.OrdinalIgnoreCase)))
{
    return await FinalFiveDiscovery.RunAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--contextual-pa-discover", StringComparison.OrdinalIgnoreCase)))
{
    return await ContextualPriceActionDiscovery.RunAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--contextual-pa-render", StringComparison.OrdinalIgnoreCase)))
{
    return ContextualPriceActionDiscovery.Render(root);
}

if (args.Any(a => string.Equals(a, "--phase7-data-audit", StringComparison.OrdinalIgnoreCase)))
{
    return await Phase7DataExpansion.AuditAsync(root, candleCacheDir);
}

if (args.Any(a => string.Equals(a, "--phase7-data-expand", StringComparison.OrdinalIgnoreCase)))
{
    return await Phase7DataExpansion.ExpandAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--price-action-data", StringComparison.OrdinalIgnoreCase)))
{
    return await PriceActionCli.RunDataAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--price-action", StringComparison.OrdinalIgnoreCase)))
{
    return await PriceActionCli.RunResearchAsync(root, candleCacheDir, args);
}

if (args.Any(a => string.Equals(a, "--scalping-data", StringComparison.OrdinalIgnoreCase)))
{
    var scalpDir = Path.Combine(root, "artifacts", "strategy-research", "scalping");
    Directory.CreateDirectory(scalpDir);
    Console.WriteLine("Scalping coverage. LIVE disabled. Isolated LOW unchanged. Taker/OI/funding not fabricated.");
    using var scalpHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
    scalpHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformScalpingCoverage/1.0");
    var rows = await ScalpingCoverage.RunAsync(scalpHttp, candleCacheDir, scalpDir);
    File.WriteAllText(Path.Combine(root, "docs", "scalping-coverage.md"), ScalpingReport.CoverageMarkdown(rows));
    Console.WriteLine($"Coverage rows {rows.Count}. Wrote {Path.Combine(scalpDir, "coverage.json")}");
    Console.WriteLine(ScalpingReport.Confirmation);
    return 0;
}

if (args.Any(a => string.Equals(a, "--scalping", StringComparison.OrdinalIgnoreCase)))
{
    var scalpDir = Path.Combine(root, "artifacts", "strategy-research", "scalping");
    Directory.CreateDirectory(scalpDir);
    var scalpSymbols = string.IsNullOrWhiteSpace(symbolFilter) ? ScalpingCatalog.Universe : symbols;
    var scalpTfs = (timeframeRaw ?? "5m,15m").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var scalpCandidates = string.IsNullOrWhiteSpace(candidateId)
        ? ResearchRegistry.Scalping.ToList()
        : ResearchRegistry.Scalping.Where(c => string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.ParentTemplateKey, candidateId, StringComparison.OrdinalIgnoreCase)).ToList();
    var scalpNotes = new List<string>
    {
        ScalpingReport.Confirmation,
        "Scalping research wave. LIVE disabled. Frozen five / Isolated LOW catalog numbers unchanged. No OperatorCatalog. No VALIDATED_FOR_PAPER.",
        "Book: LOW Isolated $1000, 0.5% R, 3x, 0.04% fee, 0.02% slip. MaxHoldBars per TF (1m=15, 3m=12, 5m=8, 15m=6).",
        "Futures scalp keys skip until coverage supplies taker/OI/funding. Missing = DATA_UNAVAILABLE.",
        $"Coins {string.Join(",", scalpSymbols)}. Timeframes {string.Join("/", scalpTfs)}. Candidates {scalpCandidates.Count}."
    };
    Console.WriteLine(scalpNotes[0]);
    using var scalpHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
    scalpHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformScalpingResearch/1.0");
    var coveragePath = Path.Combine(scalpDir, "coverage.json");
    IReadOnlyList<ScalpingCoverageRow> coverage = [];
    if (File.Exists(coveragePath))
    {
        coverage = JsonSerializer.Deserialize<List<ScalpingCoverageRow>>(File.ReadAllText(coveragePath), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
        scalpNotes.Add($"Loaded coverage rows={coverage.Count} from {coveragePath}.");
    }
    else
    {
        coverage = await ScalpingCoverage.RunAsync(scalpHttp, candleCacheDir, scalpDir);
        scalpNotes.Add($"Coverage generated rows={coverage.Count}.");
    }

    var scalpEnd = DateTimeOffset.UtcNow;
    var scalpSeries = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    foreach (var symbol in scalpSymbols)
    {
        foreach (var tf in scalpTfs.Concat(["15m"]).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Exception? last = null;
            var windowStart = ScalpingCoverage.WindowStart(tf, scalpEnd);
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(scalpHttp, candleCacheDir, symbol, tf, windowStart, scalpEnd);
                    var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                    const int scalpBarCap = 6000;
                    if (closed.Count > scalpBarCap)
                    {
                        scalpNotes.Add($"{symbol} {tf} truncated to last {scalpBarCap} of {closed.Count} bars for bounded v1 (not a 528-universe run).");
                        closed = closed.TakeLast(scalpBarCap).ToList();
                    }

                    scalpSeries[(symbol, tf)] = closed;
                    scalpNotes.Add($"{symbol} {tf} bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got}");
                    Console.WriteLine(scalpNotes[^1]);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(250 * attempt);
                }
            }

            if (last is not null)
            {
                scalpNotes.Add($"{symbol} {tf} load failed ({last.Message})");
                Console.WriteLine(scalpNotes[^1]);
            }
        }
    }

    var scalpBooks = ResearchRunner.Evaluate(new ResearchRunRequest
    {
        Phase = ResearchPhases.Pilot,
        Candidates = scalpCandidates,
        Symbols = scalpSymbols,
        Timeframes = scalpTfs,
        Series = scalpSeries,
        CostLabels = [ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress],
        Force = true,
        MaxParallel = maxParallel,
        UseLowIsolated = true,
        HonorSuggestedStops = false,
        SkipWalkForward = true,
        DataSnapshot = new ResearchDataSnapshot(["OHLCV", "CompletedHtf"])
    });
    if (scalpBooks.Any(b => string.Equals(b.Status, ResearchStatuses.ValidatedForPaper, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("AssignStatus must never return VALIDATED_FOR_PAPER.");
    }

    OccupancyReplayResult? occupancy = null;
    try
    {
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow;
        var occSettings = StrategyValidation.LowIsolatedRisk(from, to) with { MaxSimultaneousPositions = 5, MaxHoldBars = 8 };
        var occBooks = new List<(string StrategyKey, string Symbol, IReadOnlyList<MarketCandle> Candles, IReadOnlyList<TradingPlatform.Domain.Trading.SignalType> Signals)>();
        foreach (var symbol in scalpSymbols.Take(3))
        {
            foreach (var key in new[] { StrategyTemplateKeys.ScalpEmaMomentum, StrategyTemplateKeys.ScalpRsiPullback })
            {
                if (!scalpSeries.TryGetValue((symbol, "5m"), out var candles) || candles.Count < 40)
                {
                    continue;
                }

                candles = candles.TakeLast(400).ToList();

                var candidate = scalpCandidates.First(c => c.ParentTemplateKey == key);
                var engine = new ResearchStrategyEngine(candidate);
                var definition = ResearchRunner.DefinitionFor(candidate, "5m");
                var cache = new CausalIndicatorCache(candles);
                var signals = new SignalType[candles.Count];
                for (var i = 1; i < candles.Count; i++)
                {
                    var ctx = new StrategyContext
                    {
                        ClosedCandles = candles.Take(i + 1).ToList(),
                        CurrentPrice = candles[i].Close,
                        HasOpenPosition = false
                    };
                    signals[i] = engine.EvaluateAt(definition, ctx, cache, i, out _);
                }

                occBooks.Add((key, symbol, candles, signals));
            }
        }

        if (occBooks.Count > 0)
        {
            occupancy = PortfolioOccupancyReplay.RunBooks(occBooks, occSettings);
            scalpNotes.Add($"Occupancy same-coin={occupancy.SameCoinRejects} slots={occupancy.SlotRejects} heat={occupancy.HeatRejects} trades={occupancy.Trades.Count} DD={occupancy.MaximumDrawdownPercent:0.00}%.");
            File.WriteAllText(Path.Combine(scalpDir, "occupancy.json"), JsonSerializer.Serialize(occupancy, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    catch (Exception ex)
    {
        scalpNotes.Add($"Occupancy replay skipped ({ex.Message}).");
    }

    var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
    var summary = new
    {
        id = runId,
        confirmation = ScalpingReport.Confirmation,
        liveOff = true,
        scalpingLiveOff = true,
        books = scalpBooks,
        coverage,
        occupancyRejects = occupancy?.Rejects ?? [],
        sameCoinRejects = occupancy?.SameCoinRejects ?? 0,
        slotRejects = occupancy?.SlotRejects ?? 0,
        heatRejects = occupancy?.HeatRejects ?? 0
    };
    File.WriteAllText(Path.Combine(scalpDir, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    File.WriteAllText(Path.Combine(scalpDir, "books.json"), JsonSerializer.Serialize(scalpBooks));
    Directory.CreateDirectory(Path.Combine(scalpDir, "runs"));
    File.WriteAllText(Path.Combine(scalpDir, "runs", $"{runId}.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    var report = Path.Combine(root, "docs", "SCALPING_RESEARCH_REPORT.md");
    File.WriteAllText(report, ScalpingReport.Render(scalpCandidates, scalpBooks, coverage, occupancy, scalpNotes));
    Console.WriteLine($"Wrote {report}");
    Console.WriteLine($"Books {scalpBooks.Count}.");
    Console.WriteLine(ScalpingReport.Confirmation);
    return 0;
}

if (args.Any(a => string.Equals(a, "--btc15m-fit", StringComparison.OrdinalIgnoreCase)))
{
    var wStart = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
    var wEnd = new DateTimeOffset(2026, 9, 19, 21, 15, 0, TimeSpan.Zero);
    Console.WriteLine("BTCUSDT 15m historically fitted search. LIVE off. Registry unchanged.");
    using var btcFitHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
    btcFitHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformBtc15mFit/1.0");
    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(btcFitHttp, candleCacheDir, "BTCUSDT", "15m", wStart, wEnd);
    var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
    Console.WriteLine($"BTCUSDT 15m bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got} first={closed[0].OpenTime:u} last={closed[^1].CloseTime:u}");
    var fit = Btc15mFit.Run(closed);
    var reportPathBtc = Path.Combine(root, "docs", "btc-2y-15m-fitted-strategy-report.md");
    var frozenPath = Path.Combine(root, "docs", "btc-2y-15m-fitted-strategy-frozen.json");
    File.WriteAllText(reportPathBtc, Btc15mFitReport.Render(fit, closed));
    File.WriteAllText(frozenPath, Btc15mFitReport.FrozenJson(fit));
    Console.WriteLine($"Wrote {reportPathBtc}");
    Console.WriteLine($"Wrote {frozenPath}");
    Console.WriteLine($"class={fit.Classification} combos={fit.CombinationsTested} feasible={fit.AllFeasible.Count}");
    Console.WriteLine("LIVE was not changed. Isolated LOW was not changed. Registry was not changed. No VALIDATED_FOR_PAPER.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--wave6", StringComparison.OrdinalIgnoreCase)))
{
    var wave6Dir = Path.Combine(root, "artifacts", "strategy-research", "wave-6");
    Directory.CreateDirectory(wave6Dir);
    var w6Start = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
    var w6End = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    var w6Notes = new List<string>
    {
        "Wave-6 trade-path. Existing signals only. No new entries. No router. LIVE disabled. Isolated LOW unchanged.",
        $"Window {w6Start:yyyy-MM-dd} → {w6End:yyyy-MM-dd}. Timeframes 5m/15m/1h. Coins {string.Join(",", symbols)}.",
        "Path labels: MFE/MAE/first-touch. 1R = 2% slipped entry. 2%/4% is not the path exit.",
        "Random entry: same coin/TF/phase/long-short counts, signal fills excluded, seed 42."
    };
    Console.WriteLine(w6Notes[0]);
    var w6Universe = Wave5Catalog.RouterUniverse();
    w6Notes.Add($"Strategy universe {w6Universe.Count}.");
    using var w6Http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
    w6Http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformWave6Research/1.0");
    var w6Series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    foreach (var tf in new[] { "5m", "15m", "1h" })
    {
        foreach (var symbol in symbols)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(w6Http, candleCacheDir, symbol, tf, w6Start, w6End);
                    var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                    w6Series[(symbol, tf)] = closed;
                    w6Notes.Add($"{symbol} {tf} bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got}");
                    Console.WriteLine(w6Notes[^1]);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(300 * attempt);
                }
            }

            if (last is not null)
            {
                w6Notes.Add($"{symbol} {tf} load failed ({last.Message})");
                Console.WriteLine(w6Notes[^1]);
            }
        }
    }

    var w6Events = new List<Wave5Event>();
    foreach (var tf in new[] { "5m", "15m", "1h" })
    {
        Console.WriteLine($"Harvest {tf}...");
        var slice = Wave5Harvest.Harvest(w6Universe, w6Series, tf, symbols);
        w6Events.AddRange(slice);
        w6Notes.Add($"Harvest {tf}: {slice.Count} events.");
        Console.WriteLine(w6Notes[^1]);
    }

    Console.WriteLine($"Path labels for {w6Events.Count} events...");
    var w6Result = Wave6Eval.Evaluate(w6Events, w6Series);
    var w6Report = Path.Combine(root, "docs", "strategy-research-wave6-trade-path-report.md");
    File.WriteAllText(w6Report, Wave6Report.Render(w6Result, w6Notes));
    Wave6Report.WriteArtifacts(wave6Dir, w6Result);
    Console.WriteLine($"Wrote {w6Report} class={w6Result.Classification} paths={w6Result.SignalCount}");
    Console.WriteLine("LIVE was not changed. Isolated LOW was not changed. No VALIDATED_FOR_PAPER.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--wave5", StringComparison.OrdinalIgnoreCase)))
{
    var wave5Dir = Path.Combine(root, "artifacts", "strategy-research", "wave-5");
    Directory.CreateDirectory(wave5Dir);
    var w5Start = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
    var w5End = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    var w5Notes = new List<string>
    {
        "Wave-5 router. Existing signals only. LIVE disabled. Isolated LOW $1000 / 0.5% / 3x unchanged.",
        $"Window {w5Start:yyyy-MM-dd} → {w5End:yyyy-MM-dd}. Timeframes 5m/15m/1h. Coins {string.Join(",", symbols)}.",
        "Harvest skips a second same-strategy same-coin signal while the Isolated book is still in the prior simulated trade (causal occupancy).",
        "Funding/OI not joined (Wave-4 Vision OI exists but was not attached to 5m/15m events). Liquidations DATA_UNAVAILABLE.",
        "Full 528-coin 5m harvest not loaded (cache JSON size). Router experiment is 10 liquid coins × 3 timeframes."
    };
    Console.WriteLine(w5Notes[0]);
    var universe = Wave5Catalog.RouterUniverse();
    w5Notes.Add($"Strategy universe {universe.Count}.");
    Console.WriteLine(w5Notes[^1]);
    using var w5Http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
    w5Http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformWave5Research/1.0");
    var w5Series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    foreach (var tf in new[] { "5m", "15m", "1h" })
    {
        foreach (var symbol in symbols)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(w5Http, candleCacheDir, symbol, tf, w5Start, w5End);
                    var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                    w5Series[(symbol, tf)] = closed;
                    w5Notes.Add($"{symbol} {tf} bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got}");
                    Console.WriteLine(w5Notes[^1]);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    await Task.Delay(300 * attempt);
                }
            }

            if (last is not null)
            {
                w5Notes.Add($"{symbol} {tf} load failed ({last.Message})");
                Console.WriteLine(w5Notes[^1]);
            }
        }
    }

    var events = new List<Wave5Event>();
    foreach (var tf in new[] { "5m", "15m", "1h" })
    {
        Console.WriteLine($"Harvest {tf}...");
        var slice = Wave5Harvest.Harvest(universe, w5Series, tf, symbols);
        events.AddRange(slice);
        w5Notes.Add($"Harvest {tf}: {slice.Count} events.");
        Console.WriteLine(w5Notes[^1]);
    }

    Console.WriteLine($"Scoring {events.Count} events...");
    var w5Result = Wave5Router.Evaluate(universe, events);
    var w5Report = Path.Combine(root, "docs", "strategy-research-wave5-router-report.md");
    File.WriteAllText(w5Report, Wave5Report.Render(w5Result, w5Notes));
    Wave5Report.WriteArtifacts(wave5Dir, w5Result);
    Console.WriteLine($"Wrote {w5Report} class={w5Result.Classification} events={events.Count}");
    Console.WriteLine("LIVE was not changed. Isolated LOW was not changed. No VALIDATED_FOR_PAPER.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--wave4-rerender", StringComparison.OrdinalIgnoreCase)))
{
    var wave4Dir = Path.Combine(root, "artifacts", "strategy-research", "wave-4");
    var jsonPath = Path.Combine(wave4Dir, "wave4-signals.json");
    var loaded = JsonSerializer.Deserialize<Wave4SignalResult>(File.ReadAllText(jsonPath), new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
    }) ?? throw new InvalidOperationException($"Missing {jsonPath}");
    var w4Report = Path.Combine(root, "docs", "strategy-research-wave4-report.md");
    File.WriteAllText(w4Report, Wave4Report.Render(loaded, ["Re-rendered classifications from cached Wave-4 artifacts. No re-download."]));
    Console.WriteLine($"Wrote {w4Report}");
    return 0;
}

if (args.Any(a => string.Equals(a, "--wave4", StringComparison.OrdinalIgnoreCase)))
{
    var wave4Dir = Path.Combine(root, "artifacts", "strategy-research", "wave-4");
    var klineDir = Path.Combine(wave4Dir, "klines");
    var visionDir = Path.Combine(wave4Dir, "vision", "metrics");
    Directory.CreateDirectory(klineDir);
    Directory.CreateDirectory(visionDir);
    var w4Start = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
    var w4End = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    var w4Notes = new List<string>
    {
        "Wave-4 information research. LIVE disabled. Frozen five / FrozenRisk / Isolated LOW unchanged.",
        "Existing strategy-validation-cache was not overwritten. Taker klines written to artifacts/strategy-research/wave-4/klines.",
        $"Window {w4Start:yyyy-MM-dd} → {w4End:yyyy-MM-dd}. Panel 1h inner-join. Coins {string.Join(",", symbols)}."
    };
    Console.WriteLine(w4Notes[0]);
    using var w4Http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
    w4Http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformWave4Research/1.0");
    using var visionHttp = new HttpClient { BaseAddress = new Uri("https://data.binance.vision/"), Timeout = TimeSpan.FromSeconds(120) };
    visionHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformWave4Research/1.0");

    var bySymbol = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
    foreach (var symbol in symbols)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(
                    w4Http, klineDir, symbol, "1h", w4Start, w4End, requireTaker: true);
                var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                bySymbol[symbol] = closed;
                var takerN = closed.Count(c => c.TakerBuyVolume > 0m && c.TakerBuyVolume <= c.Volume);
                w4Notes.Add($"{symbol} 1h bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got} takerOk {takerN}/{closed.Count} ({ResearchKlineCache.TakerCoverage(closed):P1})");
                Console.WriteLine(w4Notes[^1]);
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
            w4Notes.Add($"{symbol} 1h load failed ({last.Message})");
            Console.WriteLine(w4Notes[^1]);
        }
    }

    var panel = Wave3Panel.Align(bySymbol, symbols);
    w4Notes.Add($"Aligned panel rows={panel.Length} coins={panel.Width} first={panel.OpenTimes[0]:u} last={panel.OpenTimes[^1]:u}");
    Console.WriteLine(w4Notes[^1]);

    Dictionary<string, IReadOnlyList<FundingPoint>>? fundingMap = null;
    try
    {
        var dataRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
        var client = new BinanceFuturesHistoryClient(w4Http);
        fundingMap = new Dictionary<string, IReadOnlyList<FundingPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            var rows = await FuturesHistoryCache.LoadOrFetchFundingAsync(client, dataRoot, symbol, w4Start, w4End, force: false);
            fundingMap[symbol] = rows;
            w4Notes.Add($"{symbol} funding n={rows.Count}");
            Console.WriteLine(w4Notes[^1]);
        }
    }
    catch (Exception ex)
    {
        fundingMap = null;
        w4Notes.Add($"Funding load failed ({ex.Message}). Not fabricated.");
        Console.WriteLine(w4Notes[^1]);
    }

    var vision = new BinanceVisionClient(visionHttp);
    var metricsMap = new Dictionary<string, IReadOnlyList<VisionMetricsPoint>>(StringComparer.OrdinalIgnoreCase);
    var visionGate = new SemaphoreSlim(6);
    await Task.WhenAll(symbols.Select(async symbol =>
    {
        await visionGate.WaitAsync();
        try
        {
            var rows = await vision.LoadMetricsAsync(symbol, w4Start, w4End, visionDir);
            lock (metricsMap)
            {
                metricsMap[symbol] = rows;
            }

            var line = $"{symbol} vision metrics n={rows.Count} first={(rows.Count == 0 ? "n/a" : rows[0].CreateTime.ToString("u"))} last={(rows.Count == 0 ? "n/a" : rows[^1].CreateTime.ToString("u"))}";
            lock (w4Notes)
            {
                w4Notes.Add(line);
            }

            Console.WriteLine(line);
        }
        catch (Exception ex)
        {
            lock (w4Notes)
            {
                w4Notes.Add($"{symbol} vision metrics failed ({ex.Message}). Not fabricated.");
            }

            Console.WriteLine($"{symbol} vision metrics failed ({ex.Message})");
        }
        finally
        {
            visionGate.Release();
        }
    }));

    double[,]? oi = null;
    double[,]? oiValue = null;
    double[,]? lsRatio = null;
    double[,]? topLs = null;
    double[,]? takerLs = null;
    if (metricsMap.Count > 0)
    {
        oi = BinanceVisionClient.AlignToPanel(panel, metricsMap, p => (double)p.SumOpenInterest);
        oiValue = BinanceVisionClient.AlignToPanel(panel, metricsMap, p => (double)p.SumOpenInterestValue);
        lsRatio = BinanceVisionClient.AlignToPanel(panel, metricsMap, p => BinanceVisionClient.AsDouble(p.CountLongShortRatio));
        topLs = BinanceVisionClient.AlignToPanel(panel, metricsMap, p => BinanceVisionClient.AsDouble(p.SumTopTraderLongShortRatio));
        takerLs = BinanceVisionClient.AlignToPanel(panel, metricsMap, p => BinanceVisionClient.AsDouble(p.SumTakerLongShortVolRatio));
    }

    IReadOnlyList<Wave3IcRow> universeIc = [];
    if (!args.Any(a => string.Equals(a, "--skip-universe", StringComparison.OrdinalIgnoreCase)))
    {
        try
        {
            var slim = new Dictionary<string, IReadOnlyList<(DateTimeOffset Open, decimal Close, decimal Volume)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.GetFiles(candleCacheDir, "*_1h.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var symbolName = name.EndsWith("_1h", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;
                await using var stream = File.OpenRead(file);
                var bars = await JsonSerializer.DeserializeAsync<List<ResearchKlineCache.CachedBar>>(stream, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? [];
                var sliced = bars
                    .Where(b => b.OpenTime >= w4Start && b.OpenTime <= w4End)
                    .Select(b => (b.OpenTime, b.Close, b.Volume))
                    .OrderBy(b => b.OpenTime)
                    .ToList();
                if (sliced.Count >= 400)
                {
                    slim[symbolName] = sliced;
                }
            }

            w4Notes.Add($"Universe 1h OHLCV series loaded={slim.Count} from {candleCacheDir} (taker not required).");
            Console.WriteLine(w4Notes[^1]);
            universeIc = Wave4UniverseResearch.Evaluate(slim);
            w4Notes.Add($"Universe IC rows={universeIc.Count}.");
            Console.WriteLine(w4Notes[^1]);
        }
        catch (Exception ex)
        {
            w4Notes.Add($"Universe panel skipped ({ex.Message}). Not fabricated.");
            Console.WriteLine(w4Notes[^1]);
        }
    }
    else
    {
        w4Notes.Add("Universe panel skipped (--skip-universe).");
    }

    var coverage = new List<Wave4CoverageNote>
    {
        new("TAKER_KLINES", "GET /fapi/v1/klines field 9", "ACQUIRED", $"versioned cache {klineDir}"),
        new("VISION_METRICS", BinanceVisionClient.Source, metricsMap.Count == symbols.Length ? "ACQUIRED" : "PARTIAL", $"symbols={metricsMap.Count} dir={visionDir}"),
        new("LIQUIDATION", "Vision um/daily/liquidationSnapshot", "DATA_UNAVAILABLE", "prefix empty; Binance no longer provides USD-M snapshots"),
        new("DEPTH", "Vision um/daily/bookDepth", "ARCHIVE_AVAILABLE_NOT_INGESTED", "exists from 2023; not downloaded this wave")
    };

    var w4Result = Wave4SignalResearch.Evaluate(panel, oi, oiValue, lsRatio, topLs, takerLs, fundingMap, universeIc) with
    {
        Coverage = coverage
    };
    var w4Report = Path.Combine(root, "docs", "strategy-research-wave4-report.md");
    File.WriteAllText(w4Report, Wave4Report.Render(w4Result, w4Notes));
    Wave4Report.WriteArtifacts(wave4Dir, w4Result);
    Console.WriteLine($"Wrote {w4Report}");
    Console.WriteLine("LIVE was not changed. Isolated LOW was not changed. 1,584-book validation was not launched.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--wave3", StringComparison.OrdinalIgnoreCase)))
{
    var wave3Dir = Path.Combine(root, "artifacts", "strategy-research", "wave-3");
    Directory.CreateDirectory(wave3Dir);
    var w3Start = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
    var w3End = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    var w3Notes = new List<string>
    {
        "Wave-3 Stage 1 signal research. LIVE disabled. Frozen five / FrozenRisk / Isolated LOW unchanged.",
        "No strategy promotion. No 528-universe run. OOS not used to mutate lookbacks.",
        $"Window {w3Start:yyyy-MM-dd} → {w3End:yyyy-MM-dd}. Panel 1h inner-join. Coins {string.Join(",", symbols)}."
    };
    Console.WriteLine(w3Notes[0]);
    using var w3Http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
    w3Http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformWave3Research/1.0");
    var bySymbol = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
    foreach (var symbol in symbols)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            try
            {
                var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(w3Http, candleCacheDir, symbol, "1h", w3Start, w3End);
                var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                bySymbol[symbol] = closed;
                var takerN = closed.Count(c => c.TakerBuyVolume > 0m);
                w3Notes.Add($"{symbol} 1h bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got} takerBuy>0 {takerN}");
                Console.WriteLine(w3Notes[^1]);
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
            w3Notes.Add($"{symbol} 1h load failed ({last.Message})");
        }
    }

    var panel = Wave3Panel.Align(bySymbol, symbols);
    w3Notes.Add($"Aligned panel rows={panel.Length} coins={panel.Width} first={panel.OpenTimes[0]:u} last={panel.OpenTimes[^1]:u}");
    Console.WriteLine(w3Notes[^1]);

    Dictionary<string, IReadOnlyList<FundingPoint>>? fundingMap = null;
    try
    {
        var dataRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
        Directory.CreateDirectory(dataRoot);
        var client = new BinanceFuturesHistoryClient(w3Http);
        fundingMap = new Dictionary<string, IReadOnlyList<FundingPoint>>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            var rows = await FuturesHistoryCache.LoadOrFetchFundingAsync(client, dataRoot, symbol, w3Start, w3End, force: false);
            fundingMap[symbol] = rows;
            w3Notes.Add($"{symbol} funding n={rows.Count} first={(rows.Count == 0 ? "n/a" : rows[0].FundingTime.ToString("u"))} last={(rows.Count == 0 ? "n/a" : rows[^1].FundingTime.ToString("u"))}");
            Console.WriteLine(w3Notes[^1]);
        }
    }
    catch (Exception ex)
    {
        fundingMap = null;
        w3Notes.Add($"Funding load failed ({ex.Message}). H_FUNDING skipped. Not fabricated.");
        Console.WriteLine(w3Notes[^1]);
    }

    var w3Result = Wave3SignalResearch.Evaluate(panel, fundingMap);
    var w3Report = Path.Combine(root, "docs", "strategy-research-wave3-report.md");
    File.WriteAllText(w3Report, Wave3Report.Render(w3Result, w3Notes));
    Wave3Report.WriteArtifacts(wave3Dir, w3Result);
    Console.WriteLine($"Wrote {w3Report}");
    Console.WriteLine("LIVE was not changed. Isolated LOW was not changed. 528-universe was not launched.");
    return 0;
}

if (args.Any(a => string.Equals(a, "--wave2", StringComparison.OrdinalIgnoreCase)))
{
    var waveDir = Path.Combine(root, "artifacts", "strategy-research", "wave-2");
    Directory.CreateDirectory(waveDir);
    var waveStart = new DateTimeOffset(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);
    var waveEnd = new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    var waveTfs = (timeframeRaw ?? "1h,15m").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var waveCandidates = string.IsNullOrWhiteSpace(candidateId)
        ? ResearchRegistry.Wave2.ToList()
        : ResearchRegistry.Wave2.Where(c => string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase)).ToList();
    var waveNotes = new List<string>
    {
        "Wave-2 mechanism screen. LIVE disabled. Frozen five / FrozenRisk / Isolated LOW catalog numbers unchanged.",
        "Book: LOW Isolated $1000, 0.5% risk, 3x, structural stops honor SuggestedStop, 2R when TP omitted.",
        "OOS is reported and was not used to change parameters. Full 528-universe run is blocked.",
        "Walk-forward TEST windows are skipped on this lightweight screen (SkipWalkForward). Re-run without that flag only after IS/VAL gates.",
        $"Window {waveStart:yyyy-MM-dd} → {waveEnd:yyyy-MM-dd}. Timeframes {string.Join("/", waveTfs)}. Candidates {waveCandidates.Count}."
    };
    Console.WriteLine(waveNotes[0]);
    using var waveHttp = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
    waveHttp.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformWave2Research/1.0");
    var waveSeries = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
    foreach (var symbol in symbols)
    {
        foreach (var tf in waveTfs)
        {
            Exception? last = null;
            for (var attempt = 1; attempt <= 6; attempt++)
            {
                try
                {
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(waveHttp, candleCacheDir, symbol, tf, waveStart, waveEnd);
                    var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                    waveSeries[(symbol, tf)] = closed;
                    waveNotes.Add($"{symbol} {tf}: bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got}");
                    Console.WriteLine(waveNotes[^1]);
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
                waveNotes.Add($"{symbol} {tf}: load failed ({last.Message})");
            }
        }
    }

    var waveBooks = ResearchRunner.Evaluate(new ResearchRunRequest
    {
        Phase = ResearchPhases.Pilot,
        Candidates = waveCandidates,
        Symbols = symbols,
        Timeframes = waveTfs,
        Series = waveSeries,
        CostLabels = [ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress],
        Force = true,
        MaxParallel = maxParallel,
        UseLowIsolated = true,
        HonorSuggestedStops = true,
        SkipWalkForward = true
    });
    var waveReport = Path.Combine(root, "docs", "strategy-research-wave2-report.md");
    File.WriteAllText(waveReport, Wave2Report.RenderMarkdown(waveCandidates, waveBooks, waveNotes));
    Wave2Report.WriteArtifacts(waveDir, waveBooks);
    File.WriteAllText(Path.Combine(root, "artifacts", "strategy-research", "wave2-results.json"), JsonSerializer.Serialize(waveBooks));
    Console.WriteLine($"Wrote {waveReport}");
    Console.WriteLine($"Books {waveBooks.Count}. LIVE was not changed. Frozen five were not changed. 528-universe was not launched.");
    return 0;
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
