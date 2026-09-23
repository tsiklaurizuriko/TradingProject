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

internal sealed record ContextualManifestPayload(
    string Version,
    DateTimeOffset FrozenAtUtc,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Timeframes,
    string EntryTimeframe,
    string SplitPolicy,
    string WalkForwardPolicy,
    IReadOnlyList<string> Costs,
    string SurvivorRule,
    IReadOnlyList<ContextualHypothesis> Hypotheses,
    string CoverageSha256,
    string CausalityRule,
    bool LiveOff,
    bool PaperPromotionOff,
    string Phase7SymmetricalTriangle);

internal sealed record ContextualFrozenManifest(ContextualManifestPayload Payload, string Sha256);

internal sealed record ContextualSurvivorDecision(
    string CandidateId,
    string Family,
    string Variant,
    int IsTrades,
    string IsProfitFactor,
    decimal IsNet,
    int ValidationTrades,
    string ValidationProfitFactor,
    decimal ValidationNet,
    int ValidationSymbolsWithTrades,
    bool Passed,
    string Reason);

internal sealed record ContextualSurvivorPayload(
    string Version,
    DateTimeOffset FrozenAtUtc,
    string HypothesisManifestSha256,
    string SelectionRule,
    IReadOnlyList<ContextualSurvivorDecision> Decisions,
    IReadOnlyList<string> SurvivorCandidateIds,
    bool OosReadBeforeFreeze);

internal sealed record ContextualFrozenSurvivors(ContextualSurvivorPayload Payload, string Sha256);

internal sealed record ContextualCoverageSlice(
    string Symbol,
    string Timeframe,
    DateTimeOffset RequestedFrom,
    DateTimeOffset RequestedTo);

internal sealed record ContextualCoverageFile(IReadOnlyList<ContextualCoverageSlice> Coverage);

internal static class ContextualPriceActionDiscovery
{
    private const string DiscoveryPhase = "DISCOVERY";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string OutputDirectory(string root) =>
        Path.Combine(root, "artifacts", "strategy-research", "contextual-price-action-alpha");

    public static int Freeze(string root)
    {
        var dir = OutputDirectory(root);
        Directory.CreateDirectory(dir);
        var coveragePath = Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json");
        if (!File.Exists(coveragePath))
        {
            Console.WriteLine($"Missing Phase 7 coverage {coveragePath}. Contextual research does not download candles.");
            return 2;
        }

        var manifestPath = Path.Combine(dir, "hypothesis-manifest.json");
        if (File.Exists(manifestPath))
        {
            _ = ReadManifest(manifestPath);
            Console.WriteLine($"Verified existing contextual hypothesis manifest: {manifestPath}");
            return 0;
        }

        var payload = new ContextualManifestPayload(
            ContextualPriceActionCatalog.Version,
            DateTimeOffset.UtcNow,
            Phase7PriceActionUniverse.Symbols,
            ContextualPriceActionCatalog.Timeframes,
            ContextualPriceActionCatalog.EntryTimeframe,
            "Per coin: chronological 60% IS, 20% VALIDATION, 20% untouched OOS on the 5m entry series.",
            "Existing StrategyValidation walk-forward generator, at most eight BASE-cost test windows.",
            ContextualPriceActionCatalog.CostLabels,
            ContextualPriceActionCatalog.SurvivorRule,
            ContextualPriceActionCatalog.Hypotheses,
            Sha256(File.ReadAllText(coveragePath)),
            "Entry bar i uses 5m bars <= i. Confirmation and context use the last candle whose CloseTime <= the 5m CloseTime. Swings are only those confirmed at or before i. Outcomes never enter the signal.",
            true,
            true,
            "Phase 7 symmetrical triangle 1h stays INSUFFICIENT_DATA. It is not retuned and is not in this hypothesis list.");
        WriteFrozen(manifestPath, payload);
        Console.WriteLine($"Frozen {payload.Hypotheses.Count} contextual hypotheses: {manifestPath}");
        return 0;
    }

    public static async Task<int> RunAsync(string root, string cacheDir, string[] args)
    {
        if (Freeze(root) != 0)
        {
            return 2;
        }

        var dir = OutputDirectory(root);
        var maxParallel = Math.Clamp(ParseInt(args, "--max-parallel", 2), 1, 4);
        var manifest = ReadManifest(Path.Combine(dir, "hypothesis-manifest.json"));
        var candidates = Candidates();
        var initialPath = Path.Combine(dir, "is-validation-results.json");
        var initial = ReadBooks(initialPath);
        var initialDone = DoneKeys(initial);
        var concurrencyPath = Path.Combine(dir, "is-concurrency.json");
        var concurrency = ReadConcurrency(concurrencyPath);
        Console.WriteLine($"Contextual PA IS/VALIDATION: hypotheses={candidates.Count}, books={initial.Count}, maxParallel={maxParallel}.");

        foreach (var symbol in Phase7PriceActionUniverse.Symbols)
        {
            if (SymbolComplete(initial, candidates, symbol, "IS")
                && SymbolComplete(initial, candidates, symbol, "VALIDATION"))
            {
                continue;
            }

            var series = await LoadSymbolSeries(cacheDir, root, symbol);
            var caches = BuildCaches(series);
            var signals = ContextualPriceActionSignals.BuildAll(ByTimeframe(caches));
            RecordConcurrency(concurrency, symbol, series, signals);
            WriteJson(concurrencyPath, concurrency);
            var precomputed = Precomputed(symbol, signals);
            if (signals.Any(x => x.SeriesMissing))
            {
                foreach (var missing in signals.Where(x => x.SeriesMissing))
                {
                    foreach (var phase in new[] { "IS", "VALIDATION" })
                    {
                        var row = Unavailable(missing.CandidateId, symbol, phase);
                        if (initialDone.Add(Key(row)))
                        {
                            initial.Add(row);
                        }
                    }
                }
            }

            var runnable = candidates.Where(c => signals.Any(s => s.CandidateId == c.CandidateId && !s.SeriesMissing)).ToArray();
            if (runnable.Length > 0)
            {
                var rows = ResearchRunner.Evaluate(new ResearchRunRequest
                {
                    Phase = DiscoveryPhase,
                    Candidates = runnable,
                    Symbols = [symbol],
                    Timeframes = [ContextualPriceActionCatalog.EntryTimeframe],
                    Series = series,
                    CostLabels = [ResearchCostLabels.Base],
                    Done = initialDone,
                    Force = false,
                    MaxParallel = maxParallel,
                    UseLowIsolated = true,
                    HonorSuggestedStops = false,
                    SkipWalkForward = true,
                    IndicatorCaches = caches,
                    PrecomputedSignals = precomputed,
                    DataSnapshot = new ResearchDataSnapshot(["OHLCV"])
                });
                initial.AddRange(rows);
                foreach (var row in rows)
                {
                    initialDone.Add(Key(row));
                }
            }

            WriteJson(initialPath, Ordered(initial));
            Console.WriteLine($"{symbol}: IS/VALIDATION checkpoint books={initial.Count}.");
        }

        var survivorPath = Path.Combine(dir, "survivor-manifest.json");
        ContextualFrozenSurvivors survivors;
        if (File.Exists(survivorPath))
        {
            survivors = ReadSurvivors(survivorPath);
        }
        else if (!AllSymbolsComplete(initial, candidates, "IS") || !AllSymbolsComplete(initial, candidates, "VALIDATION"))
        {
            Console.WriteLine("IS/VALIDATION is incomplete. Survivor freeze and OOS were not opened.");
            return 2;
        }
        else
        {
            var decisions = SelectSurvivors(candidates, initial);
            var payload = new ContextualSurvivorPayload(
                ContextualPriceActionCatalog.Version,
                DateTimeOffset.UtcNow,
                manifest.Sha256,
                ContextualPriceActionCatalog.SurvivorRule,
                decisions,
                decisions.Where(x => x.Passed).Select(x => x.CandidateId).ToArray(),
                false);
            survivors = WriteFrozen(survivorPath, payload);
            Console.WriteLine($"Froze {payload.SurvivorCandidateIds.Count} pre-OOS survivors before any OOS read.");
        }

        var finalPath = Path.Combine(dir, "oos-results.json");
        var finalBooks = ReadBooks(finalPath);
        var finalDone = DoneKeys(finalBooks);
        var survivorCandidates = candidates
            .Where(c => survivors.Payload.SurvivorCandidateIds.Contains(c.CandidateId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var occupancyBooks = new List<(string StrategyKey, string Symbol, IReadOnlyList<MarketCandle> Candles, IReadOnlyList<SignalType> Signals)>();
        if (survivorCandidates.Length > 0)
        {
            foreach (var symbol in Phase7PriceActionUniverse.Symbols)
            {
                var series = await LoadSymbolSeries(cacheDir, root, symbol);
                var caches = BuildCaches(series);
                var signals = ContextualPriceActionSignals.BuildAll(ByTimeframe(caches));
                var precomputed = Precomputed(symbol, signals);
                foreach (var request in FinalRequests(symbol, survivorCandidates, series, caches, precomputed, finalDone, maxParallel))
                {
                    var rows = ResearchRunner.Evaluate(request);
                    finalBooks.AddRange(rows);
                    foreach (var row in rows)
                    {
                        finalDone.Add(Key(row));
                    }

                    WriteJson(finalPath, Ordered(finalBooks));
                }

                var entry = series[(symbol, ContextualPriceActionCatalog.EntryTimeframe)];
                var (_, oosFrom) = StrategyValidation.ChronologicalSplitIndices(entry.Count);
                foreach (var candidate in survivorCandidates)
                {
                    var row = signals.First(x => x.CandidateId == candidate.CandidateId);
                    if (row.SeriesMissing || row.Signals.Length != entry.Count)
                    {
                        continue;
                    }

                    var sliced = row.Signals.ToArray();
                    Array.Fill(sliced, SignalType.NoAction, 0, Math.Min(oosFrom, sliced.Length));
                    occupancyBooks.Add((candidate.CandidateId, symbol, entry, sliced));
                }

                Console.WriteLine($"{symbol}: OOS checkpoint books={finalBooks.Count}.");
            }
        }

        var robustness = BuildRobustness(candidates, initial, finalBooks, survivors);
        OccupancyReplayResult? occupancy = null;
        if (occupancyBooks.Count > 0)
        {
            var settings = ResearchRunner.CostScaledRisk(
                new ResearchRunRequest
                {
                    Phase = ResearchPhases.Oos,
                    Candidates = [],
                    Symbols = [],
                    Timeframes = [],
                    Series = new Dictionary<(string, string), IReadOnlyList<MarketCandle>>(),
                    UseLowIsolated = true
                },
                occupancyBooks[0].Candles[0].OpenTime,
                occupancyBooks[0].Candles[^1].CloseTime,
                ResearchCostLabels.Base);
            occupancy = PortfolioOccupancyReplay.RunBooks(occupancyBooks, settings);
        }

        WriteSummary(dir, manifest, survivors, candidates, initial, finalBooks, robustness, concurrency, occupancy);
        var report = RenderReport(root, manifest, survivors, candidates, initial, finalBooks, robustness, concurrency, occupancy);
        File.WriteAllText(Path.Combine(root, "docs", "CONTEXTUAL_PRICE_ACTION_ALPHA_REPORT.md"), report);
        File.WriteAllText(Path.Combine(dir, "report.md"), report);
        Console.WriteLine("Contextual Price Action research report written. LIVE and PAPER were not enabled.");
        return 0;
    }

    public static int Render(string root)
    {
        var dir = OutputDirectory(root);
        var manifest = ReadManifest(Path.Combine(dir, "hypothesis-manifest.json"));
        var survivors = ReadSurvivors(Path.Combine(dir, "survivor-manifest.json"));
        var candidates = Candidates();
        var initial = ReadBooks(Path.Combine(dir, "is-validation-results.json"));
        var finalBooks = ReadBooks(Path.Combine(dir, "oos-results.json"));
        var robustness = BuildRobustness(candidates, initial, finalBooks, survivors);
        var concurrency = ReadConcurrency(Path.Combine(dir, "is-concurrency.json"));
        var report = RenderReport(root, manifest, survivors, candidates, initial, finalBooks, robustness, concurrency, null);
        File.WriteAllText(Path.Combine(root, "docs", "CONTEXTUAL_PRICE_ACTION_ALPHA_REPORT.md"), report);
        Console.WriteLine("Re-rendered contextual Price Action report without a new research run.");
        return 0;
    }

    private static IReadOnlyList<ResearchCandidate> Candidates()
    {
        var created = DateTimeOffset.Parse("2026-09-23T00:00:00Z", CultureInfo.InvariantCulture);
        return ContextualPriceActionCatalog.Hypotheses.Select(h => new ResearchCandidate(
            h.CandidateId,
            StrategyTemplateKeys.PaStructureBreak,
            1,
            $"{h.Family} {h.Variant}. {h.EventRule}",
            ResearchKinds.Native,
            StrategyTemplateKeys.PaStructureBreak,
            "contextual-pa",
            ["closed-OHLCV", "causal-structure", "causal-pattern-events"],
            $"{h.EventRule} {h.ConfirmationRule} {h.ContextRule}",
            "Model B Isolated LOW SL/TP. Completed-candle signal, next-bar open fill. No OOS stop tuning.",
            new ResearchFilters(
                ConfirmationTimeframe: h.ConfirmationTimeframe,
                ContextTimeframe: h.ContextTimeframe ?? h.StructureTimeframe),
            new ResearchNativeParams(),
            [h.EntryTimeframe],
            StrategyTemplateKeys.SupportedDirections,
            [],
            created,
            "Phase 8 contextual PA. Phase 7 coins and cache. Entry 5m. Hierarchy frozen before OOS.",
            ResearchStatuses.Researching,
            ContextualPriceActionCatalog.Version)).ToArray();
    }

    private static async Task<Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> LoadSymbolSeries(
        string cacheDir,
        string root,
        string symbol)
    {
        var coverage = JsonSerializer.Deserialize<ContextualCoverageFile>(
            await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json")),
            JsonOptions) ?? throw new InvalidOperationException("Phase 7 coverage artifact is invalid.");
        var map = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var tf in ContextualPriceActionCatalog.Timeframes)
        {
            var range = coverage.Coverage.First(x =>
                string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Timeframe, tf, StringComparison.OrdinalIgnoreCase));
            var bars = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, tf);
            map[(symbol, tf)] = bars
                .Where(x => x.OpenTime >= range.RequestedFrom && x.CloseTime <= range.RequestedTo && x.IsClosed)
                .OrderBy(x => x.OpenTime)
                .ToList();
        }

        return map;
    }

    private static Dictionary<string, CausalIndicatorCache> ByTimeframe(
        IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache> caches) =>
        caches.ToDictionary(x => x.Key.Timeframe, x => x.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<(string Symbol, string Timeframe), CausalIndicatorCache> BuildCaches(
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series)
    {
        var caches = new Dictionary<(string Symbol, string Timeframe), CausalIndicatorCache>();
        foreach (var pair in series)
        {
            var cache = new CausalIndicatorCache(pair.Value);
            if (pair.Value.Count > 0)
            {
                _ = cache.PriceAction();
                _ = cache.Atr(ContextualPriceActionCatalog.AtrPeriod);
                _ = cache.RelativeVolume(ContextualPriceActionCatalog.RelativeVolumeLookback);
            }

            caches[pair.Key] = cache;
        }

        return caches;
    }

    private static Dictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>> Precomputed(
        string symbol,
        IReadOnlyList<ContextualSignalRow> signals)
    {
        var map = new Dictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>>();
        foreach (var row in signals.Where(x => !x.SeriesMissing))
        {
            map[(row.CandidateId, symbol, ContextualPriceActionCatalog.EntryTimeframe)] = row.Signals;
        }

        return map;
    }

    private static void RecordConcurrency(
        ContextualConcurrency concurrency,
        string symbol,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyList<ContextualSignalRow> signals)
    {
        concurrency.Signals.RemoveAll(token =>
        {
            var parts = token.Split('|');
            return parts.Length >= 2 && string.Equals(parts[1], symbol, StringComparison.OrdinalIgnoreCase);
        });
        var candles = series[(symbol, ContextualPriceActionCatalog.EntryTimeframe)];
        var (insEnd, _) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
        foreach (var row in signals.Where(x => !x.SeriesMissing && !IsBaseline(x.CandidateId)))
        {
            var limit = Math.Min(insEnd, row.Signals.Length);
            for (var i = 0; i < limit; i++)
            {
                if (row.Signals[i] is not (SignalType.Buy or SignalType.Sell))
                {
                    continue;
                }

                var key = candles[i].CloseTime.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
                concurrency.Signals.Add($"{key}|{symbol}|{row.CandidateId}");
            }
        }
    }

    private static bool IsBaseline(string candidateId)
    {
        var hypothesis = ContextualPriceActionCatalog.ById(candidateId);
        return hypothesis.Variant is "BASELINE" or "CONTINUATION" or "REVERSAL";
    }

    private static IEnumerable<ResearchRunRequest> FinalRequests(
        string symbol,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache> caches,
        IReadOnlyDictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>> signals,
        HashSet<string> done,
        int maxParallel)
    {
        ResearchRunRequest Request(string phase, IReadOnlyList<string> costs, bool blocks = false, bool walkForward = false) =>
            new()
            {
                Phase = phase,
                Candidates = candidates,
                Symbols = [symbol],
                Timeframes = [ContextualPriceActionCatalog.EntryTimeframe],
                Series = series,
                CostLabels = costs,
                Done = done,
                Force = false,
                MaxParallel = maxParallel,
                UseLowIsolated = true,
                HonorSuggestedStops = false,
                SkipWalkForward = !walkForward,
                IncludeChronologicalBlocks = blocks,
                IncludeWalkForward = walkForward,
                IndicatorCaches = caches,
                PrecomputedSignals = signals,
                DataSnapshot = new ResearchDataSnapshot(["OHLCV", "CompletedHtf"])
            };

        yield return Request(ResearchPhases.Oos, ContextualPriceActionCatalog.CostLabels);
        yield return Request("BLOCKS", [ResearchCostLabels.Base], blocks: true);
        yield return Request(ResearchPhases.WalkForward, [ResearchCostLabels.Base], walkForward: true);
    }

    private static IReadOnlyList<ContextualSurvivorDecision> SelectSurvivors(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books)
    {
        return candidates.Select(candidate =>
        {
            var rows = books.Where(x => x.CandidateId == candidate.CandidateId && x.CostLabel == ResearchCostLabels.Base).ToList();
            var ins = Combine(rows.Where(x => x.Phase == "IS"));
            var val = Combine(rows.Where(x => x.Phase == "VALIDATION"));
            var symbols = rows.Count(x => x.Phase == "VALIDATION" && x.TradeCount > 0);
            var hypothesis = ContextualPriceActionCatalog.ById(candidate.CandidateId);
            var passed = ins.Trades >= 30
                && (ins.ProfitFactor.Kind == ProfitFactorKind.NoLosses || ins.ProfitFactor.IsFinite && ins.ProfitFactor.Ratio >= 0.90m)
                && val.Trades >= 20
                && (val.ProfitFactor.Kind == ProfitFactorKind.NoLosses || val.ProfitFactor.IsFinite && val.ProfitFactor.Ratio > 1m)
                && val.NetPnl > 0m
                && symbols >= 5;
            return new ContextualSurvivorDecision(
                candidate.CandidateId,
                hypothesis.Family,
                hypothesis.Variant,
                ins.Trades,
                Pf(ins),
                ins.NetPnl,
                val.Trades,
                Pf(val),
                val.NetPnl,
                symbols,
                passed,
                passed ? "PRE_OOS_GATE_PASSED" : "PRE_OOS_GATE_FAILED");
        }).ToArray();
    }

    private static IReadOnlyList<ContextualRobustness> BuildRobustness(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> initial,
        IReadOnlyList<ResearchBookResult> oosBooks,
        ContextualFrozenSurvivors survivors)
    {
        return candidates.Select(candidate =>
        {
            var hypothesis = ContextualPriceActionCatalog.ById(candidate.CandidateId);
            var decision = survivors.Payload.Decisions.First(x => x.CandidateId == candidate.CandidateId);
            var isRows = initial.Where(x => x.CandidateId == candidate.CandidateId && x.Phase == "IS").ToList();
            var valRows = initial.Where(x => x.CandidateId == candidate.CandidateId && x.Phase == "VALIDATION").ToList();
            var ins = Combine(isRows);
            var val = Combine(valRows);
            var oos = oosBooks.Where(x => x.CandidateId == candidate.CandidateId && x.Phase == "OOS").ToList();
            var baseRows = oos.Where(x => x.CostLabel == ResearchCostLabels.Base).ToList();
            var highRows = oos.Where(x => x.CostLabel == ResearchCostLabels.High).ToList();
            var totals = Combine(baseRows);
            var high = Combine(highRows);
            var mild = Combine(oos.Where(x => x.CostLabel == ResearchCostLabels.Mild));
            var stress = Combine(oos.Where(x => x.CostLabel == ResearchCostLabels.Stress));
            var longSide = Combine(baseRows.Select(x => x.LongTotals).Where(x => x is not null)!);
            var shortSide = Combine(baseRows.Select(x => x.ShortTotals).Where(x => x is not null)!);
            var symbolNets = baseRows.Where(x => x.TradeCount > 0)
                .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.Sum(x => x.NetPnl))
                .ToArray();
            var abs = symbolNets.Select(Math.Abs).OrderByDescending(x => x).ToArray();
            var absSum = abs.Sum();
            var topTwo = absSum == 0 ? 0 : abs.Take(2).Sum() / absSum;
            var symbolFragile = symbolNets.Length > 1 && (topTwo >= 0.80m || symbolNets.Count(x => x > 0) < Math.Ceiling(symbolNets.Length * 0.30m));
            var costFragile = totals.ProfitFactor.IsFinite
                && totals.ProfitFactor.Ratio > 1m
                && (high.Trades == 0 || high.ProfitFactor.Kind == ProfitFactorKind.NoWins || high.ProfitFactor.IsFinite && high.ProfitFactor.Ratio < 1m);
            var regimes = baseRows.Where(x => x.TradeCount > 0)
                .GroupBy(x => string.IsNullOrWhiteSpace(x.Regime) ? "TRANSITION" : x.Regime, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Regime: g.Key, Abs: g.Sum(x => Math.Abs(x.NetPnl))))
                .ToArray();
            var regimeSum = regimes.Sum(x => x.Abs);
            var topRegime = regimeSum == 0 || regimes.Length < 2 ? 0 : regimes.Max(x => x.Abs) / regimeSum;
            var regimeFragile = regimes.Length >= 2 && topRegime >= 0.80m;
            var blocks = oosBooks.Where(x => x.CandidateId == candidate.CandidateId && x.Phase.StartsWith("BLOCK", StringComparison.OrdinalIgnoreCase) && x.CostLabel == ResearchCostLabels.Base).ToList();
            var blockNets = blocks.GroupBy(x => x.Phase).Select(g => Math.Abs(g.Sum(x => x.NetPnl))).OrderByDescending(x => x).ToArray();
            var blockSum = blockNets.Sum();
            var topBlock = blockSum == 0 ? 0 : blockNets[0] / blockSum;
            var wf = oosBooks.Where(x => x.CandidateId == candidate.CandidateId && x.Phase.StartsWith("WF", StringComparison.OrdinalIgnoreCase)).ToList();
            var wfTotals = Combine(wf);
            var labels = new List<string>();
            if (!decision.Passed)
            {
                labels.Add(ins.Trades == 0 && val.Trades == 0 ? ResearchStatuses.NoTrades : val.Trades == 0 ? ResearchStatuses.InsufficientData : ResearchStatuses.ValidationFailed);
            }
            else
            {
                if (totals.Trades < 50 || longSide.Trades < 15 || shortSide.Trades < 15)
                {
                    labels.Add(ResearchStatuses.InsufficientData);
                }

                if (totals.Trades == 0)
                {
                    labels.Add(ResearchStatuses.NoTrades);
                }
                else if (totals.Expectancy <= 0m || totals.ProfitFactor.Kind == ProfitFactorKind.NoWins || totals.ProfitFactor.IsFinite && totals.ProfitFactor.Ratio <= 1m)
                {
                    labels.Add(ResearchStatuses.OosFailed);
                }

                if (symbolFragile) labels.Add(ResearchStatuses.SymbolFragile);
                if (costFragile) labels.Add(ResearchStatuses.CostFragile);
                if (regimeFragile) labels.Add(ResearchStatuses.RegimeFragile);
                if (labels.Count == 0) labels.Add(ResearchStatuses.Researching);
            }

            return new ContextualRobustness(
                hypothesis.Family,
                hypothesis.Variant,
                candidate.CandidateId,
                decision.Passed,
                ins.Trades,
                Pf(ins),
                ins.NetPnl,
                ins.Expectancy,
                val.Trades,
                Pf(val),
                val.NetPnl,
                val.Expectancy,
                totals.Trades,
                Pf(totals),
                totals.NetPnl,
                totals.Expectancy,
                totals.WinRate,
                longSide.Trades,
                Pf(longSide),
                longSide.NetPnl,
                shortSide.Trades,
                Pf(shortSide),
                shortSide.NetPnl,
                symbolNets.Length,
                symbolNets.Count(x => x > 0),
                topTwo,
                Pf(mild),
                mild.NetPnl,
                Pf(high),
                high.NetPnl,
                Pf(stress),
                stress.NetPnl,
                wf.Count,
                wfTotals.Trades,
                Pf(wfTotals),
                wfTotals.NetPnl,
                topBlock,
                topRegime,
                string.Join("|", labels.Distinct(StringComparer.Ordinal)));
        }).ToArray();
    }

    private static void WriteSummary(
        string dir,
        ContextualFrozenManifest manifest,
        ContextualFrozenSurvivors survivors,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> initial,
        IReadOnlyList<ResearchBookResult> oos,
        IReadOnlyList<ContextualRobustness> robustness,
        ContextualConcurrency concurrency,
        OccupancyReplayResult? occupancy)
    {
        var (maxCoins, collisions) = ConcurrencyStats(concurrency);
        var summary = new
        {
            id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            confirmation = "LIVE = OFF. SCALPING LIVE = OFF. PRICE ACTION LIVE = OFF. PAPER promotion = OFF. No VALIDATED_FOR_PAPER. Research-only contextual Price Action.",
            liveOff = true,
            scalpingLiveOff = true,
            priceActionLiveOff = true,
            paperPromotionOff = true,
            validatedForPaperAssigned = false,
            hypothesisCount = candidates.Count,
            families = 8,
            survivors = survivors.Payload.SurvivorCandidateIds.Count,
            hypothesisManifestSha256 = manifest.Sha256,
            survivorManifestSha256 = survivors.Sha256,
            hypotheses = manifest.Payload.Hypotheses.Select(x => x.CandidateId).ToArray(),
            statuses = robustness,
            isConcurrencyMaxCoins = maxCoins,
            isSameSymbolHypothesisCollisions = collisions,
            occupancy = occupancy is null
                ? null
                : new
                {
                    occupancy.FinalEquity,
                    occupancy.NetProfit,
                    occupancy.MaximumDrawdownPercent,
                    trades = occupancy.Trades.Count,
                    occupancy.SameCoinRejects,
                    occupancy.SlotRejects,
                    occupancy.HeatRejects
                },
            futuresData = "DATA_UNAVAILABLE: funding, OI, taker flow, liquidations, order book, basis. OHLCV only.",
            phase7SymmetricalTriangle = manifest.Payload.Phase7SymmetricalTriangle,
            isBooks = initial.Count,
            oosBooks = oos.Count
        };
        WriteJson(Path.Combine(dir, "summary.json"), summary);
    }

    private static string RenderReport(
        string root,
        ContextualFrozenManifest manifest,
        ContextualFrozenSurvivors survivors,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> initial,
        IReadOnlyList<ResearchBookResult> oos,
        IReadOnlyList<ContextualRobustness> robustness,
        ContextualConcurrency concurrency,
        OccupancyReplayResult? occupancy)
    {
        var (maxCoins, collisions) = ConcurrencyStats(concurrency);
        var sb = new StringBuilder();
        sb.AppendLine("# CONTEXTUAL PRICE ACTION ALPHA");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. Scalping LIVE = OFF. Price Action LIVE = OFF. PAPER promotion = OFF. VALIDATED_FOR_PAPER = none.");
        sb.AppendLine("No definition was changed after the survivor manifest. OOS was not used to pick thresholds.");
        sb.AppendLine();
        sb.AppendLine("## 1. Objective");
        sb.AppendLine("Test whether a causal price-action event improves after frozen higher-timeframe context and confirmation, versus the same event alone.");
        sb.AppendLine();
        sb.AppendLine("## 2. Phase 7 baseline");
        sb.AppendLine("Phase 7 tested 162 hypotheses. Descending triangle 5m failed (OOS PF about 0.61). Symmetrical triangle 1h had OOS PF about 1.26 on about 40 trades and remains INSUFFICIENT_DATA. No MTF variant survived. Phase 8 does not retune that triangle and does not repeat the 162-hypothesis grid.");
        sb.AppendLine(manifest.Payload.Phase7SymmetricalTriangle);
        sb.AppendLine();
        sb.AppendLine("## 3. Data universe");
        sb.AppendLine($"Coins: {string.Join(", ", manifest.Payload.Symbols)}.");
        sb.AppendLine($"Timeframes loaded from the existing ResearchKlineCache: {string.Join(", ", manifest.Payload.Timeframes)}. Entry timeframe: {manifest.Payload.EntryTimeframe}. No second copy of OHLCV was downloaded.");
        sb.AppendLine($"Phase 7 coverage SHA-256 `{manifest.Payload.CoverageSha256}`.");
        var coveragePath = Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json");
        if (File.Exists(coveragePath))
        {
            var coverage = JsonSerializer.Deserialize<ContextualCoverageFile>(File.ReadAllText(coveragePath), JsonOptions)?.Coverage ?? [];
            foreach (var group in coverage.GroupBy(x => x.Timeframe))
            {
                sb.AppendLine($"- {group.Key}: coins={group.Count()}.");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## 4. Setup definitions");
        foreach (var hypothesis in manifest.Payload.Hypotheses)
        {
            sb.AppendLine($"- `{hypothesis.CandidateId}`: {hypothesis.EventRule} Confirmation: {hypothesis.ConfirmationRule} Context: {hypothesis.ContextRule}");
        }
        sb.AppendLine();
        sb.AppendLine("## 5. Causal features");
        sb.AppendLine(manifest.Payload.CausalityRule);
        sb.AppendLine($"Displacement body/range >= {ContextualPriceActionCatalog.DisplacementBodyRatio.ToString(CultureInfo.InvariantCulture)}, range >= ATR({ContextualPriceActionCatalog.AtrPeriod}). Strict relative volume lookback {ContextualPriceActionCatalog.RelativeVolumeLookback}, minimum {ContextualPriceActionCatalog.StrictRelativeVolumeMin.ToString(CultureInfo.InvariantCulture)}. Compression run >= {ContextualPriceActionCatalog.CompressionRunMin} using the existing range median rule. Funding, OI, taker flow, liquidations, order book, and basis are DATA_UNAVAILABLE.");
        sb.AppendLine();
        sb.AppendLine("## 6. Hypothesis count");
        sb.AppendLine($"Families: 8. Frozen variants: {candidates.Count}. Pre-OOS manifest SHA-256 `{manifest.Sha256}`. Survivor manifest SHA-256 `{survivors.Sha256}`. Entered OOS: {survivors.Payload.SurvivorCandidateIds.Count}.");
        sb.AppendLine($"Gate: {ContextualPriceActionCatalog.SurvivorRule}");
        sb.AppendLine();
        sb.AppendLine("## 7. Baseline vs contextual");
        sb.AppendLine("| Family | Variant | IS n | IS PF | IS net | VAL n | VAL PF | VAL net | OOS n | OOS PF | OOS net | OOS expectancy | Status |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var row in robustness)
        {
            sb.AppendLine($"| {row.Family} | {row.Variant} | {row.IsTrades} | {row.IsProfitFactor} | {row.IsNet:0.00} | {row.ValidationTrades} | {row.ValidationProfitFactor} | {row.ValidationNet:0.00} | {row.OosTrades} | {row.OosProfitFactor} | {row.OosNet:0.00} | {row.OosExpectancy:0.0000} | {row.Status} |");
        }
        sb.AppendLine();
        sb.AppendLine(BaselineDeltas(robustness));
        sb.AppendLine();
        sb.AppendLine("## 8-12. IS, validation, OOS, walk-forward, cost stress");
        sb.AppendLine("| Hypothesis | WF windows | WF n | WF PF | WF net | MILD PF/net | HIGH 1.5x PF/net | STRESS PF/net | Top block share |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | --- | --- | --- | ---: |");
        foreach (var row in robustness.Where(x => x.EnteredOos))
        {
            sb.AppendLine($"| {Md(row.CandidateId)} | {row.WalkForwardWindows} | {row.WalkForwardTrades} | {row.WalkForwardProfitFactor} | {row.WalkForwardNet:0.00} | {row.MildProfitFactor}/{row.MildNet:0.00} | {row.HighProfitFactor}/{row.HighNet:0.00} | {row.StressProfitFactor}/{row.StressNet:0.00} | {row.TopBlockAbsoluteNetShare:0.0%} |");
        }
        if (robustness.All(x => !x.EnteredOos))
        {
            sb.AppendLine("No hypothesis entered OOS. Walk-forward and cost stress were not opened.");
        }
        sb.AppendLine();
        sb.AppendLine("## 13-15. Symbol, side, regime");
        sb.AppendLine("| Hypothesis | Symbols + / tested | Top-two share | LONG n/PF/net | SHORT n/PF/net | Top regime share |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- | ---: |");
        foreach (var row in robustness.Where(x => x.EnteredOos))
        {
            sb.AppendLine($"| {Md(row.CandidateId)} | {row.SymbolsProfitable} / {row.SymbolsWithTrades} | {row.TopTwoAbsoluteNetShare:0.0%} | {row.LongTrades}/{row.LongProfitFactor}/{row.LongNet:0.00} | {row.ShortTrades}/{row.ShortProfitFactor}/{row.ShortNet:0.00} | {row.TopRegimeAbsoluteNetShare:0.0%} |");
        }
        sb.AppendLine();
        sb.AppendLine("## 16-17. Portfolio occupancy and drawdown");
        sb.AppendLine($"IS contextual signals, before outcomes: max coins with a signal on the same 5m close = {maxCoins}. Same-coin hypothesis collisions = {collisions}.");
        if (occupancy is null)
        {
            sb.AppendLine("OOS occupancy replay did not run because no hypothesis passed the frozen pre-OOS gate, or this render had no in-memory OOS books.");
        }
        else
        {
            sb.AppendLine($"OOS survivor occupancy: trades={occupancy.Trades.Count}, net={occupancy.NetProfit:0.00}, final equity={occupancy.FinalEquity:0.00}, max drawdown={occupancy.MaximumDrawdownPercent:0.00}%, same-coin rejects={occupancy.SameCoinRejects}, slot rejects={occupancy.SlotRejects}, heat rejects={occupancy.HeatRejects}.");
        }
        sb.AppendLine("Live Portfolio Risk was not modified. This replay is research reporting.");
        sb.AppendLine();
        sb.AppendLine("## 18. Multiple-testing caveat");
        sb.AppendLine($"Phase 7 already screened 162 hypotheses. Phase 8 adds {candidates.Count} more frozen variants across 8 families, 15 coins, and one entry timeframe. A PF above 1 on one slice is an expected false positive at this count. Failed rows stay in the table. OOS was limited to the pre-registered gate. That gate itself is one more selection step and does not make a survivor validated.");
        sb.AppendLine();
        sb.AppendLine("## 19. Failed hypotheses");
        foreach (var row in robustness.Where(x => x.Status != ResearchStatuses.Researching))
        {
            sb.AppendLine($"- `{row.CandidateId}`: {row.Status}");
        }
        sb.AppendLine();
        sb.AppendLine("## 20. Interesting hypotheses");
        var interesting = robustness.Where(IsInteresting).ToArray();
        if (interesting.Length == 0)
        {
            sb.AppendLine("None. No hypothesis met the pre-registered interesting bar, and none is VALIDATED_FOR_PAPER.");
        }
        else
        {
            foreach (var row in interesting)
            {
                sb.AppendLine($"- `{row.CandidateId}` remains {row.Status}. It is not promoted.");
            }
        }
        sb.AppendLine();
        sb.AppendLine("## 21. Final status");
        sb.AppendLine("VALIDATED_FOR_PAPER = none.");
        sb.AppendLine($"RESEARCHING: {robustness.Count(x => x.Status == ResearchStatuses.Researching)}.");
        sb.AppendLine($"Other statuses are listed above. LIVE, Scalping LIVE, Price Action LIVE, and PAPER promotion stayed OFF. Risk Engine, Execution, Isolated margin, Frozen Five, and the Phase 7 hypotheses were not changed.");
        sb.AppendLine();
        sb.AppendLine("## Coin-level books");
        sb.AppendLine("| Coin | Hypothesis | Phase | Cost | n | PF | Net | LONG n/PF | SHORT n/PF | Status |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | --- | --- | --- |");
        foreach (var row in initial.Concat(oos).OrderBy(x => x.CandidateId).ThenBy(x => x.Symbol).ThenBy(x => x.Phase).ThenBy(x => x.CostLabel))
        {
            sb.AppendLine($"| {row.Symbol} | {Md(row.CandidateId)} | {row.Phase} | {row.CostLabel} | {row.TradeCount} | {ResearchPf.Render(row.ProfitFactorState, row.ProfitFactor)} | {row.NetPnl:0.00} | {Side(row.LongTotals)} | {Side(row.ShortTotals)} | {row.Status} |");
        }
        return sb.ToString();
    }

    private static string BaselineDeltas(IReadOnlyList<ContextualRobustness> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("A contextual row is ahead of its family baseline only when its OOS expectancy is higher and its OOS trades are at least half of the baseline. Validation numbers are shown when OOS was not opened.");
        foreach (var family in rows.Select(x => x.Family).Distinct(StringComparer.Ordinal))
        {
            var baseline = rows.FirstOrDefault(x => x.Family == family && x.Variant is "BASELINE" or "CONTINUATION");
            if (baseline is null)
            {
                continue;
            }

            foreach (var row in rows.Where(x => x.Family == family && x.CandidateId != baseline.CandidateId))
            {
                var baseExp = baseline.EnteredOos ? baseline.OosExpectancy : baseline.ValidationExpectancy;
                var rowExp = row.EnteredOos ? row.OosExpectancy : row.ValidationExpectancy;
                var baseN = baseline.EnteredOos ? baseline.OosTrades : baseline.ValidationTrades;
                var rowN = row.EnteredOos ? row.OosTrades : row.ValidationTrades;
                var sampleOk = baseN == 0 || rowN * 2 >= baseN;
                sb.AppendLine($"- {row.CandidateId} vs {baseline.Variant}: expectancy {rowExp:0.0000} vs {baseExp:0.0000}, trades {rowN} vs {baseN}. Sample retained: {sampleOk}.");
            }
        }

        return sb.ToString();
    }

    private static bool IsInteresting(ContextualRobustness row) =>
        row.EnteredOos
        && row.Status == ResearchStatuses.Researching
        && row.OosTrades >= 50
        && row.OosExpectancy > 0m
        && row.LongTrades >= 15
        && row.ShortTrades >= 15
        && row.TopBlockAbsoluteNetShare < 0.80m
        && row.WalkForwardTrades >= 15
        && row.WalkForwardNet > 0m;

    private static (int MaxCoins, int Collisions) ConcurrencyStats(ContextualConcurrency concurrency)
    {
        var byTime = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var byTimeSymbol = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in concurrency.Signals)
        {
            var parts = token.Split('|');
            if (parts.Length < 3)
            {
                continue;
            }

            if (!byTime.TryGetValue(parts[0], out var coins))
            {
                coins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                byTime[parts[0]] = coins;
            }

            coins.Add(parts[1]);
            var pair = parts[0] + "|" + parts[1];
            byTimeSymbol[pair] = byTimeSymbol.GetValueOrDefault(pair) + 1;
        }

        var max = byTime.Count == 0 ? 0 : byTime.Max(x => x.Value.Count);
        var collisions = byTimeSymbol.Values.Count(x => x > 1);
        return (max, collisions);
    }

    private static ResearchBookResult Unavailable(string candidateId, string symbol, string phase) =>
        new(
            candidateId,
            symbol,
            ContextualPriceActionCatalog.EntryTimeframe,
            phase,
            ResearchCostLabels.Base,
            ResearchStatuses.DataUnavailable,
            0, 0, 0, 0, 0, 0,
            "NO_TRADES",
            null, 0, 0, 0, 0, 0,
            null, null, null, null, null, null, null, null, null, null, null, null,
            "TRANSITION",
            ["Required causal series missing. Not filled and not fabricated."]);

    private static bool SymbolComplete(IReadOnlyList<ResearchBookResult> books, IReadOnlyList<ResearchCandidate> candidates, string symbol, string phase) =>
        candidates.All(c => books.Any(x =>
            x.CandidateId == c.CandidateId
            && string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Phase, phase, StringComparison.OrdinalIgnoreCase)));

    private static bool AllSymbolsComplete(IReadOnlyList<ResearchBookResult> books, IReadOnlyList<ResearchCandidate> candidates, string phase) =>
        Phase7PriceActionUniverse.Symbols.All(symbol => SymbolComplete(books, candidates, symbol, phase));

    private static PnlTotals Combine(IEnumerable<ResearchBookResult> rows) =>
        ResearchDiagnostics.CombinePf(rows.Select(x => x.CombinedTotals).Where(x => x is not null).Cast<PnlTotals>());

    private static PnlTotals Combine(IEnumerable<PnlTotals?> rows) =>
        ResearchDiagnostics.CombinePf(rows.Where(x => x is not null).Cast<PnlTotals>());

    private static string Pf(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return ResearchPf.Render(state, ratio);
    }

    private static string Side(PnlTotals? totals) => totals is null ? "0/N/A" : $"{totals.Trades}/{Pf(totals)}";

    private static string Md(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static List<ResearchBookResult> ReadBooks(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<List<ResearchBookResult>>(File.ReadAllText(path), JsonOptions) ?? []
            : [];

    private static ContextualConcurrency ReadConcurrency(string path) =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<ContextualConcurrency>(File.ReadAllText(path), JsonOptions) ?? new ContextualConcurrency()
            : new ContextualConcurrency();

    private static HashSet<string> DoneKeys(IEnumerable<ResearchBookResult> rows) =>
        rows.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Key(ResearchBookResult row) =>
        $"{row.CandidateId}|{row.Symbol}|{row.Timeframe}|{row.Phase}|{row.CostLabel}";

    private static List<ResearchBookResult> Ordered(IEnumerable<ResearchBookResult> rows) =>
        rows.GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.CandidateId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Phase, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.CostLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static ContextualFrozenManifest ReadManifest(string path) =>
        ReadAndVerify<ContextualManifestPayload, ContextualFrozenManifest>(path, x => x.Payload, x => x.Sha256);

    private static ContextualFrozenSurvivors ReadSurvivors(string path) =>
        ReadAndVerify<ContextualSurvivorPayload, ContextualFrozenSurvivors>(path, x => x.Payload, x => x.Sha256);

    private static TWrapper ReadAndVerify<TPayload, TWrapper>(string path, Func<TWrapper, TPayload> payload, Func<TWrapper, string> hash)
    {
        var wrapper = JsonSerializer.Deserialize<TWrapper>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"Invalid frozen manifest {path}.");
        var actual = Sha256(JsonSerializer.Serialize(payload(wrapper), JsonOptions));
        if (!string.Equals(actual, hash(wrapper), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Frozen manifest hash mismatch: {path}");
        }

        return wrapper;
    }

    private static ContextualFrozenManifest WriteFrozen(string path, ContextualManifestPayload payload)
    {
        var result = new ContextualFrozenManifest(payload, Sha256(JsonSerializer.Serialize(payload, JsonOptions)));
        WriteJson(path, result);
        return result;
    }

    private static ContextualFrozenSurvivors WriteFrozen(string path, ContextualSurvivorPayload payload)
    {
        var result = new ContextualFrozenSurvivors(payload, Sha256(JsonSerializer.Serialize(payload, JsonOptions)));
        WriteJson(path, result);
        return result;
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static int ParseInt(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase) && int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return fallback;
    }
}

internal sealed class ContextualConcurrency
{
    public List<string> Signals { get; set; } = [];
}

internal sealed record ContextualRobustness(
    string Family,
    string Variant,
    string CandidateId,
    bool EnteredOos,
    int IsTrades,
    string IsProfitFactor,
    decimal IsNet,
    decimal IsExpectancy,
    int ValidationTrades,
    string ValidationProfitFactor,
    decimal ValidationNet,
    decimal ValidationExpectancy,
    int OosTrades,
    string OosProfitFactor,
    decimal OosNet,
    decimal OosExpectancy,
    decimal OosWinRate,
    int LongTrades,
    string LongProfitFactor,
    decimal LongNet,
    int ShortTrades,
    string ShortProfitFactor,
    decimal ShortNet,
    int SymbolsWithTrades,
    int SymbolsProfitable,
    decimal TopTwoAbsoluteNetShare,
    string MildProfitFactor,
    decimal MildNet,
    string HighProfitFactor,
    decimal HighNet,
    string StressProfitFactor,
    decimal StressNet,
    int WalkForwardWindows,
    int WalkForwardTrades,
    string WalkForwardProfitFactor,
    decimal WalkForwardNet,
    decimal TopBlockAbsoluteNetShare,
    decimal TopRegimeAbsoluteNetShare,
    string Status);
