using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.StrategyResearch;

internal static class PriceActionCli
{
    public static async Task<int> RunDataAsync(string root, string cacheDir, string[] args)
    {
        var outDir = Path.Combine(root, "artifacts", "strategy-research", "price-action");
        Directory.CreateDirectory(outDir);
        Console.WriteLine(PriceActionReport.Confirmation);
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformPriceActionCoverage/1.0");
        var rows = await ScalpingCoverage.RunAsync(http, cacheDir, outDir);
        File.WriteAllText(Path.Combine(root, "docs", "price-action-coverage.md"), ScalpingReport.CoverageMarkdown(rows));
        Console.WriteLine($"Coverage rows {rows.Count}. 1m/3m use the existing ResearchKlineCache. Missing = DATA_UNAVAILABLE.");
        Console.WriteLine(PriceActionReport.Confirmation);
        return 0;
    }

    public static async Task<int> RunResearchAsync(string root, string cacheDir, string[] args)
    {
        var outDir = Path.Combine(root, "artifacts", "strategy-research", "price-action");
        Directory.CreateDirectory(outDir);
        var symbolFilter = Arg(args, "--symbol");
        var timeframeRaw = Arg(args, "--timeframe") ?? "5m,15m";
        var maxParallel = Math.Max(1, ParseInt(args, "--max-parallel", 2));
        var symbols = string.IsNullOrWhiteSpace(symbolFilter)
            ? new[] { "BTCUSDT", "ETHUSDT" }
            : symbolFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var tfs = timeframeRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var candidateId = Arg(args, "--candidate");
        var candidates = string.IsNullOrWhiteSpace(candidateId)
            ? ResearchRegistry.PriceAction.ToList()
            : ResearchRegistry.PriceAction.Where(c =>
                string.Equals(c.CandidateId, candidateId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.ParentTemplateKey, candidateId, StringComparison.OrdinalIgnoreCase)).ToList();
        var notes = new List<string>
        {
            PriceActionReport.Confirmation,
            "Price Action research. LIVE disabled. Frozen five / Isolated LOW unchanged. No OperatorCatalog. No VALIDATED_FOR_PAPER.",
            "Outcome labels (forward return, MFE, MAE) are research-only and never enter PriceActionStrategyEvaluator.",
            "Cup & Handle = NOT_IMPLEMENTED.",
            $"Coins {string.Join(",", symbols)}. Timeframes {string.Join("/", tfs)}. Candidates {candidates.Count}."
        };
        Console.WriteLine(notes[0]);
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformPriceActionResearch/1.0");
        var coveragePath = Path.Combine(outDir, "coverage.json");
        IReadOnlyList<ScalpingCoverageRow> coverage = [];
        if (File.Exists(coveragePath))
        {
            coverage = JsonSerializer.Deserialize<List<ScalpingCoverageRow>>(File.ReadAllText(coveragePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
            notes.Add($"Loaded coverage rows={coverage.Count}.");
        }
        else
        {
            var scalpCoverage = Path.Combine(root, "artifacts", "strategy-research", "scalping", "coverage.json");
            if (File.Exists(scalpCoverage))
            {
                coverage = JsonSerializer.Deserialize<List<ScalpingCoverageRow>>(File.ReadAllText(scalpCoverage), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
                notes.Add($"Reused scalping coverage rows={coverage.Count}. 1m/3m remain DATA_UNAVAILABLE until --price-action-data or --scalping-data fills them.");
            }
        }

        var end = DateTimeOffset.UtcNow;
        var series = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var symbol in symbols)
        {
            foreach (var tf in tfs.Concat(["15m"]).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var windowStart = ScalpingCoverage.WindowStart(tf, end);
                    var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(http, cacheDir, symbol, tf, windowStart, end);
                    var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                    var cap = tf is "1m" or "3m" ? 8000 : 6000;
                    if (closed.Count > cap)
                    {
                        notes.Add($"{symbol} {tf} truncated to last {cap} of {closed.Count} bars (bounded research, not 528-universe).");
                        closed = closed.TakeLast(cap).ToList();
                    }

                    if (closed.Count < 80)
                    {
                        notes.Add($"{symbol} {tf} DATA_UNAVAILABLE bars={closed.Count} (not substituted with another timeframe).");
                        Console.WriteLine(notes[^1]);
                        continue;
                    }

                    series[(symbol, tf)] = closed;
                    notes.Add($"{symbol} {tf} bars={closed.Count} cache {(hit ? "hit" : "miss")} dl={got}");
                    Console.WriteLine(notes[^1]);
                    var gaps = ResearchKlineCache.GapCount(closed, tf);
                    var taker = ResearchKlineCache.TakerCoverage(closed);
                    coverage = OverlayCoverage(coverage, new ScalpingCoverageRow(
                        symbol,
                        tf,
                        closed[0].OpenTime,
                        closed[^1].CloseTime,
                        "fapi.binance.com/fapi/v1/klines",
                        tf,
                        gaps,
                        closed.Count,
                        got,
                        hit,
                        taker,
                        ResearchStatuses.Researching,
                        "OHLCV from ResearchKlineCache. Funding/OI/mark/index not fabricated. Liquidations DATA_UNAVAILABLE."));
                }
                catch (Exception ex)
                {
                    notes.Add($"{symbol} {tf} DATA_UNAVAILABLE ({ex.Message})");
                    Console.WriteLine(notes[^1]);
                }
            }
        }

        var sequences = new List<SequenceStat>();
        var patternStats = new List<PatternStat>();
        var occurrences = new List<object>();
        var hypotheses = new List<string>
        {
            "sequence lengths 2-6+ bullish/bearish independently",
            "bullish sequence then rejection",
            "bearish sequence then rejection",
            "compression then expansion",
            "expansion continuation vs exhaustion",
            "W detected vs W + neckline confirmation (separate)",
            "M detected vs M + neckline confirmation (separate)",
            "flag/pennant/triangle/wedge/rectangle breakout direction measured",
            "liquidity sweep rejection",
            "breakout + retest vs failed breakout",
            "H&S / inverse H&S neckline close",
            "structure BOS",
            "Model B IS/VAL/OOS + cost BASE/1.25/1.5/2.0",
            "occupancy same-coin collision"
        };
        foreach (var ((symbol, tf), candles) in series)
        {
            var book = PriceActionBook.Build(candles);
            sequences.AddRange(PriceActionResearch.Sequences(symbol, tf, candles, book));
            patternStats.AddRange(PriceActionResearch.Patterns(symbol, tf, candles, book));
            foreach (var occ in book.Occurrences.Where(o => o.Status == PatternKinds.Confirmed).Take(80))
            {
                var from = Math.Max(0, occ.StartIndex - 8);
                var to = Math.Min(candles.Count - 1, (occ.ConfirmationIndex ?? occ.DetectionIndex) + 12);
                occurrences.Add(new
                {
                    symbol,
                    timeframe = tf,
                    occ.PatternType,
                    occ.Version,
                    start = candles[occ.StartIndex].OpenTime,
                    detection = candles[occ.DetectionIndex].CloseTime,
                    confirmation = occ.ConfirmationIndex is { } c ? candles[c].CloseTime : (DateTimeOffset?)null,
                    entry = occ.EntryTriggerIndex is { } e ? candles[e].CloseTime : (DateTimeOffset?)null,
                    occ.Neckline,
                    occ.Level,
                    occ.Direction,
                    occ.Status,
                    points = occ.Points.Select(p => new { role = p.Role, price = p.Price, time = candles[p.Index].OpenTime }),
                    bars = Enumerable.Range(from, to - from + 1).Select(i => new
                    {
                        time = candles[i].OpenTime.ToUnixTimeSeconds(),
                        open = candles[i].Open,
                        high = candles[i].High,
                        low = candles[i].Low,
                        close = candles[i].Close,
                        volume = candles[i].Volume
                    })
                });
            }
        }

        var books = ResearchRunner.Evaluate(new ResearchRunRequest
        {
            Phase = ResearchPhases.Pilot,
            Candidates = candidates,
            Symbols = symbols,
            Timeframes = tfs,
            Series = series,
            CostLabels = [ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress],
            Force = true,
            MaxParallel = maxParallel,
            UseLowIsolated = true,
            HonorSuggestedStops = false,
            SkipWalkForward = false,
            DataSnapshot = new ResearchDataSnapshot(["OHLCV"])
        });
        if (books.Any(b => string.Equals(b.Status, ResearchStatuses.ValidatedForPaper, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("AssignStatus must never return VALIDATED_FOR_PAPER.");
        }

        OccupancyReplayResult? occupancy = null;
        try
        {
            var from = DateTimeOffset.UtcNow.AddDays(-30);
            var to = DateTimeOffset.UtcNow;
            var occSettings = StrategyValidation.LowIsolatedRisk(from, to) with { MaxSimultaneousPositions = 5, MaxHoldBars = 8 };
            var occBooks = new List<(string StrategyKey, string Symbol, IReadOnlyList<MarketCandle> Candles, IReadOnlyList<SignalType> Signals)>();
            foreach (var symbol in symbols.Take(2))
            {
                foreach (var key in new[] { StrategyTemplateKeys.PaWDoubleBottom, StrategyTemplateKeys.PaBullFlag, StrategyTemplateKeys.PaStructureBreak })
                {
                    if (!series.TryGetValue((symbol, "5m"), out var candles) || candles.Count < 40)
                    {
                        continue;
                    }

                    candles = candles.TakeLast(400).ToList();
                    var candidate = candidates.First(c => c.ParentTemplateKey == key);
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
                notes.Add($"Occupancy same-coin={occupancy.SameCoinRejects} slots={occupancy.SlotRejects} heat={occupancy.HeatRejects} trades={occupancy.Trades.Count} DD={occupancy.MaximumDrawdownPercent:0.00}%.");
                File.WriteAllText(Path.Combine(outDir, "occupancy.json"), JsonSerializer.Serialize(occupancy, new JsonSerializerOptions { WriteIndented = true }));
            }
        }
        catch (Exception ex)
        {
            notes.Add($"Occupancy replay skipped ({ex.Message}).");
        }

        var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var jsonOpt = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(outDir, "sequences.json"), JsonSerializer.Serialize(sequences, jsonOpt));
        File.WriteAllText(Path.Combine(outDir, "pattern-stats.json"), JsonSerializer.Serialize(patternStats, jsonOpt));
        File.WriteAllText(Path.Combine(outDir, "occurrences.json"), JsonSerializer.Serialize(occurrences, jsonOpt));
        File.WriteAllText(Path.Combine(outDir, "hypotheses.json"), JsonSerializer.Serialize(hypotheses, jsonOpt));
        File.WriteAllText(Path.Combine(outDir, "books.json"), JsonSerializer.Serialize(books, jsonOpt));
        File.WriteAllText(Path.Combine(outDir, "coverage.json"), JsonSerializer.Serialize(coverage, jsonOpt));
        var summary = new
        {
            id = runId,
            confirmation = PriceActionReport.Confirmation,
            liveOff = true,
            scalpingLiveOff = true,
            priceActionLiveOff = true,
            validatedForPaperAssigned = false,
            books,
            coverage,
            sequences,
            patterns = patternStats,
            occurrences,
            hypotheses,
            occupancyRejects = occupancy?.Rejects ?? [],
            sameCoinRejects = occupancy?.SameCoinRejects ?? 0,
            slotRejects = occupancy?.SlotRejects ?? 0,
            heatRejects = occupancy?.HeatRejects ?? 0,
            cupAndHandle = PatternKinds.NotImplemented
        };
        File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonSerializer.Serialize(summary, jsonOpt));
        Directory.CreateDirectory(Path.Combine(outDir, "runs"));
        File.WriteAllText(Path.Combine(outDir, "runs", $"{runId}.json"), JsonSerializer.Serialize(summary, jsonOpt));
        var report = Path.Combine(root, "docs", "PRICE_ACTION_RESEARCH_REPORT.md");
        File.WriteAllText(report, PriceActionReport.Render(candidates, books, coverage, occupancy, sequences, patternStats, hypotheses, notes));
        Console.WriteLine($"Wrote {report}");
        Console.WriteLine($"Books {books.Count}. Sequence rows {sequences.Count}. Pattern stat rows {patternStats.Count}.");
        Console.WriteLine(PriceActionReport.Confirmation);
        return 0;
    }

    private static IReadOnlyList<ScalpingCoverageRow> OverlayCoverage(
        IReadOnlyList<ScalpingCoverageRow> existing,
        ScalpingCoverageRow row)
    {
        var next = existing.ToList();
        var idx = next.FindIndex(x =>
            string.Equals(x.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Timeframe, row.Timeframe, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            next[idx] = row;
        }
        else
        {
            next.Add(row);
        }

        return next;
    }

    private static string? Arg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static int ParseInt(string[] args, string name, int fallback)
    {
        var raw = Arg(args, name);
        return int.TryParse(raw, out var n) ? n : fallback;
    }
}
