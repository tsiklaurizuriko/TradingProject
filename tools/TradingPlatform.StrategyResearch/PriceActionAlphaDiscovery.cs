using System.Globalization;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

internal sealed record AlphaHypothesis(
    string CandidateId,
    string Setup,
    string EntryTimeframe,
    string? ConfirmationTimeframe,
    string ContextTimeframe,
    string EntryRule,
    string ConfirmationRule,
    string ContextRule);

internal sealed record AlphaManifestPayload(
    string Version,
    DateTimeOffset FrozenAtUtc,
    IReadOnlyList<string> Symbols,
    IReadOnlyList<string> Timeframes,
    IReadOnlyDictionary<string, int> RequestedDays,
    string SplitPolicy,
    string BlockPolicy,
    string WalkForwardPolicy,
    IReadOnlyList<string> Costs,
    string SurvivorRule,
    IReadOnlyList<AlphaHypothesis> Hypotheses,
    string CoverageSha256,
    string CausalityRule,
    bool LiveOff,
    bool PaperPromotionOff);

internal sealed record AlphaFrozenManifest(AlphaManifestPayload Payload, string Sha256);

internal sealed record AlphaSurvivorDecision(
    string CandidateId,
    string Setup,
    string Timeframe,
    int IsTrades,
    string IsProfitFactor,
    decimal IsNet,
    int ValidationTrades,
    string ValidationProfitFactor,
    decimal ValidationNet,
    int ValidationSymbolsWithTrades,
    bool Passed,
    string Reason);

internal sealed record AlphaSurvivorPayload(
    string Version,
    DateTimeOffset FrozenAtUtc,
    string HypothesisManifestSha256,
    string SelectionRule,
    IReadOnlyList<AlphaSurvivorDecision> Decisions,
    IReadOnlyList<string> SurvivorCandidateIds,
    bool OosReadBeforeFreeze);

internal sealed record AlphaFrozenSurvivors(AlphaSurvivorPayload Payload, string Sha256);

internal sealed record AlphaRobustnessRow(
    string Setup,
    string CandidateId,
    string Timeframe,
    int OosTrades,
    string OosProfitFactor,
    decimal OosNet,
    int SymbolsWithTrades,
    int SymbolsProfitable,
    decimal TopTwoAbsoluteNetShare,
    bool SymbolFragile,
    bool TimeframeFragile,
    bool CostFragile,
    bool InsufficientData,
    string Status);

internal static class PriceActionAlphaDiscovery
{
    private const string DiscoveryPhase = "DISCOVERY";
    private const string SurvivorRule =
        "Pre-registered: aggregate IS trades >= 30, aggregate IS PF >= 0.90 (or NoLosses), aggregate VALIDATION trades >= 15, VALIDATION PF > 1.00 (or NoLosses), VALIDATION net > 0, and >= 5 symbols with VALIDATION trades.";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static int Freeze(string root)
    {
        var dir = PriceActionAlphaData.OutputDirectory(root);
        Directory.CreateDirectory(dir);
        var coveragePath = Path.Combine(dir, "coverage-post.json");
        if (!File.Exists(coveragePath))
        {
            Console.WriteLine($"Missing {coveragePath}. Run --price-action-alpha-expand first.");
            return 2;
        }

        var manifestPath = Path.Combine(dir, "hypothesis-manifest.json");
        if (File.Exists(manifestPath))
        {
            _ = ReadManifest(manifestPath);
            Console.WriteLine($"Verified existing immutable hypothesis manifest: {manifestPath}");
            return 0;
        }

        var payload = new AlphaManifestPayload(
            PriceActionAlphaData.Version,
            DateTimeOffset.UtcNow,
            Phase7PriceActionUniverse.Symbols,
            PriceActionAlphaData.Timeframes,
            PriceActionAlphaData.RequestedDays,
            "Per coin/setup/timeframe: chronological 60% IS, 20% VALIDATION, 20% untouched OOS. Timeframe is encoded in each hypothesis before OOS.",
            "Four contiguous chronological blocks over each complete usable series; diagnostic only after survivor freeze.",
            "Up to eight rolling test windows using the existing StrategyValidation walk-forward generator; BASE cost only.",
            [ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress],
            SurvivorRule,
            Hypotheses(),
            Sha256File(coveragePath),
            "At entry candle i, entry uses bars <= i. Confirmation and context use only the last candle whose CloseTime <= entry candle CloseTime. Outcomes/MFE/MAE/future returns never enter signal features.",
            true,
            true);
        WriteFrozen(manifestPath, payload);
        Console.WriteLine($"Frozen {payload.Hypotheses.Count} pre-OOS hypotheses: {manifestPath}");
        Console.WriteLine(PriceActionAlphaData.Confirmation);
        return 0;
    }

    public static async Task<int> RunAsync(string root, string cacheDir, string[] args)
    {
        var dir = PriceActionAlphaData.OutputDirectory(root);
        Directory.CreateDirectory(dir);
        if (Freeze(root) != 0)
        {
            return 2;
        }

        var maxParallel = Math.Clamp(ParseInt(args, "--max-parallel", 2), 1, 4);
        var manifest = ReadManifest(Path.Combine(dir, "hypothesis-manifest.json"));
        var allCandidates = BuildCandidates();
        var initialPath = Path.Combine(dir, "is-validation-results.json");
        var initial = ReadBooks(initialPath);
        var initialDone = DoneKeys(initial);
        Console.WriteLine($"Alpha discovery IS/VALIDATION: hypotheses={allCandidates.Count}, completed books={initial.Count}, maxParallel={maxParallel}.");

        foreach (var symbol in Phase7PriceActionUniverse.Symbols)
        {
            var series = await LoadSymbolSeries(cacheDir, symbol, manifest.Payload);
            var indicatorCaches = BuildIndicatorCaches(series);
            var precomputedSignals = BuildSignals(symbol, allCandidates, series, indicatorCaches);
            foreach (var timeframe in PriceActionAlphaData.Timeframes)
            {
                var candidates = allCandidates.Where(x =>
                    x.SupportedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase)).ToArray();
                var rows = ResearchRunner.Evaluate(new ResearchRunRequest
                {
                    Phase = DiscoveryPhase,
                    Candidates = candidates,
                    Symbols = [symbol],
                    Timeframes = [timeframe],
                    Series = series,
                    CostLabels = [ResearchCostLabels.Base],
                    Done = initialDone,
                    Force = false,
                    MaxParallel = maxParallel,
                    UseLowIsolated = true,
                    HonorSuggestedStops = false,
                    SkipWalkForward = true,
                    IndicatorCaches = indicatorCaches,
                    PrecomputedSignals = precomputedSignals,
                    DataSnapshot = new ResearchDataSnapshot(["OHLCV"])
                });
                initial.AddRange(rows);
                foreach (var row in rows)
                {
                    initialDone.Add(Key(row));
                }

                WriteJson(initialPath, Ordered(initial));
            }

            Console.WriteLine($"{symbol}: IS/VALIDATION checkpoint books={initial.Count}.");
        }

        var survivorPath = Path.Combine(dir, "survivor-manifest.json");
        AlphaFrozenSurvivors survivors;
        if (File.Exists(survivorPath))
        {
            survivors = ReadSurvivors(survivorPath);
        }
        else
        {
            var decisions = SelectSurvivors(allCandidates, initial);
            var payload = new AlphaSurvivorPayload(
                PriceActionAlphaData.Version,
                DateTimeOffset.UtcNow,
                manifest.Sha256,
                SurvivorRule,
                decisions,
                decisions.Where(x => x.Passed).Select(x => x.CandidateId).ToArray(),
                OosReadBeforeFreeze: false);
            survivors = WriteFrozen(survivorPath, payload);
        }

        var selected = allCandidates
            .Where(x => survivors.Payload.SurvivorCandidateIds.Contains(x.CandidateId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        Console.WriteLine($"Frozen survivors={selected.Length}/{allCandidates.Count}; hash={survivors.Sha256}.");

        var validationPath = Path.Combine(dir, "post-freeze-results.json");
        var validation = ReadBooks(validationPath);
        var validationDone = DoneKeys(validation);
        foreach (var symbol in Phase7PriceActionUniverse.Symbols)
        {
            var series = await LoadSymbolSeries(cacheDir, symbol, manifest.Payload);
            var indicatorCaches = BuildIndicatorCaches(series);
            var precomputedSignals = BuildSignals(symbol, selected, series, indicatorCaches);
            foreach (var timeframe in PriceActionAlphaData.Timeframes)
            {
                var candidates = selected.Where(x =>
                    x.SupportedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase)).ToArray();
                if (candidates.Length == 0)
                {
                    continue;
                }

                foreach (var request in FinalRequests(symbol, timeframe, candidates, series, indicatorCaches, precomputedSignals, validationDone, maxParallel))
                {
                    var rows = ResearchRunner.Evaluate(request);
                    validation.AddRange(rows);
                    foreach (var row in rows)
                    {
                        validationDone.Add(Key(row));
                    }

                    WriteJson(validationPath, Ordered(validation));
                }
            }

            Console.WriteLine($"{symbol}: post-freeze checkpoint books={validation.Count}.");
        }

        var allBooks = Ordered(initial.Concat(validation));
        if (allBooks.Any(x => string.Equals(x.Status, ResearchStatuses.ValidatedForPaper, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Alpha Discovery must never assign VALIDATED_FOR_PAPER.");
        }

        var robustness = BuildRobustness(allCandidates, allBooks);
        WriteJson(Path.Combine(dir, "results.json"), allBooks);
        WriteJson(Path.Combine(dir, "robustness.json"), robustness);
        WriteSummary(root, allCandidates, allBooks, robustness, survivors);
        File.WriteAllText(
            Path.Combine(root, "docs", "PRICE_ACTION_ALPHA_DISCOVERY_REPORT.md"),
            RenderReport(root, manifest, survivors, allCandidates, allBooks, robustness));
        Console.WriteLine($"Wrote {Path.Combine(root, "docs", "PRICE_ACTION_ALPHA_DISCOVERY_REPORT.md")}");
        Console.WriteLine(PriceActionAlphaData.Confirmation);
        return 0;
    }

    public static int Render(string root)
    {
        var dir = PriceActionAlphaData.OutputDirectory(root);
        var manifest = ReadManifest(Path.Combine(dir, "hypothesis-manifest.json"));
        var survivors = ReadSurvivors(Path.Combine(dir, "survivor-manifest.json"));
        var candidates = BuildCandidates();
        var books = ReadBooks(Path.Combine(dir, "results.json"));
        var robustness = BuildRobustness(candidates, books);
        WriteJson(Path.Combine(dir, "robustness.json"), robustness);
        WriteSummary(root, candidates, books, robustness, survivors);
        File.WriteAllText(
            Path.Combine(root, "docs", "PRICE_ACTION_ALPHA_DISCOVERY_REPORT.md"),
            RenderReport(root, manifest, survivors, candidates, books, robustness));
        Console.WriteLine("Re-rendered Price Action Alpha artifacts without downloading data or running research.");
        return 0;
    }

    internal static IReadOnlyList<AlphaHypothesis> Hypotheses()
    {
        var rows = new List<AlphaHypothesis>();
        foreach (var setup in ResearchRegistry.PriceAction)
        {
            foreach (var tf in PriceActionAlphaData.Timeframes)
            {
                rows.Add(new AlphaHypothesis(
                    $"{setup.CandidateId}|STF|{tf}",
                    setup.ParentTemplateKey ?? setup.ParentStrategyId,
                    tf,
                    null,
                    tf,
                    "Existing causal Price Action setup on the stated entry timeframe.",
                    "None for single-timeframe hypothesis.",
                    "Same-timeframe causal setup only."));
            }

            rows.Add(Mtf(setup, "1m", "3m", "15m"));
            rows.Add(Mtf(setup, "3m", "5m", "15m"));
            rows.Add(Mtf(setup, "5m", null, "15m"));
            rows.Add(Mtf(setup, "15m", null, "1h"));
        }

        return rows;
    }

    internal static IReadOnlyList<ResearchCandidate> BuildCandidates()
    {
        var byId = ResearchRegistry.PriceAction.ToDictionary(x => x.ParentTemplateKey!, StringComparer.OrdinalIgnoreCase);
        return Hypotheses().Select(h =>
        {
            var source = byId[h.Setup];
            var mtf = h.ConfirmationTimeframe is not null
                || !string.Equals(h.EntryTimeframe, h.ContextTimeframe, StringComparison.OrdinalIgnoreCase);
            return source with
            {
                CandidateId = h.CandidateId,
                Hypothesis = $"{source.Hypothesis} Entry={h.EntryTimeframe}; confirmation={h.ConfirmationTimeframe ?? "none"}; context={h.ContextTimeframe}.",
                Filters = mtf
                    ? source.Filters with
                    {
                        HigherTimeframe = null,
                        ConfirmationTimeframe = h.ConfirmationTimeframe,
                        ContextTimeframe = h.ContextTimeframe
                    }
                    : source.Filters with
                    {
                        HigherTimeframe = null,
                        ConfirmationTimeframe = null,
                        ContextTimeframe = null
                    },
                SupportedTimeframes = [h.EntryTimeframe],
                DatasetScope = "Phase 7 all-timeframe Alpha Discovery; 15 fixed coins; timeframe/MTF structure frozen before OOS.",
                Status = ResearchStatuses.Researching,
                CodeVersion = PriceActionAlphaData.Version
            };
        }).ToArray();
    }

    private static AlphaHypothesis Mtf(
        ResearchCandidate setup,
        string entry,
        string? confirmation,
        string context)
    {
        var structure = confirmation is null ? $"{entry}-{context}" : $"{entry}-{confirmation}-{context}";
        return new AlphaHypothesis(
            $"{setup.CandidateId}|MTF|{structure}",
            setup.ParentTemplateKey ?? setup.ParentStrategyId,
            entry,
            confirmation,
            context,
            "Existing causal Price Action setup on entry timeframe.",
            confirmation is null
                ? "No separate confirmation timeframe."
                : "Last fully closed confirmation candle must have same-side causal market-structure bias or same-side BOS.",
            "Last fully closed context candle must have same-side EMA20/EMA50 trend alignment.");
    }

    private static async Task<IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> LoadSymbolSeries(
        string cacheDir,
        string symbol,
        AlphaManifestPayload manifest)
    {
        var coverageArtifact = JsonSerializer.Deserialize<AlphaDataArtifact>(
            await File.ReadAllTextAsync(Path.Combine(Directory.GetParent(cacheDir)!.FullName, "strategy-research", "price-action-alpha", "coverage-post.json")),
            JsonOptions) ?? throw new InvalidOperationException("Invalid post-expansion coverage artifact.");
        var map = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var tf in manifest.Timeframes)
        {
            var range = coverageArtifact.Coverage.First(x =>
                string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Timeframe, tf, StringComparison.OrdinalIgnoreCase));
            var bars = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, tf);
            map[(symbol, tf)] = bars
                .Where(x => x.OpenTime >= range.RequestedFrom && x.CloseTime <= range.RequestedTo)
                .OrderBy(x => x.OpenTime)
                .ToList();
        }

        return map;
    }

    private static IEnumerable<ResearchRunRequest> FinalRequests(
        string symbol,
        string timeframe,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache> indicatorCaches,
        IReadOnlyDictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>> precomputedSignals,
        HashSet<string> done,
        int maxParallel)
    {
        ResearchRunRequest Request(
            string phase,
            IReadOnlyList<string> costs,
            bool blocks = false,
            bool walkForward = false) =>
            new()
            {
                Phase = phase,
                Candidates = candidates,
                Symbols = [symbol],
                Timeframes = [timeframe],
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
                IndicatorCaches = indicatorCaches,
                PrecomputedSignals = precomputedSignals,
                DataSnapshot = new ResearchDataSnapshot(["OHLCV", "CompletedHtf"])
            };

        yield return Request(
            ResearchPhases.Oos,
            [ResearchCostLabels.Base, ResearchCostLabels.Mild, ResearchCostLabels.High, ResearchCostLabels.Stress]);
        yield return Request("BLOCKS", [ResearchCostLabels.Base], blocks: true);
        yield return Request(ResearchPhases.WalkForward, [ResearchCostLabels.Base], walkForward: true);
    }

    private static IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache> BuildIndicatorCaches(
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series)
    {
        var result = new Dictionary<(string Symbol, string Timeframe), CausalIndicatorCache>();
        foreach (var pair in series)
        {
            var sw = Stopwatch.StartNew();
            var cache = new CausalIndicatorCache(pair.Value);
            _ = cache.PriceAction();
            _ = cache.Ema(20);
            _ = cache.Ema(50);
            result[pair.Key] = cache;
            Console.WriteLine($"{pair.Key.Symbol} {pair.Key.Timeframe}: precomputed causal indicators in {sw.Elapsed}.");
        }

        return result;
    }

    private static IReadOnlyDictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>> BuildSignals(
        string symbol,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache> caches)
    {
        var result = new Dictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>>();
        var sources = ResearchRegistry.PriceAction.ToDictionary(x => x.ParentTemplateKey!, StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates.GroupBy(x =>
                     (Setup: x.ParentTemplateKey ?? x.ParentStrategyId, Timeframe: x.SupportedTimeframes[0])))
        {
            var setup = group.Key.Setup;
            var timeframe = group.Key.Timeframe;
            var candles = series[(symbol, timeframe)];
            var cache = caches[(symbol, timeframe)];
            var source = sources[setup] with { SupportedTimeframes = [timeframe] };
            var definition = ResearchRunner.DefinitionFor(source, timeframe);
            var engine = new ResearchStrategyEngine(source);
            var baseSignals = new SignalType[candles.Count];
            for (var i = 0; i < candles.Count; i++)
            {
                baseSignals[i] = engine.EvaluateAt(
                    definition,
                    new StrategyContext
                    {
                        ClosedCandles = candles,
                        CurrentPrice = candles[i].Close,
                        HasOpenPosition = false
                    },
                    cache,
                    i,
                    out _);
            }

            foreach (var candidate in group)
            {
                if (string.IsNullOrWhiteSpace(candidate.Filters.ContextTimeframe))
                {
                    result[(candidate.CandidateId, symbol, timeframe)] = baseSignals;
                    continue;
                }

                var gated = new SignalType[baseSignals.Length];
                CausalIndicatorCache? confirmation = null;
                if (!string.IsNullOrWhiteSpace(candidate.Filters.ConfirmationTimeframe))
                {
                    confirmation = caches[(symbol, candidate.Filters.ConfirmationTimeframe)];
                }

                var context = caches[(symbol, candidate.Filters.ContextTimeframe)];
                var contextFast = context.Ema(candidate.Filters.EmaFast);
                var contextSlow = context.Ema(candidate.Filters.EmaSlow);
                for (var i = 0; i < baseSignals.Length; i++)
                {
                    var signal = baseSignals[i];
                    if (signal is not (SignalType.Buy or SignalType.Sell))
                    {
                        gated[i] = signal;
                        continue;
                    }

                    var longSide = signal == SignalType.Buy;
                    var signalClose = candles[i].CloseTime;
                    if (confirmation is not null)
                    {
                        var ci = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(confirmation, signalClose);
                        if (ci < 0)
                        {
                            continue;
                        }

                        var structure = confirmation.PriceAction().Structure[ci];
                        if (longSide
                            ? structure.Bias <= 0 && !structure.BosBull
                            : structure.Bias >= 0 && !structure.BosBear)
                        {
                            continue;
                        }
                    }

                    var contextIndex = ResearchStrategyEngine.LastClosedHigherTimeframeIndex(context, signalClose);
                    if (contextIndex < 0
                        || contextFast[contextIndex] is not { } fast
                        || contextSlow[contextIndex] is not { } slow
                        || (longSide ? fast <= slow : fast >= slow))
                    {
                        continue;
                    }

                    gated[i] = signal;
                }

                result[(candidate.CandidateId, symbol, timeframe)] = gated;
            }
        }

        Console.WriteLine($"{symbol}: precomputed signal arrays={result.Count}.");
        return result;
    }

    private static IReadOnlyList<AlphaSurvivorDecision> SelectSurvivors(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books)
    {
        return candidates.Select(candidate =>
        {
            var rows = books.Where(x => x.CandidateId == candidate.CandidateId && x.CostLabel == ResearchCostLabels.Base).ToList();
            var isRows = rows.Where(x => x.Phase == "IS").ToList();
            var validationRows = rows.Where(x => x.Phase == "VALIDATION").ToList();
            var ins = Combine(isRows);
            var val = Combine(validationRows);
            var isPf = ins.ProfitFactor;
            var valPf = val.ProfitFactor;
            var symbols = validationRows.Count(x => x.TradeCount > 0);
            var passed = ins.Trades >= 30
                && (isPf.Kind == ProfitFactorKind.NoLosses || isPf.IsFinite && isPf.Ratio >= 0.90m)
                && val.Trades >= 15
                && (valPf.Kind == ProfitFactorKind.NoLosses || valPf.IsFinite && valPf.Ratio > 1m)
                && val.NetPnl > 0m
                && symbols >= 5;
            return new AlphaSurvivorDecision(
                candidate.CandidateId,
                candidate.ParentTemplateKey ?? candidate.ParentStrategyId,
                candidate.SupportedTimeframes[0],
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

    private static IReadOnlyList<AlphaRobustnessRow> BuildRobustness(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books)
    {
        var interim = candidates.Select(candidate =>
        {
            var oos = books.Where(x => x.CandidateId == candidate.CandidateId && x.Phase == "OOS").ToList();
            var baseRows = oos.Where(x => x.CostLabel == ResearchCostLabels.Base).ToList();
            var highRows = oos.Where(x => x.CostLabel == ResearchCostLabels.High).ToList();
            var totals = Combine(baseRows);
            var high = Combine(highRows);
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
                && (high.Trades == 0 || high.ProfitFactor.Kind == ProfitFactorKind.NoWins
                    || high.ProfitFactor.IsFinite && high.ProfitFactor.Ratio < 1m);
            return new
            {
                Candidate = candidate,
                Totals = totals,
                Symbols = symbolNets.Length,
                Profitable = symbolNets.Count(x => x > 0),
                TopTwo = topTwo,
                SymbolFragile = symbolFragile,
                CostFragile = costFragile,
                Insufficient = totals.Trades < 50
            };
        }).ToArray();

        return interim.Select(x =>
        {
            var setup = x.Candidate.ParentTemplateKey ?? x.Candidate.ParentStrategyId;
            var positiveTfs = interim.Where(y =>
                    string.Equals(y.Candidate.ParentTemplateKey ?? y.Candidate.ParentStrategyId, setup, StringComparison.OrdinalIgnoreCase)
                    && y.Totals.Trades >= 50
                    && (y.Totals.ProfitFactor.Kind == ProfitFactorKind.NoLosses
                        || y.Totals.ProfitFactor.IsFinite && y.Totals.ProfitFactor.Ratio > 1m))
                .Select(y => y.Candidate.SupportedTimeframes[0])
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var candidatePositive = x.Totals.ProfitFactor.Kind == ProfitFactorKind.NoLosses
                || x.Totals.ProfitFactor.IsFinite && x.Totals.ProfitFactor.Ratio > 1m;
            var timeframeFragile = x.Totals.Trades >= 50 && candidatePositive && positiveTfs <= 1;
            var labels = new List<string>();
            if (x.Insufficient) labels.Add(ResearchStatuses.InsufficientData);
            if (x.SymbolFragile) labels.Add(ResearchStatuses.SymbolFragile);
            if (timeframeFragile) labels.Add(ResearchStatuses.TimeframeFragile);
            if (x.CostFragile) labels.Add(ResearchStatuses.CostFragile);
            if (labels.Count == 0) labels.Add(ResearchStatuses.ResearchComplete);
            return new AlphaRobustnessRow(
                setup,
                x.Candidate.CandidateId,
                x.Candidate.SupportedTimeframes[0],
                x.Totals.Trades,
                Pf(x.Totals),
                x.Totals.NetPnl,
                x.Symbols,
                x.Profitable,
                x.TopTwo,
                x.SymbolFragile,
                timeframeFragile,
                x.CostFragile,
                x.Insufficient,
                string.Join("|", labels));
        }).ToArray();
    }

    private static void WriteSummary(
        string root,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<AlphaRobustnessRow> robustness,
        AlphaFrozenSurvivors survivors)
    {
        var dir = PriceActionAlphaData.OutputDirectory(root);
        var coverage = JsonSerializer.Deserialize<AlphaDataArtifact>(
            File.ReadAllText(Path.Combine(dir, "coverage-post.json")), JsonOptions)?.Coverage ?? [];
        var summary = new
        {
            id = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
            confirmation = PriceActionAlphaData.Confirmation,
            liveOff = true,
            scalpingLiveOff = true,
            priceActionLiveOff = true,
            validatedForPaperAssigned = false,
            hypotheses = new[]
            {
                "162 setup/timeframe hypotheses frozen before OOS.",
                "Independent: 1m, 3m, 5m, 15m, 1h.",
                "MTF: 1m entry + 3m confirmation + 15m context.",
                "MTF: 3m entry + 5m confirmation + 15m context.",
                "MTF: 5m entry + 15m context.",
                "MTF: 15m entry + 1h context.",
                $"Pre-OOS survivors: {survivors.Payload.SurvivorCandidateIds.Count}; VALIDATED_FOR_PAPER: none."
            },
            books = books.Where(x =>
                x.Phase == "OOS" && x.CostLabel == ResearchCostLabels.Base).ToArray(),
            dataExpansion = coverage,
            robustness,
            survivorManifestSha256 = survivors.Sha256,
            candidates = candidates.Count,
            survivors = survivors.Payload.SurvivorCandidateIds.Count
        };
        WriteJson(Path.Combine(dir, "summary.json"), summary);
    }

    private static string RenderReport(
        string root,
        AlphaFrozenManifest manifest,
        AlphaFrozenSurvivors survivors,
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<AlphaRobustnessRow> robustness)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# PRICE ACTION ALPHA DISCOVERY — ALL TIMEFRAMES");
        sb.AppendLine();
        sb.AppendLine(PriceActionAlphaData.Confirmation);
        sb.AppendLine("No strategy was promoted. VALIDATED_FOR_PAPER = none. OOS was read only after the survivor manifest was persisted and hashed.");
        sb.AppendLine();
        sb.AppendLine("## Scope and frozen hypotheses");
        sb.AppendLine($"Universe: {string.Join(", ", manifest.Payload.Symbols)}.");
        sb.AppendLine($"Independent timeframes: {string.Join(", ", manifest.Payload.Timeframes)}.");
        sb.AppendLine($"Hypotheses: {candidates.Count}; pre-OOS manifest SHA-256 `{manifest.Sha256}`; survivor manifest SHA-256 `{survivors.Sha256}`.");
        sb.AppendLine($"Pre-OOS rule: {SurvivorRule}");
        sb.AppendLine($"Survivors: {survivors.Payload.SurvivorCandidateIds.Count}. No survivor means no OOS was opened for that hypothesis.");
        sb.AppendLine();
        sb.AppendLine("Fixed MTF structures: 1m+3m+15m; 3m+5m+15m; 5m+15m; 15m+1h. Entry is the existing causal setup; confirmation is last-closed market-structure/BOS alignment; context is last-closed EMA20/EMA50 alignment.");
        sb.AppendLine();
        sb.AppendLine("## Coverage and data quality");
        var coverage = JsonSerializer.Deserialize<AlphaDataArtifact>(
            File.ReadAllText(Path.Combine(PriceActionAlphaData.OutputDirectory(root), "coverage-post.json")), JsonOptions)?.Coverage ?? [];
        foreach (var group in coverage.GroupBy(x => x.Timeframe))
        {
            sb.AppendLine($"- {group.Key}: rows={group.Count()}, bars={group.Sum(x => (long)x.BarCount):N0}, coverage min={group.Min(x => x.CoveragePercent):0.000}% max={group.Max(x => x.CoveragePercent):0.000}%, gaps={group.Sum(x => x.GapCount)}, duplicates={group.Sum(x => x.DuplicateCount)}, quality passed={group.Count(x => x.QualityPassed)}/{group.Count()}.");
        }
        sb.AppendLine("Exact coin/timeframe first/last timestamps, bar counts, gaps, duplicates, OHLCV checks, taker coverage, and longest usable periods are in `artifacts/strategy-research/price-action-alpha/coverage-post.json` and `coverage-post.md`.");
        sb.AppendLine();
        sb.AppendLine("## Pre-OOS survivor decisions");
        sb.AppendLine("| Setup / hypothesis | IS n | IS PF | VAL n | VAL PF | VAL symbols | Decision |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var row in survivors.Payload.Decisions)
        {
            sb.AppendLine($"| {Md(row.CandidateId)} | {row.IsTrades} | {row.IsProfitFactor} | {row.ValidationTrades} | {row.ValidationProfitFactor} | {row.ValidationSymbolsWithTrades} | {row.Reason} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Robustness summary");
        sb.AppendLine("| Setup | Hypothesis | TF | OOS n | OOS PF | OOS net | Coins + / tested | Concentration | Status |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var row in robustness.Where(x => survivors.Payload.SurvivorCandidateIds.Contains(x.CandidateId, StringComparer.OrdinalIgnoreCase)))
        {
            sb.AppendLine($"| {row.Setup} | {Md(row.CandidateId)} | {row.Timeframe} | {row.OosTrades} | {row.OosProfitFactor} | {row.OosNet:0.00} | {row.SymbolsProfitable} / {row.SymbolsWithTrades} | {row.TopTwoAbsoluteNetShare:0.0%} | {row.Status} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Coin / timeframe / setup / direction / chronological results");
        sb.AppendLine("LONG and SHORT use the same W/|L| PF operator as Combined. BLOCK1–4 and WF rows are BASE cost; OOS has BASE/MILD/HIGH/STRESS.");
        sb.AppendLine("| Coin | Hypothesis | TF | Phase | Cost | Combined n/PF/net | LONG n/PF | SHORT n/PF | Status |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var row in books.OrderBy(x => x.CandidateId).ThenBy(x => x.Symbol).ThenBy(x => x.Phase).ThenBy(x => x.CostLabel))
        {
            sb.AppendLine($"| {row.Symbol} | {Md(row.CandidateId)} | {row.Timeframe} | {row.Phase} | {row.CostLabel} | "
                + $"{row.TradeCount}/{ResearchPf.Render(row.ProfitFactorState, row.ProfitFactor)}/{row.NetPnl:0.00} | "
                + $"{Side(row.LongTotals)} | {Side(row.ShortTotals)} | {row.Status} |");
        }
        sb.AppendLine();
        sb.AppendLine("## Data availability and causality");
        sb.AppendLine("- Signals use OHLCV and existing causal indicators/pattern events. Kline taker-buy volume is reported where present but is not invented.");
        sb.AppendLine("- Historical OI, funding, basis, liquidation, and order-book data are DATA_UNAVAILABLE for this run and were not fabricated.");
        sb.AppendLine("- Confirmation/context indices require `higher.CloseTime <= entry.CloseTime`; outcome labels, future returns, MFE/MAE and future swing confirmation do not enter signal features.");
        sb.AppendLine("- Missing candles remain missing. No 5m substitution for 1m/3m and no synthetic repair occurred.");
        sb.AppendLine();
        sb.AppendLine("## Commands");
        sb.AppendLine("`dotnet run --project tools/TradingPlatform.StrategyResearch -- --price-action-alpha-audit`");
        sb.AppendLine("`dotnet run --project tools/TradingPlatform.StrategyResearch -- --price-action-alpha-expand --max-parallel 2`");
        sb.AppendLine("`dotnet run --project tools/TradingPlatform.StrategyResearch -- --price-action-alpha-freeze`");
        sb.AppendLine("`dotnet run --project tools/TradingPlatform.StrategyResearch -- --price-action-alpha-discover --max-parallel 2`");
        sb.AppendLine();
        sb.AppendLine("## Safety");
        sb.AppendLine("LIVE, Scalping LIVE, Price Action LIVE and automatic PAPER promotion remained OFF. Frozen Five, Isolated LOW, margin mode, Risk Engine, Portfolio Risk and Execution were unchanged. No new production strategy was created.");
        return sb.ToString();
    }

    private static string Md(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);

    private static string Side(PnlTotals? totals)
    {
        if (totals is null)
        {
            return "0/N/A";
        }

        return $"{totals.Trades}/{Pf(totals)}";
    }

    private static PnlTotals Combine(IEnumerable<ResearchBookResult> rows) =>
        ResearchDiagnostics.CombinePf(rows.Select(x => x.CombinedTotals).Where(x => x is not null).Cast<PnlTotals>());

    private static string Pf(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return ResearchPf.Render(state, ratio);
    }

    private static List<ResearchBookResult> ReadBooks(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ResearchBookResult>>(File.ReadAllText(path), JsonOptions) ?? [];
    }

    private static HashSet<string> DoneKeys(IEnumerable<ResearchBookResult> rows) =>
        rows.Select(Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string Key(ResearchBookResult row) =>
        $"{row.CandidateId}|{row.Symbol}|{row.Timeframe}|{row.Phase}|{row.CostLabel}";

    private static List<ResearchBookResult> Ordered(IEnumerable<ResearchBookResult> rows) =>
        rows.GroupBy(Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Last())
            .OrderBy(x => x.CandidateId)
            .ThenBy(x => x.Symbol)
            .ThenBy(x => x.Timeframe)
            .ThenBy(x => x.Phase)
            .ThenBy(x => x.CostLabel)
            .ToList();

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

    private static AlphaFrozenManifest ReadManifest(string path) =>
        ReadAndVerify<AlphaManifestPayload, AlphaFrozenManifest>(
            path,
            x => x.Payload,
            x => x.Sha256);

    private static AlphaFrozenSurvivors ReadSurvivors(string path) =>
        ReadAndVerify<AlphaSurvivorPayload, AlphaFrozenSurvivors>(
            path,
            x => x.Payload,
            x => x.Sha256);

    private static AlphaFrozenManifest WriteFrozen(string path, AlphaManifestPayload payload)
    {
        var result = new AlphaFrozenManifest(payload, Sha256(JsonSerializer.Serialize(payload, JsonOptions)));
        WriteJson(path, result);
        return result;
    }

    private static AlphaFrozenSurvivors WriteFrozen(string path, AlphaSurvivorPayload payload)
    {
        var result = new AlphaFrozenSurvivors(payload, Sha256(JsonSerializer.Serialize(payload, JsonOptions)));
        WriteJson(path, result);
        return result;
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static string Sha256File(string path) => Sha256(File.ReadAllText(path));

    private static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static int ParseInt(string[] args, string name, int fallback)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(args[i + 1], out var value))
            {
                return value;
            }
        }

        return fallback;
    }
}
