using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Research.Alpha;
using TradingPlatform.Research.Framework;

namespace TradingPlatform.StrategyResearch.AlphaDiscovery;

/// <summary>
/// Phase 2 alpha discovery. Stages: panel → prereg → run (per family: IS screen, IS grid, IS plateau choice, one
/// Validation run, ±20% neighbours, bootstrap) → regimes (family G) → gates (multiple testing, verdicts, OOS for
/// PROMISING only). Nothing here touches live trading.
/// </summary>
internal static class AlphaRunner
{
    public static readonly DateTimeOffset PanelStart = new(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset PanelEnd = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset MetricsStart = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
    private static readonly JsonSerializerOptions Line = new() { WriteIndented = false, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    public static int Run(string root, string stage, string? families)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var paths = new Paths(root);
        return stage switch
        {
            "panel" => BuildPanel(paths),
            "prereg" => Preregister(paths.Prereg, PreregLines()),
            "prereg2b" => Preregister(paths.Prereg2b, ExtensionLines()),
            "run" => RunFamilies(paths, families),
            "regimes" => RunRegimes(paths),
            "costcurve" => CostCurve(paths),
            "gates" => Gates(paths),
            "report" => Report(paths),
            _ => throw new ArgumentException($"Unknown stage {stage}. Use panel | prereg | prereg2b | run | regimes | costcurve | gates | report.")
        };
    }

    private sealed class Paths(string root)
    {
        public string Vision { get; } = Path.Combine(root, "artifacts", "data", "vision");
        public string PanelFile => Path.Combine(Vision, "panel-1h.bin");
        public string Out { get; } = Path.Combine(root, "artifacts", "research", "alpha-discovery");
        public string FamiliesDir => Path.Combine(Out, "families");
        public string Prereg => Path.Combine(Out, "preregistration.jsonl");
        public string Prereg2b => Path.Combine(Out, "preregistration-2b.jsonl");
        public string Registry { get; } = Path.Combine(root, "artifacts", "research", "registry", "experiments.jsonl");
        public string Vault { get; } = Path.Combine(root, "artifacts", "research", "registry", "oos-access.jsonl");
    }

    // ---------------------------------------------------------------- panel

    private static int BuildPanel(Paths paths)
    {
        var sw = Stopwatch.StartNew();
        var symbols = VisionBulkDownloader.CryptoPerpetuals(paths.Vision)
            .Where(s => Directory.Exists(Path.Combine(paths.Vision, "klines-1h", s)))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var panel = VisionPanelLoader.Load(paths.Vision, PanelStart, PanelEnd, symbols);
        panel.Save(paths.PanelFile);
        Console.WriteLine($"Panel {panel.Coins} coins × {panel.Hours} hours built in {sw.Elapsed:mm\\:ss}. Fingerprint {panel.Fingerprint()}.");
        var universe = new PanelUniverse(panel);
        WriteDataSummary(paths, panel, universe);
        return 0;
    }

    private static void WriteDataSummary(Paths paths, HourlyPanel panel, PanelUniverse universe)
    {
        var current = File.Exists(Path.Combine(paths.Vision, "exchangeInfo-2026-10-05.json"))
            ? JsonDocument.Parse(File.ReadAllText(Path.Combine(paths.Vision, "exchangeInfo-2026-10-05.json"))).RootElement
                .GetProperty("symbols").EnumerateArray()
                .Where(s => s.GetProperty("status").GetString() == "TRADING")
                .Select(s => s.GetProperty("symbol").GetString()!)
                .ToHashSet(StringComparer.Ordinal)
            : [];
        var lastBar = new int[panel.Coins];
        var bars = new int[panel.Coins];
        var funding = new int[panel.Coins];
        var takerNonZero = new int[panel.Coins];
        for (var c = 0; c < panel.Coins; c++)
        {
            lastBar[c] = -1;
            for (var t = 0; t < panel.Hours; t++)
            {
                if (!float.IsNaN(panel.Close[c][t]))
                {
                    bars[c]++;
                    lastBar[c] = t;
                    if (panel.TakerBuyQuote[c][t] > 0)
                    {
                        takerNonZero[c]++;
                    }
                }

                if (!float.IsNaN(panel.Funding[c][t]))
                {
                    funding[c]++;
                }
            }
        }

        var delisted = Enumerable.Range(0, panel.Coins).Where(c => !current.Contains(panel.Symbols[c])).ToList();
        var eligibleByMonth = new SortedDictionary<string, double>(StringComparer.Ordinal);
        for (var d = 0; d < panel.Days; d += 1)
        {
            var key = panel.Start.AddDays(d).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var count = Enumerable.Range(0, panel.Coins).Count(c => universe.DayEligible[c][d]);
            eligibleByMonth[key] = eligibleByMonth.TryGetValue(key, out var v) ? Math.Max(v, count) : count;
        }

        var everEligible = Enumerable.Range(0, panel.Coins).Count(c => universe.DayEligible[c].Any(x => x));
        var delistedEligible = delisted.Count(c => universe.DayEligible[c].Any(x => x));
        var summary = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            fingerprint = panel.Fingerprint(),
            start = panel.Start,
            hours = panel.Hours,
            coins = panel.Coins,
            barsTotal = bars.Sum(),
            takerNonZeroShare = bars.Sum() == 0 ? 0 : takerNonZero.Sum() / (double)bars.Sum(),
            fundingSettlements = funding.Sum(),
            coinsWithFunding = funding.Count(f => f > 0),
            notCurrentlyTrading = delisted.Count,
            notCurrentlyTradingEverEligible = delistedEligible,
            everEligible,
            maxEligiblePerMonth = eligibleByMonth,
            notCurrentlyTradingSymbols = delisted.Select(c => panel.Symbols[c]).ToList()
        };
        Directory.CreateDirectory(paths.Out);
        File.WriteAllText(Path.Combine(paths.Out, "data-summary.json"), JsonSerializer.Serialize(summary, Json));
        Console.WriteLine($"Coins {panel.Coins}, ever eligible {everEligible}, not trading now {delisted.Count} (eligible at some point {delistedEligible}). Taker non-zero share {summary.takerNonZeroShare:P1}.");
    }

    private static (HourlyPanel Panel, PanelUniverse Universe, PanelFeatures Features, string Hash) LoadPanel(Paths paths)
    {
        var sw = Stopwatch.StartNew();
        var panel = HourlyPanel.Load(paths.PanelFile);
        var universe = new PanelUniverse(panel);
        var features = new PanelFeatures(universe);
        var hash = panel.Fingerprint();
        Console.WriteLine($"Loaded panel {panel.Coins}×{panel.Hours} ({hash}) in {sw.Elapsed:mm\\:ss}.");
        return (panel, universe, features, hash);
    }

    // ---------------------------------------------------------------- pre-registration

    private sealed record PreregLine(string Family, string Name, string Priority, string Kind, string Id, string Variant, IReadOnlyDictionary<string, double> Params, string[] Perturb, string Hypothesis, string Mechanism);

    private static IEnumerable<PreregLine> PreregLines()
    {
        foreach (var f in AlphaFamilies.All)
        {
            foreach (var s in f.Screens)
            {
                yield return new PreregLine(f.Id, f.Name, f.Priority, "SCREEN:" + s.Kind, $"{f.Id}.screen.{s.Name}", "screen",
                    new Dictionary<string, double> { ["horizon"] = s.Horizon, ["step"] = s.Step }, [], f.Hypothesis, f.Mechanism);
            }

            foreach (var g in f.Grid)
            {
                yield return new PreregLine(f.Id, f.Name, f.Priority, g.Kind, g.Id, g.Variant, g.Params, g.Perturb, f.Hypothesis, f.Mechanism);
            }
        }

        foreach (var (name, _) in AlphaFamilies.Regimes)
        {
            yield return new PreregLine("G", "Market-regime conditioner", "P0", "GATE", $"G.{name}", name, new Dictionary<string, double>(), [],
                "Applied to the IS-chosen configuration of every P0/P1 family that passes its screen: trade only while the regime holds.",
                "Edges in crypto are regime dependent (trend vs chop, breadth, dispersion, leverage); conditioning can isolate the regime where an edge lives.");
        }

        yield return new PreregLine("RULE", "Sign rule", "-", "RULE", "RULE.sign", "sign", new Dictionary<string, double>(), [],
            "If the strongest IS screen row of a family has |t| ≥ 2 with the opposite sign to the registered hypothesis, the full grid is also run with sign = −1 and every flipped point counts as a trial.", "");
        yield return new PreregLine("RULE", "Stop rule", "-", "RULE", "RULE.stop", "stop", new Dictionary<string, double>(), [],
            "A family whose IS screen has no row with |t| ≥ 2 (IC t for XS, day-clustered excess t for events) stops after the screen.", "");
        yield return new PreregLine("RULE", "Choice rule", "-", "RULE", "RULE.choice", "choice", new Dictionary<string, double>(), [],
            "The chosen configuration is the best IS net-CONSERVATIVE daily Sharpe among points with positive Sharpe whose one-parameter grid neighbours all have positive IS Sharpe. No such point: family rejected in IS.", "");
    }

    private static IEnumerable<PreregLine> ExtensionLines()
    {
        foreach (var f in AlphaFamilies.Extension)
        {
            foreach (var s in f.Screens)
            {
                yield return new PreregLine(f.Id, f.Name, f.Priority, "SCREEN:" + s.Kind, $"{f.Id}.screen.{s.Name}", "screen",
                    new Dictionary<string, double> { ["horizon"] = s.Horizon, ["step"] = s.Step }, [], f.Hypothesis, f.Mechanism);
            }

            foreach (var g in f.Grid)
            {
                yield return new PreregLine(f.Id, f.Name, f.Priority, g.Kind, g.Id, g.Variant, g.Params, g.Perturb, f.Hypothesis, f.Mechanism);
            }
        }

        yield return new PreregLine("RULE", "Phase 2b scope", "-", "RULE", "RULE.2b", "scope", new Dictionary<string, double>(), [],
            "Registered after phase 2 IS results (no Validation or OOS result exists for any family). Same stop, sign, choice and gate rules as phase 2. Phase 2b trials are added to the phase 2 trial count for DSR and BH-FDR. Family G is not re-run on X or M.", "");
    }

    private static int Preregister(string path, IEnumerable<PreregLine> source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lines = source.Select(l => JsonSerializer.Serialize(l, Line)).ToList();
        var body = string.Join("\n", lines) + "\n";
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Split("\n#", 2)[0].TrimEnd('\n') + "\n";
            if (Hash(existing) != Hash(body))
            {
                throw new InvalidOperationException($"{Path.GetFileName(path)} exists and differs from the code. Registered hypotheses are not edited after the fact.");
            }

            Console.WriteLine($"Pre-registration unchanged ({lines.Count} lines, {Hash(body)}).");
            return 0;
        }

        File.WriteAllText(path, body + $"# written {DateTimeOffset.UtcNow:O} sha256 {Hash(body)}\n");
        Console.WriteLine($"Pre-registered {lines.Count} lines ({Hash(body)}) at {path}.");
        return 0;
    }

    private static void RequirePrereg(Paths paths, bool extension = false)
    {
        var (path, source) = extension ? (paths.Prereg2b, ExtensionLines()) : (paths.Prereg, PreregLines());
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Run --alpha {(extension ? "prereg2b" : "prereg")} first: nothing is tested before it is registered.");
        }

        var body = string.Join("\n", source.Select(l => JsonSerializer.Serialize(l, Line))) + "\n";
        var existing = File.ReadAllText(path).Split("\n#", 2)[0].TrimEnd('\n') + "\n";
        if (Hash(existing) != Hash(body))
        {
            throw new InvalidOperationException($"The grid in code no longer matches {Path.GetFileName(path)}.");
        }
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)), 0, 8).ToLowerInvariant();

    // ---------------------------------------------------------------- family runs

    /// <summary><paramref name="IcSkip1"/> is a diagnostic only (not used by the stop rule): the same IC with one bar between signal and forward return.</summary>
    internal sealed record ScreenResult(string Name, string Kind, int Horizon, int Step, IcSummary? Ic, EventScreen? Event, double T, IcSummary? IcSkip1 = null);

    internal sealed record RunRecord(
        string Id,
        string Variant,
        string Kind,
        IReadOnlyDictionary<string, double> Params,
        string Split,
        AlphaSummary Summary,
        EventSummary? Events,
        double ProfitFactorForGate,
        int TradesForGate);

    internal sealed record FamilyOutcome(
        string Family,
        string Name,
        string Priority,
        string Hypothesis,
        string Mechanism,
        string Data,
        string DataHash,
        int FromDay,
        int ToDay,
        int InSampleEndDay,
        int ValidationEndDay,
        IReadOnlyList<ScreenResult> Screens,
        bool PassedScreen,
        bool Flipped,
        IReadOnlyList<RunRecord> InSample,
        string? ChosenId,
        string IsDecision,
        IReadOnlyList<RunRecord> Validation,
        IReadOnlyList<RunRecord> Neighbours,
        StabilityScore? Stability,
        BootstrapSummary? Bootstrap,
        IReadOnlyList<RunRecord> InSampleChosenAllCosts,
        double ElapsedSeconds);

    private static int RunFamilies(Paths paths, string? which)
    {
        RequirePrereg(paths);
        var (panel, universe, features, hash) = LoadPanel(paths);
        var registry = new ExperimentRegistry(paths.Registry);
        var selected = (which ?? "A,B,C,F,H,E,I,J,K,D").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (selected.Any(id => AlphaFamilies.Extension.Any(f => f.Id == id)))
        {
            RequirePrereg(paths, extension: true);
        }

        MetricsPanel? metrics = null;
        AlphaContext? liquid = null;
        foreach (var id in selected)
        {
            var family = AlphaFamilies.Everything.Single(f => f.Id == id);
            AlphaSplits splits;
            if (family.UsesMetrics)
            {
                var metricsDir = Path.Combine(paths.Vision, "metrics");
                if (!Directory.Exists(metricsDir))
                {
                    Console.WriteLine($"{id}: metrics not downloaded. NOT TESTABLE.");
                    continue;
                }

                metrics ??= MetricsPanel.Load(panel, metricsDir);
                splits = AlphaSplits.For((int)(MetricsStart - PanelStart).TotalDays, panel.Days);
            }
            else
            {
                splits = AlphaSplits.For(0, panel.Days);
            }

            var ctx = new AlphaContext(universe, features, splits, metrics);
            if (family.Id is "X" or "M")
            {
                if (liquid is null)
                {
                    var liquidUniverse = new PanelUniverse(panel, AlphaFamilies.LiquidMinimumQuoteVolume);
                    liquid = new AlphaContext(liquidUniverse, new PanelFeatures(liquidUniverse), splits);
                    liquid.Liquid = liquid;
                }

                ctx.Liquid = liquid;
                if (family.LiquidUniverse)
                {
                    ctx = liquid;
                }
            }

            var outcome = RunFamily(ctx, family, registry, hash);
            Directory.CreateDirectory(paths.FamiliesDir);
            File.WriteAllText(Path.Combine(paths.FamiliesDir, $"{id}.json"), JsonSerializer.Serialize(outcome, Json));
            Console.WriteLine($"{id}: {outcome.IsDecision} ({outcome.ElapsedSeconds:0}s)");
        }

        return 0;
    }

    private static FamilyOutcome RunFamily(AlphaContext ctx, FamilySpec family, ExperimentRegistry registry, string hash)
    {
        var sw = Stopwatch.StartNew();
        var isWindow = SimWindow.InSample(ctx.Splits);
        var warmup = 24 * 31;
        Console.WriteLine($"[{family.Id}] screen ({family.Screens.Count} rows) on IS days {ctx.Splits.FromDay}..{ctx.Splits.InSampleEndDay}");
        var screens = family.Screens.AsParallel().AsOrdered().Select(s =>
        {
            if (s.Kind == "XS")
            {
                var signal = s.Signal!(ctx);
                var ic = InformationScreen.CrossSectional(ctx.U, signal, isWindow, s.Horizon, warmup: warmup);
                var skip = InformationScreen.CrossSectional(ctx.U, signal, isWindow, s.Horizon, warmup: warmup, skip: 1);
                return new ScreenResult(s.Name, s.Kind, s.Horizon, s.Step, ic, null, ic.IcTStat, skip);
            }

            var ev = InformationScreen.Events(ctx.U, s.Trigger!(ctx), isWindow, s.Horizon, s.Step, warmup);
            return new ScreenResult(s.Name, s.Kind, s.Horizon, s.Step, null, ev, ev.ExcessTStat);
        }).ToList();
        foreach (var s in screens)
        {
            Console.WriteLine($"  {s.Name,-40} t={s.T,6:0.00} " + (s.Ic is { } ic ? $"IC={ic.MeanIc:0.0000} spread={ic.MeanSpreadBp,7:0.0}bp n={ic.Periods} coins={ic.MeanCoins:0} | skip1 t={s.IcSkip1!.IcTStat:0.00} IC={s.IcSkip1.MeanIc:0.0000} spread={s.IcSkip1.MeanSpreadBp:0.0}bp" : $"events={s.Event!.Events} mean={s.Event.MeanBp:0.0}bp excess={s.Event.MeanExcessBp:0.0}bp"));
        }

        var strongest = screens.OrderByDescending(s => Math.Abs(s.T)).FirstOrDefault();
        var passed = strongest is not null && Math.Abs(strongest.T) >= 2;
        var flipped = passed && strongest!.T <= -2;
        var empty = Array.Empty<RunRecord>();
        if (!passed)
        {
            return new FamilyOutcome(family.Id, family.Name, family.Priority, family.Hypothesis, family.Mechanism, family.Data, hash,
                ctx.Splits.FromDay, ctx.Splits.ToDay, ctx.Splits.InSampleEndDay, ctx.Splits.ValidationEndDay, screens, false, false,
                empty, null, "STOPPED: no IS screen row with |t| ≥ 2", empty, empty, null, null, empty, sw.Elapsed.TotalSeconds);
        }

        var grid = family.Grid.ToList();
        if (flipped)
        {
            grid.AddRange(family.Grid.Select(g => g.With("sign", -1)));
        }

        Console.WriteLine($"[{family.Id}] IS grid {grid.Count} points (flipped={flipped})");
        var inSample = grid.AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Max(2, Environment.ProcessorCount / 2))
            .Select(g => Simulate(ctx, g, isWindow, CostProfileKind.Conservative))
            .ToList();
        foreach (var r in inSample)
        {
            Register(registry, family, r, hash, "IS-trial");
            Console.WriteLine($"  {r.Id,-70} SR/yr={r.Summary.SharpeAnnual,6:0.00} PF={r.ProfitFactorForGate,5:0.00} net={r.Summary.Net,10:0} gross={r.Summary.Gross,10:0} trades={r.TradesForGate}");
        }

        var chosen = ChoosePlateau(grid, inSample);
        if (chosen is null)
        {
            var best = inSample.OrderByDescending(r => r.Summary.SharpePerDay).First();
            return new FamilyOutcome(family.Id, family.Name, family.Priority, family.Hypothesis, family.Mechanism, family.Data, hash,
                ctx.Splits.FromDay, ctx.Splits.ToDay, ctx.Splits.InSampleEndDay, ctx.Splits.ValidationEndDay, screens, true, flipped,
                inSample, null, $"REJECTED IN IS: no positive plateau under CONSERVATIVE costs (best {best.Id} SR/yr {best.Summary.SharpeAnnual:0.00})",
                empty, empty, null, null, empty, sw.Elapsed.TotalSeconds);
        }

        var chosenConfig = grid.Single(g => g.Id == chosen.Id);
        var isAllCosts = new[] { CostProfileKind.Base, CostProfileKind.Stress }.Select(k => Simulate(ctx, chosenConfig, isWindow, k)).Prepend(chosen).ToList();
        var valWindow = SimWindow.Validation(ctx.Splits);
        Console.WriteLine($"[{family.Id}] Validation once: {chosen.Id}");
        var validation = CostProfiles.All.Select(k => Simulate(ctx, chosenConfig, valWindow, k)).ToList();
        foreach (var v in validation)
        {
            Register(registry, family, v, hash, "VALIDATION");
            Console.WriteLine($"  {v.Summary.Cost,-12} SR/yr={v.Summary.SharpeAnnual,6:0.00} PF={v.ProfitFactorForGate,5:0.00} net={v.Summary.Net,10:0} trades={v.TradesForGate}");
        }

        var neighbourConfigs = chosenConfig.Perturb
            .SelectMany(key => Robustness.Neighbours((decimal)chosenConfig.Params[key], IsInteger(key)).Select(v => chosenConfig.With(key, (double)v)))
            .ToList();
        var neighbours = neighbourConfigs.AsParallel().AsOrdered().Select(n => Simulate(ctx, n, valWindow, CostProfileKind.Conservative)).ToList();
        foreach (var n in neighbours)
        {
            Register(registry, family, n with { Split = "Validation-neighbour" }, hash, "NEIGHBOUR");
        }

        var cons = validation.Single(v => v.Summary.Cost == "CONSERVATIVE");
        var stability = Robustness.Stability(Dec(cons.ProfitFactorForGate), neighbours.Select(n => Dec(n.ProfitFactorForGate)).ToList());
        var sim = Simulate(ctx, chosenConfig, valWindow, CostProfileKind.Conservative, keepDaily: true);
        var bootstrap = Robustness.BlockBootstrap(sim.Daily!, blockLength: 5, draws: 2000);
        return new FamilyOutcome(family.Id, family.Name, family.Priority, family.Hypothesis, family.Mechanism, family.Data, hash,
            ctx.Splits.FromDay, ctx.Splits.ToDay, ctx.Splits.InSampleEndDay, ctx.Splits.ValidationEndDay, screens, true, flipped,
            inSample, chosen.Id, $"VALIDATED ONCE: {chosen.Id}", validation, neighbours, stability, bootstrap, isAllCosts, sw.Elapsed.TotalSeconds);
    }

    private static bool IsInteger(string key) => key is "lb" or "hold" or "trend";

    private static decimal Dec(double v) => double.IsFinite(v) ? (decimal)Math.Min(v, 1_000d) : v > 0 ? 1_000m : 0m;

    private static RunRecord? ChoosePlateau(IReadOnlyList<AlphaConfig> grid, IReadOnlyList<RunRecord> results)
    {
        RunRecord? best = null;
        for (var i = 0; i < grid.Count; i++)
        {
            var r = results[i];
            if (r.Summary.SharpePerDay <= 0)
            {
                continue;
            }

            var neighbours = Enumerable.Range(0, grid.Count).Where(j => j != i && OneStep(grid[i], grid[j])).ToList();
            if (neighbours.Count == 0 || neighbours.Any(j => results[j].Summary.SharpePerDay <= 0))
            {
                continue;
            }

            if (best is null || r.Summary.SharpePerDay > best.Summary.SharpePerDay)
            {
                best = r;
            }
        }

        return best;
    }

    private static bool OneStep(AlphaConfig a, AlphaConfig b)
    {
        if (a.Variant != b.Variant || a.Params.Count != b.Params.Count)
        {
            return false;
        }

        var diff = 0;
        foreach (var (k, v) in a.Params)
        {
            if (!b.Params.TryGetValue(k, out var w))
            {
                return false;
            }

            if (v != w)
            {
                diff++;
            }
        }

        return diff == 1 && a.Params["sign"] == b.Params["sign"];
    }

    private sealed record SimOutput(RunRecord Record, double[]? Daily);

    private static RunRecord Simulate(AlphaContext ctx, AlphaConfig config, SimWindow window, CostProfileKind cost) =>
        Simulate(ctx, config, window, cost, keepDaily: false).Record;

    private static SimOutput Simulate(AlphaContext ctx, AlphaConfig config, SimWindow window, CostProfileKind cost, bool keepDaily)
    {
        var model = config.Factory(ctx, config.Params);
        var result = AlphaSimulator.Run(ctx.U, model, window, new SimOptions(cost));
        var summary = AlphaMetrics.Summarize(result, ctx.P);
        EventSummary? events = null;
        if (model is EventModel ev)
        {
            ev.Flush(window.To - 1);
            events = AlphaMetrics.Events(ctx.U, ev.Trades, cost, result.Book / 20d, window.To);
        }

        var pf = events?.ProfitFactor ?? summary.ProfitFactorDaily;
        var trades = events?.Trades ?? summary.Entries;
        return new SimOutput(new RunRecord(config.Id, config.Variant, config.Kind, config.Params, window.Split, summary, events, pf, trades), keepDaily ? result.DailyReturns : null);
    }

    private static void Register(ExperimentRegistry registry, FamilySpec family, RunRecord r, string hash, string verdict) =>
        registry.Append(new ExperimentRecord(
            Guid.NewGuid().ToString("N")[..12],
            DateTimeOffset.UtcNow,
            "alpha:" + family.Id,
            r.Id,
            family.Hypothesis,
            r.Params.ToDictionary(p => p.Key, p => AlphaConfig.Fmt(p.Value)),
            hash,
            r.Summary.Cost,
            r.Split,
            r.Summary.SharpePerDay,
            r.Summary.Days,
            double.IsFinite(r.ProfitFactorForGate) ? (decimal)Math.Round(r.ProfitFactorForGate, 4) : null,
            r.TradesForGate,
            (decimal)Math.Round(r.Summary.Net, 2),
            verdict));

    // ---------------------------------------------------------------- family G (regime conditioner)

    private static int RunRegimes(Paths paths)
    {
        RequirePrereg(paths);
        var (panel, universe, features, hash) = LoadPanel(paths);
        var registry = new ExperimentRegistry(paths.Registry);
        var spec = new FamilySpec("G", "Market-regime conditioner", "P0", "inherits", "Regime-dependent edges.", "Conditioning a screened family on a causal market regime improves its net result.", "1h Vision klines + funding", false, [], []);
        var bases = Directory.Exists(paths.FamiliesDir)
            ? Directory.GetFiles(paths.FamiliesDir, "*.json").Select(f => JsonSerializer.Deserialize<FamilyOutcome>(File.ReadAllText(f), Json)!)
                .Where(o => o.PassedScreen && o.Family is not "G" and not "D" and not "X" and not "M" && o.InSample.Count > 0).ToList()
            : [];
        var splits = AlphaSplits.For(0, panel.Days);
        var ctx = new AlphaContext(universe, features, splits);
        var isWindow = SimWindow.InSample(splits);
        var runs = new List<RunRecord>();
        var configs = new List<AlphaConfig>();
        foreach (var b in bases)
        {
            var baseId = b.ChosenId ?? b.InSample.OrderByDescending(r => r.Summary.SharpePerDay).First().Id;
            var baseConfig = Expand(AlphaFamilies.All.Single(f => f.Id == b.Family)).Single(g => g.Id == baseId);
            foreach (var (name, gate) in AlphaFamilies.Regimes)
            {
                configs.Add(baseConfig with
                {
                    Family = "G",
                    Variant = $"{b.Family}.{baseConfig.Variant}|{name}",
                    Factory = (c, p) => new GatedModel(baseConfig.Factory(c, p), gate(c))
                });
            }
        }

        Console.WriteLine($"[G] {configs.Count} regime-gated IS runs over {bases.Count} screened families");
        runs = configs.AsParallel().AsOrdered().WithDegreeOfParallelism(Math.Max(2, Environment.ProcessorCount / 2))
            .Select(g => Simulate(ctx, g, isWindow, CostProfileKind.Conservative)).ToList();
        foreach (var r in runs)
        {
            Register(registry, spec, r, hash, "IS-trial");
            Console.WriteLine($"  {r.Id,-80} SR/yr={r.Summary.SharpeAnnual,6:0.00} PF={r.ProfitFactorForGate,5:0.00} net={r.Summary.Net,10:0}");
        }

        var positive = runs.Select((r, i) => (r, i)).Where(x => x.r.Summary.SharpePerDay > 0).OrderByDescending(x => x.r.Summary.SharpePerDay).ToList();
        string decision;
        RunRecord? chosen = null;
        var validation = new List<RunRecord>();
        var neighbours = new List<RunRecord>();
        StabilityScore? stability = null;
        BootstrapSummary? bootstrap = null;
        if (positive.Count == 0)
        {
            decision = "REJECTED IN IS: no regime-gated configuration is positive under CONSERVATIVE costs";
        }
        else
        {
            chosen = positive[0].r;
            var config = configs[positive[0].i];
            var valWindow = SimWindow.Validation(splits);
            validation = CostProfiles.All.Select(k => Simulate(ctx, config, valWindow, k)).ToList();
            foreach (var v in validation)
            {
                Register(registry, spec, v, hash, "VALIDATION");
            }

            var neighbourConfigs = config.Perturb
                .SelectMany(key => Robustness.Neighbours((decimal)config.Params[key], IsInteger(key)).Select(v => config.With(key, (double)v)))
                .ToList();
            neighbours = neighbourConfigs.AsParallel().AsOrdered().Select(n => Simulate(ctx, n, valWindow, CostProfileKind.Conservative)).ToList();
            foreach (var n in neighbours)
            {
                Register(registry, spec, n with { Split = "Validation-neighbour" }, hash, "NEIGHBOUR");
            }

            var cons = validation.Single(v => v.Summary.Cost == "CONSERVATIVE");
            stability = Robustness.Stability(Dec(cons.ProfitFactorForGate), neighbours.Select(n => Dec(n.ProfitFactorForGate)).ToList());
            bootstrap = Robustness.BlockBootstrap(Simulate(ctx, config, valWindow, CostProfileKind.Conservative, keepDaily: true).Daily!, 5, 2000);
            decision = $"VALIDATED ONCE: {chosen.Id}";
        }

        var outcome = new FamilyOutcome("G", spec.Name, "P0", spec.Hypothesis, spec.Mechanism, spec.Data, hash, splits.FromDay, splits.ToDay,
            splits.InSampleEndDay, splits.ValidationEndDay, [], bases.Count > 0, false, runs, chosen?.Id, decision, validation, neighbours, stability, bootstrap, [], 0);
        File.WriteAllText(Path.Combine(paths.FamiliesDir, "G.json"), JsonSerializer.Serialize(outcome, Json));
        Console.WriteLine($"G: {decision}");
        return 0;
    }

    private static IEnumerable<AlphaConfig> Expand(FamilySpec f) => f.Grid.Concat(f.Grid.Select(g => g.With("sign", -1)));

    // ---------------------------------------------------------------- cost sensitivity (diagnostic, not trials)

    internal sealed record CostRow(string Family, string Id, double GrossPnl, double GrossSharpeAnnual, double BaseSharpeAnnual, double BaseNet,
        double ConservativeSharpeAnnual, double ConservativeNet, double CostsConservative, double FundingConservative, double TurnoverPerDay, double CostToGross);

    /// <summary>
    /// For each family, the three IS grid points with the largest gross PnL, re-run on IS under BASE. Registered as
    /// split "IS-diagnostic" so they do not add trials; nothing here selects a configuration.
    /// </summary>
    private static int CostCurve(Paths paths)
    {
        var (panel, universe, features, hash) = LoadPanel(paths);
        var registry = new ExperimentRegistry(paths.Registry);
        var full = new AlphaContext(universe, features, AlphaSplits.For(0, panel.Days));
        var liquidUniverse = new PanelUniverse(panel, AlphaFamilies.LiquidMinimumQuoteVolume);
        var liquid = new AlphaContext(liquidUniverse, new PanelFeatures(liquidUniverse), full.Splits);
        liquid.Liquid = liquid;
        full.Liquid = liquid;
        MetricsPanel? metrics = null;
        var rows = new List<CostRow>();
        foreach (var file in Directory.GetFiles(paths.FamiliesDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            var o = JsonSerializer.Deserialize<FamilyOutcome>(File.ReadAllText(file), Json)!;
            if (o.InSample.Count == 0 || o.Family == "G")
            {
                continue;
            }

            var spec = AlphaFamilies.Everything.Single(f => f.Id == o.Family);
            var ctx = spec.LiquidUniverse ? liquid : full;
            if (spec.UsesMetrics)
            {
                metrics ??= MetricsPanel.Load(panel, Path.Combine(paths.Vision, "metrics"));
                ctx = new AlphaContext(universe, features, AlphaSplits.For(o.FromDay, o.ToDay), metrics);
            }

            var configs = Expand(spec).ToDictionary(g => g.Id, StringComparer.Ordinal);
            var window = SimWindow.InSample(ctx.Splits);
            foreach (var cons in o.InSample.OrderByDescending(r => r.Summary.Gross).Take(3))
            {
                var config = configs[cons.Id];
                var baseRun = Simulate(ctx, config, window, CostProfileKind.Base);
                Register(registry, spec, baseRun with { Split = "IS-diagnostic" }, hash, "COST-DIAGNOSTIC");
                var costs = cons.Summary.Fees + cons.Summary.Slippage;
                rows.Add(new CostRow(o.Family, cons.Id, cons.Summary.Gross, baseRun.Summary.GrossSharpeAnnual, baseRun.Summary.SharpeAnnual, baseRun.Summary.Net,
                    cons.Summary.SharpeAnnual, cons.Summary.Net, costs, cons.Summary.Funding, cons.Summary.TurnoverPerDay,
                    cons.Summary.Gross > 0 ? costs / cons.Summary.Gross : double.NaN));
                Console.WriteLine($"{o.Family} {cons.Id,-62} gross={cons.Summary.Gross,9:0} grossSR={baseRun.Summary.GrossSharpeAnnual,5:0.00} BASE SR={baseRun.Summary.SharpeAnnual,6:0.00} CONS SR={cons.Summary.SharpeAnnual,6:0.00} costs={costs,9:0} funding={cons.Summary.Funding,8:0} turnover/day={cons.Summary.TurnoverPerDay:0.00}");
            }
        }

        File.WriteAllText(Path.Combine(paths.Out, "cost-sensitivity.json"), JsonSerializer.Serialize(rows, Json));
        return 0;
    }

    /// <summary>Flattens the book while the regime is off; the inner model still sees every decision so its state stays causal.</summary>
    private sealed class GatedModel(ITargetModel inner, HourGate gate) : ITargetModel
    {
        public bool IsDecision(int hour) => inner.IsDecision(hour);

        public void Targets(int hour, double[] target)
        {
            inner.Targets(hour, target);
            if (!gate(hour))
            {
                Array.Clear(target);
            }
        }
    }

    // ---------------------------------------------------------------- report artifacts

    private static int Report(Paths paths)
    {
        var outcomes = Directory.GetFiles(paths.FamiliesDir, "*.json")
            .Select(f => JsonSerializer.Deserialize<FamilyOutcome>(File.ReadAllText(f), Json)!)
            .OrderBy(o => Order(o.Family))
            .ToList();
        var registry = new ExperimentRegistry(paths.Registry);
        var alpha = registry.Records.Where(r => r.Family.StartsWith("alpha:", StringComparison.Ordinal)).ToList();
        var isTrials = alpha.Where(r => r.Split == "IS").Select(ExperimentRegistry.ConfigurationKey).Distinct(StringComparer.Ordinal).Count();
        var screenRows = outcomes.Sum(o => o.Screens.Count);
        var gatesPath = Path.Combine(paths.Out, "gates.json");
        var gates = File.Exists(gatesPath) ? JsonDocument.Parse(File.ReadAllText(gatesPath)).RootElement.Clone() : (JsonElement?)null;
        var cost = File.Exists(Path.Combine(paths.Out, "cost-sensitivity.json"))
            ? JsonSerializer.Deserialize<List<CostRow>>(File.ReadAllText(Path.Combine(paths.Out, "cost-sensitivity.json")), Json)!
            : [];
        var oosAlpha = new OosVault(paths.Vault).Accesses.Count(a => a.Strategy.StartsWith("alpha:", StringComparison.Ordinal));
        var promising = gates is { } g && g.TryGetProperty("rows", out var rowsEl)
            ? rowsEl.EnumerateArray().Count(r => r.GetProperty("Verdict").GetString() == ResearchStatuses.Promising)
            : 0;
        var verdict = promising > 0 ? "B. PROMISING BUT NOT VALIDATED" : "C. NO VALIDATED EDGE FOUND";

        var results = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            verdict,
            isTrials,
            screenRows,
            validationRuns = alpha.Count(r => r.Split == "Validation"),
            oosOpenings = oosAlpha,
            families = outcomes.Select(o => new
            {
                o.Family,
                o.Name,
                o.Priority,
                o.Hypothesis,
                o.Data,
                o.PassedScreen,
                o.Flipped,
                o.IsDecision,
                o.ChosenId,
                screens = o.Screens,
                bestInSample = o.InSample.OrderByDescending(r => r.Summary.SharpePerDay).Take(5).ToList(),
                bestGross = o.InSample.OrderByDescending(r => r.Summary.Gross).Take(3).ToList(),
                o.Validation,
                o.Neighbours,
                o.Stability,
                o.Bootstrap
            }).ToList(),
            costSensitivity = cost,
            gates
        };
        File.WriteAllText(Path.Combine(paths.Out, "alpha-discovery-results.json"), JsonSerializer.Serialize(results, Json));

        var md = new StringBuilder();
        md.AppendLine("# Alpha discovery: rejected configurations");
        md.AppendLine();
        md.AppendLine($"Generated {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC from `families/*.json`. All IS numbers are net of CONSERVATIVE costs and real funding on a $100k book.");
        md.AppendLine();
        foreach (var o in outcomes)
        {
            md.AppendLine($"## {o.Family}: {o.Name} ({o.Priority})");
            md.AppendLine();
            md.AppendLine($"- Decision: **{o.IsDecision}**");
            md.AppendLine($"- Screen passed: {(o.PassedScreen ? "yes" : "no")}; sign flipped by rule: {(o.Flipped ? "yes" : "no")}; IS grid points: {o.InSample.Count}");
            md.AppendLine($"- Days {o.FromDay}..{o.ToDay} of the panel; IS ends day {o.InSampleEndDay}, Validation ends day {o.ValidationEndDay}.");
            md.AppendLine();
            if (o.Screens.Count > 0)
            {
                md.AppendLine("| Screen row | horizon h | t | IC | spread bp | skip-1 t | events | excess bp |");
                md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
                foreach (var s in o.Screens)
                {
                    md.AppendLine(s.Ic is { } ic
                        ? $"| {s.Name} | {s.Horizon} | {s.T:0.00} | {ic.MeanIc:0.0000} | {ic.MeanSpreadBp:0.0} | {s.IcSkip1?.IcTStat:0.00} | | |"
                        : $"| {s.Name} | {s.Horizon} | {s.T:0.00} | | | | {s.Event!.Events} | {s.Event.MeanExcessBp:0.0} |");
                }

                md.AppendLine();
            }

            if (o.InSample.Count > 0)
            {
                md.AppendLine("| Best IS points (by net Sharpe) | trades | gross | fees | slippage | funding | net | PF | SR/yr | MaxDD % | beta BTC |");
                md.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
                foreach (var r in o.InSample.OrderByDescending(r => r.Summary.SharpePerDay).Take(5))
                {
                    var s = r.Summary;
                    md.AppendLine($"| `{r.Id}` | {r.TradesForGate} | {s.Gross:0} | {s.Fees:0} | {s.Slippage:0} | {s.Funding:0} | {s.Net:0} | {r.ProfitFactorForGate:0.00} | {s.SharpeAnnual:0.00} | {s.MaxDrawdownPercent:0.0} | {s.BetaBtc:0.00} |");
                }

                md.AppendLine();
            }
        }

        File.WriteAllText(Path.Combine(paths.Out, "rejected-summary.md"), md.ToString());

        var summary = new StringBuilder();
        summary.AppendLine("# Alpha discovery summary");
        summary.AppendLine();
        summary.AppendLine($"Verdict: **{verdict}**");
        summary.AppendLine();
        summary.AppendLine($"- Families run: {outcomes.Count}. Screen rows: {screenRows}. Registered IS trials (phase 2 + 2b): {isTrials}.");
        summary.AppendLine($"- Families passing the IS screen: {outcomes.Count(o => o.PassedScreen)}. Families with an IS net-positive plateau: {outcomes.Count(o => o.ChosenId is not null)}.");
        summary.AppendLine($"- Validation runs: {alpha.Count(r => r.Split == "Validation")}. Sealed OOS openings for alpha strategies: {oosAlpha}.");
        summary.AppendLine();
        summary.AppendLine("| Family | Decision | best IS net SR/yr (CONSERVATIVE) | best IS gross PnL |");
        summary.AppendLine("|---|---|---:|---:|");
        foreach (var o in outcomes)
        {
            var best = o.InSample.OrderByDescending(r => r.Summary.SharpePerDay).FirstOrDefault();
            var gross = o.InSample.OrderByDescending(r => r.Summary.Gross).FirstOrDefault();
            summary.AppendLine($"| {o.Family} {o.Name} | {o.IsDecision} | {(best is null ? "n/a" : best.Summary.SharpeAnnual.ToString("0.00"))} | {(gross is null ? "n/a" : gross.Summary.Gross.ToString("0"))} |");
        }

        File.WriteAllText(Path.Combine(paths.Out, "alpha-discovery-summary.md"), summary.ToString());
        Console.WriteLine($"Report artifacts written. Verdict: {verdict}");
        return 0;
    }

    private static int Order(string family) => "ABCDFGHEIJKXM".IndexOf(family, StringComparison.Ordinal) is var i and >= 0 ? i : 99;

    // ---------------------------------------------------------------- gates and verdicts

    internal sealed record GateRow(
        string Family,
        string Name,
        string ConfigId,
        int FamilyTrials,
        int AllTrials,
        double SharpePValue,
        double DsrFamily,
        double DsrAll,
        bool BhDiscovery,
        double ValidationPfConservative,
        double ValidationPfStress,
        double ValidationPfBase,
        int ValidationTrades,
        double Breadth,
        bool? Stable,
        double BootstrapLoss,
        double TopCoinShare,
        string Verdict,
        IReadOnlyList<string> Reasons,
        string OosStatus);

    private static int Gates(Paths paths)
    {
        var registry = new ExperimentRegistry(paths.Registry);
        var outcomes = Directory.GetFiles(paths.FamiliesDir, "*.json")
            .Select(f => JsonSerializer.Deserialize<FamilyOutcome>(File.ReadAllText(f), Json)!)
            .OrderBy(o => o.Family, StringComparer.Ordinal)
            .ToList();
        var isRecords = registry.Records.Where(r => r.Family.StartsWith("alpha:", StringComparison.Ordinal) && r.Split == "IS").ToList();
        var allTrials = isRecords.Select(ExperimentRegistry.ConfigurationKey).Distinct(StringComparer.Ordinal).Count();
        var allVariance = ResearchStatistics.Variance(isRecords.GroupBy(ExperimentRegistry.ConfigurationKey).Select(g => g.Last().SharpePerObservation).ToList());
        var validated = outcomes.Where(o => o.Validation.Count > 0).ToList();
        var pValues = validated.Select(o => ResearchStatistics.SharpePValue(o.Validation.Single(v => v.Summary.Cost == "CONSERVATIVE").Summary.Moments)).ToList();
        var bh = ResearchStatistics.BenjaminiHochberg(pValues, 0.10);
        var rows = new List<GateRow>();
        for (var i = 0; i < validated.Count; i++)
        {
            var o = validated[i];
            var cons = o.Validation.Single(v => v.Summary.Cost == "CONSERVATIVE");
            var stress = o.Validation.Single(v => v.Summary.Cost == "STRESS");
            var baseRun = o.Validation.Single(v => v.Summary.Cost == "BASE");
            var familyTrials = registry.TrialCount("alpha:" + o.Family, "IS");
            var familyVariance = registry.SharpeVariance("alpha:" + o.Family, "IS");
            var dsrFamily = ResearchStatistics.DeflatedSharpe(cons.Summary.Moments, familyTrials, familyVariance);
            var dsrAll = ResearchStatistics.DeflatedSharpe(cons.Summary.Moments, allTrials, allVariance);
            var verdict = ResearchVerdict.Assign(new VerdictInputs(
                cons.TradesForGate,
                Dec(cons.ProfitFactorForGate),
                Math.Min(dsrFamily, dsrAll),
                (decimal)cons.Summary.Breadth,
                o.Stability?.Stable,
                StressProfitFactor: Dec(stress.ProfitFactorForGate),
                SurvivorshipBiased: false));
            var status = verdict.Status;
            var reasons = verdict.Reasons.ToList();
            if (status is ResearchStatuses.Promising)
            {
                var extra = new List<string>();
                if (!bh[i]) extra.Add("not a BH-FDR discovery at q = 0.10");
                if (stress.ProfitFactorForGate < 1.0) extra.Add($"STRESS PF {stress.ProfitFactorForGate:0.00} < 1.00");
                if (o.Bootstrap is { } b && b.ProbabilityOfLoss >= 0.10) extra.Add($"bootstrap P(loss) {b.ProbabilityOfLoss:P0} ≥ 10%");
                if (!(cons.Summary.TopCoinShare <= 0.10)) extra.Add($"top coin {cons.Summary.TopCoin} is {cons.Summary.TopCoinShare:P0} of net PnL");
                if (extra.Count > 0)
                {
                    status = ResearchStatuses.Weak;
                    reasons.Add("Phase 2 extra gates: " + string.Join("; ", extra) + ".");
                }
            }

            rows.Add(new GateRow(o.Family, o.Name, cons.Id, familyTrials, allTrials, pValues[i], dsrFamily, dsrAll, bh[i],
                cons.ProfitFactorForGate, stress.ProfitFactorForGate, baseRun.ProfitFactorForGate, cons.TradesForGate, cons.Summary.Breadth,
                o.Stability?.Stable, o.Bootstrap?.ProbabilityOfLoss ?? double.NaN, cons.Summary.TopCoinShare, status, reasons,
                status == ResearchStatuses.Promising ? "ELIGIBLE (not opened by this stage)" : "SEALED (not eligible)"));
        }

        var report = new
        {
            generatedUtc = DateTimeOffset.UtcNow,
            phase2IsTrials = allTrials,
            phase2IsSharpeVariance = allVariance,
            validatedFamilies = validated.Count,
            oosAccessesForAlpha = new OosVault(paths.Vault).Accesses.Count(a => a.Strategy.StartsWith("alpha:", StringComparison.Ordinal)),
            rows,
            families = outcomes.Select(o => new { o.Family, o.Name, o.Priority, o.PassedScreen, o.Flipped, o.IsDecision, o.ChosenId, isTrials = o.InSample.Count }).ToList()
        };
        File.WriteAllText(Path.Combine(paths.Out, "gates.json"), JsonSerializer.Serialize(report, Json));
        foreach (var r in rows)
        {
            Console.WriteLine($"{r.Family} {r.ConfigId}: {r.Verdict} | PFc {r.ValidationPfConservative:0.00} PFs {r.ValidationPfStress:0.00} DSRf {r.DsrFamily:0.00} DSRall {r.DsrAll:0.00} BH {r.BhDiscovery} | {string.Join(" ", r.Reasons)}");
        }

        Console.WriteLine($"Phase 2 IS trials {allTrials}. PROMISING: {rows.Count(r => r.Verdict == ResearchStatuses.Promising)}.");
        return 0;
    }
}
