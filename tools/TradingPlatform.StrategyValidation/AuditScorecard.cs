using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Research.Framework;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;
using ResearchStatuses = TradingPlatform.Research.Framework.ResearchStatuses;
using SV = TradingPlatform.Backtesting.Validation.StrategyValidation;

namespace TradingPlatform.StrategyValidation;

/// <summary>
/// Audit scorecard: every operator template plus macd_trend, offline from the kline and funding caches.
/// IS 60% / Validation 20% / sealed OOS 20% per dataset. OOS is opened only for PROMISING rows.
/// </summary>
internal static class AuditScorecard
{
    private const decimal Capital = 10_000m;
    private const decimal TypicalNotional = 2_500m;
    private const string Baseline = "baseline";
    private static readonly string[] CachedTimeframes = ["5m", "15m", "30m", "1h", "4h", "1d"];
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    internal sealed record Plan(string Template, string Tf, string Side, bool BtcOnly, string? NotTestable)
    {
        public string Key => $"{Template}|{Tf}";
    }

    internal sealed record TradeRow
    {
        public required string Key { get; init; }
        public required string Symbol { get; init; }
        public required string Split { get; init; }
        public required string Profile { get; init; }
        public required string Variant { get; init; }
        public bool Short { get; init; }
        public DateTimeOffset Open { get; init; }
        public DateTimeOffset Close { get; init; }
        public decimal Net { get; init; }
        public decimal Gross { get; init; }
        public decimal Fees { get; init; }
        public decimal Funding { get; init; }
        public decimal Slip { get; init; }
        public decimal Notional { get; init; }
        public bool Stop { get; init; }
        public int HoldBars { get; init; }
        public double Mae { get; init; }
        public double Mfe { get; init; }
        public string Regime { get; init; } = "";
        public double AtrRank { get; init; }
        public double QuoteVol24h { get; init; }
        public double FundingAtEntry { get; init; }
    }

    internal sealed class Span
    {
        public DateTimeOffset From = DateTimeOffset.MaxValue;
        public DateTimeOffset To = DateTimeOffset.MinValue;
        public int Coins;
    }

    internal sealed record Agg(
        int Trades,
        decimal Net,
        decimal Pf,
        decimal WinRate,
        decimal Gross,
        decimal Fees,
        decimal Funding,
        decimal Slip,
        int CoinsTraded,
        decimal Breadth,
        double SharpeDaily,
        double? SharpeAnnual,
        decimal? Sortino,
        decimal MaxDdPct,
        decimal? Calmar,
        int Days,
        decimal ReturnPct,
        double Skew,
        double Kurtosis);

    private sealed class Dataset
    {
        public required string Symbol;
        public required string Tf;
        public required List<MarketCandle> Bars;
        public required CausalIndicatorCache Cache;
        public CausalIndicatorCache? Htf;
        public required List<ReplayFundingSettlement> Settlements;
        public StrategyFuturesSeries? Futures;
        public required long[] OpenTicks;
        public required double[] AtrPct;
        public required double[] QuotePrefix;
        public required LiquidityBucket Bucket;
        public required decimal Impact;
        public required SealedSplitRanges Split;
        public required string Hash;
        public int BarsPerDay;
    }

    private sealed class Context
    {
        public required string Root;
        public required string CacheDir;
        public required string FuturesRoot;
        public required ConcurrentBag<TradeRow> Rows;
        public required ConcurrentDictionary<string, Span> Spans;
        public required ConcurrentDictionary<string, string> Hashes;
        public required long[] BtcCloseTicks;
        public required string[] BtcRegime;
        public required Dictionary<string, decimal> DailyQuoteVolume;
    }

    public static async Task<int> RunAsync(string root, string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var sw = Stopwatch.StartNew();
        var cacheDir = Path.Combine(root, "artifacts", "strategy-validation-cache");
        var outDir = Path.Combine(root, "artifacts", "research", "audit");
        var registryDir = Path.Combine(root, "artifacts", "research", "registry");
        Directory.CreateDirectory(outDir);
        var parallel = Arg(args, "--parallel", Math.Max(1, Environment.ProcessorCount - 2));
        var maxCoins = Arg(args, "--max-coins", int.MaxValue);
        var onlyTemplates = ArgList(args, "--templates");
        var onlyTimeframes = ArgList(args, "--timeframes");
        var skipOos = args.Contains("--no-oos", StringComparer.OrdinalIgnoreCase);

        var plans = Plans(onlyTemplates, onlyTimeframes);
        Console.WriteLine($"Audit scorecard: {plans.Count} template×timeframe plans ({plans.Count(p => p.NotTestable is not null)} not testable offline).");

        var dailyQv = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var firstBar = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.GetFiles(cacheDir, "*_15m.json"))
        {
            var symbol = Path.GetFileName(file)[..^"_15m.json".Length];
            var bars = Load(file);
            if (bars.Count < 96 * 60)
            {
                continue;
            }

            var tail = bars.Skip(Math.Max(0, bars.Count - 96 * 30)).ToList();
            dailyQv[symbol] = tail.Sum(b => b.Volume * b.Close) / Math.Max(1m, tail.Count / 96m);
            firstBar[symbol] = bars[0].OpenTime;
        }

        var universe = dailyQv.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).Take(maxCoins).ToList();
        if (!universe.Contains("BTCUSDT"))
        {
            universe.Add("BTCUSDT");
        }

        var (btcTicks, btcRegime) = BtcRegime(LoadTf(cacheDir, "BTCUSDT", "1h"));
        var ctx = new Context
        {
            Root = root,
            CacheDir = cacheDir,
            FuturesRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts")),
            Rows = [],
            Spans = new ConcurrentDictionary<string, Span>(),
            Hashes = new ConcurrentDictionary<string, string>(),
            BtcCloseTicks = btcTicks,
            BtcRegime = btcRegime,
            DailyQuoteVolume = dailyQv
        };

        var testable = plans.Where(p => p.NotTestable is null).ToList();
        var datasets = testable.Select(p => p.Tf).Distinct()
            .SelectMany(tf => universe.Select(symbol => (Symbol: symbol, Tf: tf)))
            .Where(d => testable.Any(p => p.Tf == d.Tf && (!p.BtcOnly || d.Symbol == "BTCUSDT")))
            .ToList();
        Console.WriteLine($"Universe {universe.Count} coins; {datasets.Count} datasets; parallel {parallel}.");

        await ForEachDataset(ctx, datasets, parallel, (ds, _) =>
        {
            foreach (var plan in testable.Where(p => p.Tf == ds.Tf && (!p.BtcOnly || ds.Symbol == "BTCUSDT")))
            {
                RunPlan(ctx, ds, plan, Definition(plan), Baseline, includeIs: true);
                if (IsMeanReversion(plan.Template))
                {
                    var hold = MaxHold(plan.Template);
                    RunPlan(ctx, ds, plan, Definition(plan), "H3_BE1R", includeIs: true, mod: s => s with { BreakEvenAtR = 1m }, stress: true);
                    RunPlan(ctx, ds, plan, Definition(plan), "H3_TIME", includeIs: true, mod: s => s with { MaxHoldBars = hold > 0 ? Math.Max(2, hold / 2) : TimeStopBars(plan.Tf) }, stress: true);
                }
            }
        });
        Console.WriteLine($"Pass 1 done in {sw.Elapsed:hh\\:mm\\:ss}. Trades {ctx.Rows.Count:N0}.");

        var registry = new ExperimentRegistry(Path.Combine(registryDir, "experiments.jsonl"));
        var vault = new OosVault(Path.Combine(registryDir, "oos-access.jsonl"));
        var rows = ctx.Rows.ToList();
        var byKey = rows.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.ToList());

        Agg AggFor(string key, string split, string profile, string variant, IEnumerable<TradeRow>? source = null) =>
            Aggregate((source ?? (byKey.TryGetValue(key, out var list) ? list : []))
                .Where(r => r.Split == split && r.Profile == profile && r.Variant == variant).ToList(),
                SpanOf(ctx, key, split, variant));

        var scorecard = new List<Dictionary<string, object?>>();
        var baseRows = new List<(Plan Plan, Agg Val, Agg ValBase, Agg ValStress, Agg Is, SharpeMoments M)>();
        foreach (var plan in testable)
        {
            var val = AggFor(plan.Key, "VAL", "CONSERVATIVE", Baseline);
            var valBase = AggFor(plan.Key, "VAL", "BASE", Baseline);
            var valStress = AggFor(plan.Key, "VAL", "STRESS", Baseline);
            var isAgg = AggFor(plan.Key, "IS", "CONSERVATIVE", Baseline);
            var moments = Moments(byKey.GetValueOrDefault(plan.Key) ?? [], plan.Key, "VAL", "CONSERVATIVE", Baseline, ctx);
            baseRows.Add((plan, val, valBase, valStress, isAgg, moments));
            Register(registry, plan, Baseline, "IS", "CONSERVATIVE", isAgg, MomentsOf(isAgg), ctx);
            foreach (var (profile, agg) in new[] { ("BASE", valBase), ("CONSERVATIVE", val), ("STRESS", valStress) })
            {
                Register(registry, plan, Baseline, "VAL", profile, agg, profile == "CONSERVATIVE" ? moments : MomentsOf(agg), ctx);
            }
        }

        var hypotheses = Hypotheses(ctx, testable, byKey, registry);

        var pvalues = baseRows.Select(r => ResearchStatistics.SharpePValue(r.M)).ToList();
        var discoveries = ResearchStatistics.BenjaminiHochberg(pvalues, 0.10);
        var trials = registry.Records.Where(r => r.Split == "Validation" && r.CostProfile == "CONSERVATIVE")
            .Select(ExperimentRegistry.ConfigurationKey).Distinct().Count();
        var sharpeVar = ResearchStatistics.Variance(registry.Records
            .Where(r => r.Split == "Validation" && r.CostProfile == "CONSERVATIVE" && r.Observations > 1)
            .GroupBy(ExperimentRegistry.ConfigurationKey).Select(g => g.Last().SharpePerObservation).ToList());
        var bias = UniverseBiasFor(firstBar, universe);

        var candidates = new List<Plan>();
        var verdictInputs = new Dictionary<string, VerdictInputs>();
        var dsrGlobal = new Dictionary<string, double>();
        var familyTrials = new Dictionary<string, int>();
        for (var i = 0; i < baseRows.Count; i++)
        {
            var (plan, val, _, stress, _, m) = baseRows[i];
            var family = registry.Records
                .Where(r => r.Family == plan.Template && r.Split == "Validation" && r.CostProfile == "CONSERVATIVE")
                .GroupBy(ExperimentRegistry.ConfigurationKey)
                .Select(g => g.Last())
                .ToList();
            var familyVar = ResearchStatistics.Variance(family.Where(r => r.Observations > 1).Select(r => r.SharpePerObservation).ToList());
            var dsr = ResearchStatistics.DeflatedSharpe(m, Math.Max(1, family.Count), familyVar);
            dsrGlobal[plan.Key] = ResearchStatistics.DeflatedSharpe(m, Math.Max(1, trials), sharpeVar);
            familyTrials[plan.Key] = family.Count;
            var input = new VerdictInputs(val.Trades, val.Trades == 0 ? null : val.Pf, dsr, plan.BtcOnly ? 1m : val.Breadth, null, StressProfitFactor: stress.Pf);
            verdictInputs[plan.Key] = input;
            if (val.Trades >= ResearchVerdict.MinimumValidationTrades && val.Pf > ResearchVerdict.PromisingProfitFactor
                && dsr > ResearchVerdict.PromisingDeflatedSharpe && (plan.BtcOnly || val.Breadth > ResearchVerdict.PromisingBreadth))
            {
                candidates.Add(plan);
            }
        }

        Console.WriteLine($"Candidates for perturbation: {candidates.Count}. Trials N={trials}, Sharpe variance {sharpeVar:E3}.");
        var stability = await Perturb(ctx, candidates, universe, parallel, byKey);

        var promising = new List<Plan>();
        foreach (var plan in candidates)
        {
            var input = verdictInputs[plan.Key] with { Stable = stability.GetValueOrDefault(plan.Key)?.Stable };
            verdictInputs[plan.Key] = input;
            if (ResearchVerdict.Assign(input).Status == ResearchStatuses.Promising)
            {
                promising.Add(plan);
            }
        }

        var oos = new Dictionary<string, (Agg Cons, Agg Stress)>();
        if (!skipOos && promising.Count > 0)
        {
            Console.WriteLine($"Opening sealed OOS for {promising.Count} PROMISING rows.");
            var oosRows = new ConcurrentBag<TradeRow>();
            var oosCtx = new Context
            {
                Root = ctx.Root, CacheDir = ctx.CacheDir, FuturesRoot = ctx.FuturesRoot, Rows = oosRows, Spans = ctx.Spans,
                Hashes = ctx.Hashes, BtcCloseTicks = ctx.BtcCloseTicks, BtcRegime = ctx.BtcRegime, DailyQuoteVolume = dailyQv
            };
            var oosDatasets = promising.SelectMany(p => universe.Where(s => !p.BtcOnly || s == "BTCUSDT").Select(s => (Symbol: s, p.Tf))).Distinct().ToList();
            await ForEachDataset(oosCtx, oosDatasets, parallel, (ds, _) =>
            {
                foreach (var plan in promising.Where(p => p.Tf == ds.Tf && (!p.BtcOnly || ds.Symbol == "BTCUSDT")))
                {
                    try
                    {
                        var ticket = vault.Open(plan.Key, ds.Hash, ds.Bars.Count, "Audit 2026-10: PROMISING after Validation, perturbation frozen.");
                        RunRange(oosCtx, ds, plan, Definition(plan), "OOS", Baseline, ticket.From, ticket.To, CostProfileKind.Conservative, null);
                        RunRange(oosCtx, ds, plan, Definition(plan), "OOS", Baseline, ticket.From, ticket.To, CostProfileKind.Stress, null);
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine($"OOS refused for {plan.Key} {ds.Symbol}: {ex.Message}");
                    }
                }
            });
            foreach (var plan in promising)
            {
                var list = oosRows.Where(r => r.Key == plan.Key).ToList();
                var cons = AggFor(plan.Key, "OOS", "CONSERVATIVE", Baseline, list);
                var stress = AggFor(plan.Key, "OOS", "STRESS", Baseline, list);
                oos[plan.Key] = (cons, stress);
                verdictInputs[plan.Key] = verdictInputs[plan.Key] with { OosOpenedOnce = true, OosProfitFactorConservative = cons.Trades == 0 ? null : cons.Pf };
                Register(registry, plan, Baseline, "OOS", "CONSERVATIVE", cons, MomentsOf(cons), ctx);
            }
        }

        for (var i = 0; i < baseRows.Count; i++)
        {
            var (plan, val, valBase, valStress, isAgg, m) = baseRows[i];
            var input = verdictInputs[plan.Key] with { SurvivorshipBiased = bias.CurrentListingsOnly };
            var verdict = ResearchVerdict.Assign(input);
            var allRows = byKey.GetValueOrDefault(plan.Key) ?? [];
            scorecard.Add(new Dictionary<string, object?>
            {
                ["template"] = plan.Template,
                ["timeframe"] = plan.Tf,
                ["sides"] = plan.Side,
                ["btcOnly"] = plan.BtcOnly,
                ["status"] = verdict.Status,
                ["reasons"] = verdict.Reasons,
                ["valConservative"] = val,
                ["valBase"] = valBase,
                ["valStress"] = valStress,
                ["isConservative"] = isAgg,
                ["valLong"] = Aggregate(allRows.Where(r => r.Split == "VAL" && r.Profile == "CONSERVATIVE" && r.Variant == Baseline && !r.Short).ToList(), SpanOf(ctx, plan.Key, "VAL", Baseline)),
                ["valShort"] = Aggregate(allRows.Where(r => r.Split == "VAL" && r.Profile == "CONSERVATIVE" && r.Variant == Baseline && r.Short).ToList(), SpanOf(ctx, plan.Key, "VAL", Baseline)),
                ["sharpePValue"] = pvalues[i],
                ["bhDiscovery"] = discoveries[i],
                ["deflatedSharpe"] = input.DeflatedSharpeProbability,
                ["familyTrials"] = familyTrials.GetValueOrDefault(plan.Key),
                ["deflatedSharpeAllTrials"] = dsrGlobal.GetValueOrDefault(plan.Key),
                ["stability"] = stability.GetValueOrDefault(plan.Key),
                ["oosConservative"] = oos.TryGetValue(plan.Key, out var o) ? o.Cons : null,
                ["oosStress"] = oos.TryGetValue(plan.Key, out var o2) ? o2.Stress : null,
                ["coinsEvaluated"] = SpanOf(ctx, plan.Key, "VAL", Baseline).Coins
            });
        }

        foreach (var plan in plans.Where(p => p.NotTestable is not null))
        {
            scorecard.Add(new Dictionary<string, object?>
            {
                ["template"] = plan.Template,
                ["timeframe"] = plan.Tf,
                ["status"] = "NOT_TESTABLE",
                ["reasons"] = new[] { plan.NotTestable }
            });
        }

        var sideStudy = SideStudy(testable, byKey, ctx);
        var meta = new Dictionary<string, object?>
        {
            ["generatedAt"] = DateTimeOffset.UtcNow,
            ["runtime"] = sw.Elapsed.ToString(),
            ["coins"] = universe.Count,
            ["datasets"] = datasets.Count,
            ["trades"] = rows.Count,
            ["trialsValidationConservative"] = trials,
            ["sharpeVarianceAcrossTrials"] = sharpeVar,
            ["universeBias"] = bias,
            ["costProfiles"] = CostProfiles.All.Select(k => Enum.GetValues<LiquidityBucket>().Select(b => CostProfiles.For(k, b).Describe())).SelectMany(x => x).ToList(),
            ["fundingNote"] = "Real funding settlements from artifacts/data/futures-history-v1/funding for every profile.",
            ["riskBook"] = "LOW: $10,000 per coin book, 0.5% risk, 3x, book SL 2% / TP 4% unless the template supplies structural stops (same switches as BacktestService).",
            ["hypothesesApproximation"] = "H1/H2/H4/H5/H6 filter or reweight the single-book trade list. A blocked trade does not free the slot for a later signal. H3 is a true replay variant."
        };
        await File.WriteAllTextAsync(Path.Combine(outDir, "scorecard.json"), JsonSerializer.Serialize(new { meta, scorecard, sideStudy, hypotheses }, Indented));
        await File.WriteAllTextAsync(Path.Combine(outDir, "scorecard.md"), Render(meta, scorecard, sideStudy, hypotheses));
        Console.WriteLine($"Wrote {Path.Combine(outDir, "scorecard.md")} in {sw.Elapsed:hh\\:mm\\:ss}.");
        return 0;
    }

    // ---------- plans ----------

    private static List<Plan> Plans(IReadOnlyList<string> onlyTemplates, IReadOnlyList<string> onlyTimeframes)
    {
        var keys = StrategyTemplateKeys.OperatorCatalog.Append(StrategyTemplateKeys.MacdTrend).Distinct(StringComparer.OrdinalIgnoreCase);
        var plans = new List<Plan>();
        foreach (var key in keys)
        {
            if (onlyTemplates.Count > 0 && !onlyTemplates.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var side = StrategyTemplateKeys.DirectionsFor(key).Contains("SHORT") ? StrategySides.Both : StrategySides.Long;
            var btcOnly = key.StartsWith("btc_", StringComparison.OrdinalIgnoreCase);
            string? notTestable = StrategyMarketContext.NeedsOpenInterest(key)
                ? "Needs open-interest history. Binance serves ~30 days; the local cache covers 2026-08-21 to 2026-09-30 only."
                : null;
            foreach (var tf in StrategyTemplateKeys.TimeframesFor(key))
            {
                if (onlyTimeframes.Count > 0 && !onlyTimeframes.Contains(tf, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                plans.Add(new Plan(key, tf, side, btcOnly,
                    notTestable ?? (CachedTimeframes.Contains(tf) ? null : $"Timeframe {tf} is not in the kline cache.")));
            }
        }

        return plans;
    }

    private static StrategyDefinition Definition(Plan plan, StrategyTemplateParams? parameters = null)
    {
        var p = (parameters ?? StrategyTemplates.DefaultsFor(plan.Template, true)) with
        {
            TemplateKey = plan.Template,
            AllowedSide = plan.Side,
            Timeframe = plan.Tf
        };
        return new StrategyDefinitionValidator().Parse(StrategyTemplates.Build(plan.Template, 1, p));
    }

    private static int MaxHold(string key) =>
        StrategyTemplateKeys.IsFlatRange(key) ? FlatRangeStrategy.MaxHoldHours : RefactoredStrategyEvaluator.MaxHoldBars(key);

    private static int TimeStopBars(string tf) => tf switch
    {
        "5m" => 36,
        "15m" => 24,
        "30m" => 16,
        "1h" => 12,
        "4h" => 6,
        _ => 5
    };

    private static bool IsMeanReversion(string key) =>
        StrategyTemplateKeys.Family(key).Contains("MEAN REVERSION", StringComparison.OrdinalIgnoreCase)
        || StrategyTemplateKeys.Family(key).Contains("REVERSAL", StringComparison.OrdinalIgnoreCase)
        || key is StrategyTemplateKeys.ZigZagFade or StrategyTemplateKeys.MacContrarian710 or StrategyTemplateKeys.RsiPullback;

    private static ReplaySettings BaseSettings(string key, DateTimeOffset from, DateTimeOffset to) =>
        SV.LowIsolatedRisk(from, to, Capital) with
        {
            HonorSuggestedStops = StrategyTemplateKeys.IsFlatRange(key) || StrategyTemplateKeys.IsImported(key) || StrategyTemplateKeys.IsRefactored(key),
            MaxHoldBars = MaxHold(key),
            BookStopsOff = StrategyTemplateKeys.IsImported(key),
            PreserveNullTake = StrategyTemplateKeys.IsRefactored(key)
        };

    // ---------- datasets ----------

    private static async Task ForEachDataset(Context ctx, IReadOnlyList<(string Symbol, string Tf)> datasets, int parallel, Action<Dataset, int> body)
    {
        var done = 0;
        var sw = Stopwatch.StartNew();
        await Parallel.ForEachAsync(datasets, new ParallelOptions { MaxDegreeOfParallelism = parallel }, (d, _) =>
        {
            try
            {
                var ds = Prepare(ctx, d.Symbol, d.Tf);
                if (ds is not null)
                {
                    body(ds, 0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{d.Symbol} {d.Tf}: FAILED {ex.GetType().Name}: {ex.Message}");
            }

            var n = Interlocked.Increment(ref done);
            if (n % 25 == 0 || n == datasets.Count)
            {
                var eta = TimeSpan.FromTicks(sw.Elapsed.Ticks / n * (datasets.Count - n));
                Console.WriteLine($"  {n}/{datasets.Count} datasets, elapsed {sw.Elapsed:hh\\:mm\\:ss}, eta {eta:hh\\:mm\\:ss}");
            }

            return ValueTask.CompletedTask;
        });
    }

    private static Dataset? Prepare(Context ctx, string symbol, string tf)
    {
        var bars = KlineSeries.Normalize(LoadTf(ctx.CacheDir, symbol, tf), out var duplicates).ToList();
        var timeframe = ParseTf(tf);
        if (bars.Count < 400 || KlineSeries.Inspect(bars, timeframe, duplicates).MissingPercent > 2m)
        {
            return null;
        }

        var cache = new CausalIndicatorCache(bars);
        var hourly = tf == "1h" ? bars : null;
        CausalIndicatorCache? htf = null;
        if (hourly is not null)
        {
            htf = cache;
        }
        else
        {
            var hourBars = LoadTf(ctx.CacheDir, symbol, "1h");
            htf = hourBars.Count > 0 ? new CausalIndicatorCache(hourBars) : null;
        }

        var funding = FuturesHistoryCache.ReadFunding(ctx.FuturesRoot, symbol, bars[0].OpenTime, bars[^1].CloseTime);
        var settlements = funding.Select(f => new ReplayFundingSettlement(f.FundingTime, f.FundingRate)).OrderBy(f => f.FundingTime).ToList();
        var duration = timeframe.ToDuration();
        var barsPerDay = (int)Math.Max(1, TimeSpan.FromDays(1) / duration);
        var atr = new double[bars.Count];
        var tr = new double[bars.Count];
        var prefix = new double[bars.Count + 1];
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            var prevClose = i == 0 ? b.Open : bars[i - 1].Close;
            tr[i] = (double)Math.Max(b.High - b.Low, Math.Max(Math.Abs(b.High - prevClose), Math.Abs(b.Low - prevClose)));
            prefix[i + 1] = prefix[i] + (double)(b.Volume * b.Close);
        }

        double sum = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            sum += tr[i];
            if (i >= 14)
            {
                sum -= tr[i - 14];
            }

            atr[i] = bars[i].Close > 0m ? sum / Math.Min(14, i + 1) / (double)bars[i].Close : 0;
        }

        var recent = Math.Min(bars.Count, barsPerDay * 30);
        var barQv = bars.Skip(bars.Count - recent).Select(b => b.Volume * b.Close).OrderBy(v => v).ToList();
        var medianBarQv = barQv.Count == 0 ? 0m : barQv[barQv.Count / 2];
        var dailyQv = ctx.DailyQuoteVolume.GetValueOrDefault(symbol, medianBarQv * barsPerDay);
        var dailyCloses = bars.Skip(bars.Count - recent)
            .GroupBy(b => b.OpenTime.UtcDateTime.Date)
            .Select(g => (double)g.Last().Close)
            .ToList();
        var dailyReturns = dailyCloses.Zip(dailyCloses.Skip(1), (a, b) => a > 0 && b > 0 ? Math.Log(b / a) : 0d).ToList();
        var dailyVolPercent = dailyReturns.Count < 2
            ? 0m
            : (decimal)(Math.Sqrt(dailyReturns.Sum(r => Math.Pow(r - dailyReturns.Average(), 2)) / (dailyReturns.Count - 1)) * 100);
        var hash = DataFingerprint.Of([(symbol, tf, bars)]);
        ctx.Hashes[$"{symbol}|{tf}"] = hash;
        return new Dataset
        {
            Symbol = symbol,
            Tf = tf,
            Bars = bars,
            Cache = cache,
            Htf = htf,
            Settlements = settlements,
            OpenTicks = bars.Select(b => b.OpenTime.UtcTicks).ToArray(),
            AtrPct = atr,
            QuotePrefix = prefix,
            Bucket = CostProfiles.Bucket(dailyQv),
            Impact = CostProfiles.ImpactPercent(TypicalNotional, dailyQv, dailyVolPercent),
            Split = OosVault.Split(bars.Count),
            Hash = hash,
            BarsPerDay = barsPerDay
        };
    }

    private static void RunPlan(
        Context ctx,
        Dataset ds,
        Plan plan,
        StrategyDefinition definition,
        string variant,
        bool includeIs,
        Func<ReplaySettings, ReplaySettings>? mod = null,
        bool stress = true)
    {
        var warm = SV.WarmupBars(definition);
        var (isFrom, isTo) = ds.Split.InSample;
        var (valFrom, valTo) = ds.Split.Validation;
        if (includeIs && isTo - Math.Max(isFrom, warm) >= 80)
        {
            RunRange(ctx, ds, plan, definition, "IS", variant, Math.Max(isFrom, warm), isTo, CostProfileKind.Conservative, mod);
        }

        if (valTo - Math.Max(valFrom, warm) < 40)
        {
            return;
        }

        var from = Math.Max(valFrom, warm);
        if (variant == Baseline)
        {
            RunRange(ctx, ds, plan, definition, "VAL", variant, from, valTo, CostProfileKind.Base, mod);
        }

        RunRange(ctx, ds, plan, definition, "VAL", variant, from, valTo, CostProfileKind.Conservative, mod);
        if (stress)
        {
            RunRange(ctx, ds, plan, definition, "VAL", variant, from, valTo, CostProfileKind.Stress, mod);
        }
    }

    private static void RunRange(
        Context ctx,
        Dataset ds,
        Plan plan,
        StrategyDefinition definition,
        string split,
        string variant,
        int from,
        int to,
        CostProfileKind kind,
        Func<ReplaySettings, ReplaySettings>? mod)
    {
        var bars = ds.Bars;
        var profile = CostProfiles.For(kind, ds.Bucket, null, ds.Impact);
        var settings = CostProfiles.Apply(BaseSettings(plan.Template, bars[from].OpenTime, bars[to - 1].CloseTime), profile);
        settings = mod?.Invoke(settings) ?? settings;
        var futures = StrategyMarketContext.NeedsFunding(plan.Template)
            ? new StrategyFuturesSeries { FundingRate = AlignedMarketSeries.Align(ds.Settlements.Select(s => (s.FundingTime, s.FundingRate)).ToList(), bars) }
            : null;
        var result = new BacktestReplay(new StrategyEngine()).Run(
            definition,
            bars,
            settings,
            ds.Cache,
            from,
            to,
            StrategyMarketContext.NeedsHigherTimeframe(plan.Template) ? ds.Htf : null,
            futures,
            ds.Settlements.Count > 0 ? ds.Settlements : null);

        if (kind == CostProfileKind.Conservative)
        {
            var span = ctx.Spans.GetOrAdd($"{plan.Key}|{split}|{variant}", _ => new Span());
            lock (span)
            {
                if (bars[from].OpenTime < span.From)
                {
                    span.From = bars[from].OpenTime;
                }

                if (bars[to - 1].CloseTime > span.To)
                {
                    span.To = bars[to - 1].CloseTime;
                }

                span.Coins++;
            }
        }

        var label = kind.ToString().ToUpperInvariant();
        foreach (var t in result.Trades)
        {
            ctx.Rows.Add(Row(ctx, ds, plan.Key, split, label, variant, t));
        }
    }

    private static TradeRow Row(Context ctx, Dataset ds, string key, string split, string profile, string variant, ReplayTrade t)
    {
        var isShort = t.Side == "Short";
        var entryIdx = Array.BinarySearch(ds.OpenTicks, t.OpenedAt.UtcTicks);
        if (entryIdx < 0)
        {
            entryIdx = Math.Max(0, ~entryIdx - 1);
        }

        var exitIdx = Array.BinarySearch(ds.OpenTicks, t.ClosedAt.UtcTicks - 1);
        if (exitIdx < 0)
        {
            exitIdx = Math.Max(entryIdx, ~exitIdx - 1);
        }

        var lo = decimal.MaxValue;
        var hi = decimal.MinValue;
        for (var i = entryIdx; i <= exitIdx && i < ds.Bars.Count; i++)
        {
            lo = Math.Min(lo, ds.Bars[i].Low);
            hi = Math.Max(hi, ds.Bars[i].High);
        }

        var entry = t.EntryPrice <= 0m ? 1m : t.EntryPrice;
        double mae;
        double mfe;
        if (isShort)
        {
            mae = (double)((entry - hi) / entry);
            mfe = (double)((entry - lo) / entry);
        }
        else
        {
            mae = (double)((lo - entry) / entry);
            mfe = (double)((hi - entry) / entry);
        }

        var signalIdx = Math.Max(0, entryIdx - 1);
        var look = Math.Max(0, signalIdx - 500);
        var below = 0;
        for (var j = look; j < signalIdx; j++)
        {
            if (ds.AtrPct[j] < ds.AtrPct[signalIdx])
            {
                below++;
            }
        }

        var atrRank = signalIdx - look <= 0 ? 0.5 : below / (double)(signalIdx - look);
        var startQv = Math.Max(0, entryIdx - ds.BarsPerDay);
        var qv = ds.QuotePrefix[entryIdx] - ds.QuotePrefix[startQv];
        if (entryIdx - startQv > 0 && entryIdx - startQv < ds.BarsPerDay)
        {
            qv *= ds.BarsPerDay / (double)(entryIdx - startQv);
        }

        var fundingAtEntry = 0d;
        for (var k = ds.Settlements.Count - 1; k >= 0; k--)
        {
            if (ds.Settlements[k].FundingTime <= t.OpenedAt)
            {
                fundingAtEntry = (double)ds.Settlements[k].FundingRate;
                break;
            }
        }

        return new TradeRow
        {
            Key = key,
            Symbol = ds.Symbol,
            Split = split,
            Profile = profile,
            Variant = variant,
            Short = isShort,
            Open = t.OpenedAt,
            Close = t.ClosedAt,
            Net = t.PnL,
            Gross = t.GrossPnl,
            Fees = t.Fees,
            Funding = t.FundingPnl,
            Slip = t.SlippageCost,
            Notional = t.Quantity * t.EntryPrice,
            Stop = t.Reason == "Stop loss",
            HoldBars = Math.Max(1, exitIdx - entryIdx + 1),
            Mae = mae,
            Mfe = mfe,
            Regime = RegimeAt(ctx, t.OpenedAt),
            AtrRank = atrRank,
            QuoteVol24h = qv,
            FundingAtEntry = fundingAtEntry
        };
    }

    // ---------- BTC regime ----------

    private static (long[] Ticks, string[] Regime) BtcRegime(IReadOnlyList<MarketCandle> btc)
    {
        var ticks = new long[btc.Count];
        var regime = new string[btc.Count];
        var ema = new double[btc.Count];
        const double k = 2.0 / 201.0;
        for (var i = 0; i < btc.Count; i++)
        {
            var c = (double)btc[i].Close;
            ema[i] = i == 0 ? c : ema[i - 1] + k * (c - ema[i - 1]);
            ticks[i] = btc[i].CloseTime.UtcTicks;
            if (i < 224)
            {
                regime[i] = "unknown";
                continue;
            }

            var slopeUp = ema[i] > ema[i - 24];
            regime[i] = c > ema[i] && slopeUp ? "btc_up" : c < ema[i] && !slopeUp ? "btc_down" : "btc_chop";
        }

        return (ticks, regime);
    }

    private static string RegimeAt(Context ctx, DateTimeOffset at)
    {
        var idx = Array.BinarySearch(ctx.BtcCloseTicks, at.UtcTicks);
        if (idx < 0)
        {
            idx = ~idx - 1;
        }

        return idx < 0 ? "unknown" : ctx.BtcRegime[idx];
    }

    // ---------- aggregation ----------

    private static Span SpanOf(Context ctx, string key, string split, string variant) =>
        ctx.Spans.TryGetValue($"{key}|{split}|{variant}", out var s) ? s
        : ctx.Spans.TryGetValue($"{key}|{split}|{Baseline}", out var b) ? b
        : new Span();

    internal static Agg Aggregate(IReadOnlyList<TradeRow> rows, Span span)
    {
        var pos = rows.Where(r => r.Net > 0m).Sum(r => r.Net);
        var neg = -rows.Where(r => r.Net < 0m).Sum(r => r.Net);
        var net = pos - neg;
        var pf = neg == 0m ? (pos > 0m ? 99m : 0m) : Math.Round(pos / neg, 4);
        var perCoin = rows.GroupBy(r => r.Symbol).Select(g => (g.Count(), g.Sum(r => r.Net))).ToList();
        var coins = Math.Max(1, span.Coins);
        var capital = Capital * coins;
        var daily = Daily(rows, span, capital);
        var m = ResearchStatistics.Moments(daily.Returns);
        var ratios = DailyReturnMetrics.From(daily.Returns.Select(r => (decimal)r).ToList());
        var returnPct = capital == 0m ? 0m : net / capital * 100m;
        return new Agg(
            rows.Count,
            Math.Round(net, 2),
            pf,
            rows.Count == 0 ? 0m : Math.Round(rows.Count(r => r.Net > 0m) / (decimal)rows.Count, 4),
            Math.Round(rows.Sum(r => r.Gross), 2),
            Math.Round(rows.Sum(r => r.Fees), 2),
            Math.Round(rows.Sum(r => r.Funding), 2),
            Math.Round(rows.Sum(r => r.Slip), 2),
            perCoin.Count,
            Math.Round(Robustness.Breadth(perCoin), 4),
            m.Sharpe,
            ratios.Sharpe is { } s ? (double)s : null,
            ratios.Sortino,
            Math.Round(daily.MaxDdPct, 4),
            DailyReturnMetrics.Calmar(returnPct, daily.Returns.Count, daily.MaxDdPct),
            daily.Returns.Count,
            Math.Round(returnPct, 4),
            m.Skewness,
            m.Kurtosis);
    }

    private static (List<double> Returns, decimal MaxDdPct) Daily(IReadOnlyList<TradeRow> rows, Span span, decimal capital)
    {
        if (span.From == DateTimeOffset.MaxValue)
        {
            return ([], 0m);
        }

        var byDay = rows.GroupBy(r => DateOnly.FromDateTime(r.Close.UtcDateTime)).ToDictionary(g => g.Key, g => g.Sum(r => r.Net));
        var start = DateOnly.FromDateTime(span.From.UtcDateTime);
        var end = DateOnly.FromDateTime(span.To.UtcDateTime);
        var returns = new List<double>();
        var equity = capital;
        var peak = capital;
        var maxDd = 0m;
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var pnl = byDay.GetValueOrDefault(d);
            returns.Add(equity <= 0m ? 0 : (double)(pnl / equity));
            equity += pnl;
            peak = Math.Max(peak, equity);
            if (peak > 0m)
            {
                maxDd = Math.Max(maxDd, (peak - equity) / peak * 100m);
            }
        }

        return (returns, maxDd);
    }

    private static SharpeMoments Moments(IReadOnlyList<TradeRow> all, string key, string split, string profile, string variant, Context ctx)
    {
        var rows = all.Where(r => r.Split == split && r.Profile == profile && r.Variant == variant).ToList();
        var span = SpanOf(ctx, key, split, variant);
        return ResearchStatistics.Moments(Daily(rows, span, Capital * Math.Max(1, span.Coins)).Returns);
    }

    private static SharpeMoments MomentsOf(Agg a) => new(a.SharpeDaily, a.Days, a.Skew, a.Kurtosis);

    private static void Register(ExperimentRegistry registry, Plan plan, string variant, string split, string profile, Agg agg, SharpeMoments m, Context ctx)
    {
        var hash = string.Join(",", ctx.Hashes.Where(h => h.Key.EndsWith("|" + plan.Tf, StringComparison.Ordinal)).OrderBy(h => h.Key).Select(h => h.Value));
        var dataHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hash)), 0, 8).ToLowerInvariant();
        registry.Append(new ExperimentRecord(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            plan.Template,
            plan.Key,
            variant == Baseline ? "Frozen defaults" : variant,
            new Dictionary<string, string> { ["variant"] = variant, ["sides"] = plan.Side },
            dataHash,
            profile,
            split switch { "VAL" => "Validation", "IS" => "InSample", _ => "OOS" },
            m.Sharpe,
            m.Observations,
            agg.Trades == 0 ? null : agg.Pf,
            agg.Trades,
            agg.Net,
            ""));
    }

    // ---------- long vs short ----------

    private static List<Dictionary<string, object?>> SideStudy(IReadOnlyList<Plan> plans, Dictionary<string, List<TradeRow>> byKey, Context ctx)
    {
        var output = new List<Dictionary<string, object?>>();
        foreach (var plan in plans.Where(p => p.Side == StrategySides.Both))
        {
            var all = byKey.GetValueOrDefault(plan.Key) ?? [];
            var row = new Dictionary<string, object?> { ["template"] = plan.Template, ["timeframe"] = plan.Tf };
            var nets = new Dictionary<string, decimal>();
            foreach (var split in new[] { "IS", "VAL" })
            {
                foreach (var isShort in new[] { false, true })
                {
                    var rows = all.Where(r => r.Split == split && r.Profile == "CONSERVATIVE" && r.Variant == Baseline && r.Short == isShort).ToList();
                    var side = isShort ? "short" : "long";
                    var agg = Aggregate(rows, SpanOf(ctx, plan.Key, split, Baseline));
                    nets[$"{split}_{side}"] = agg.Net;
                    var downNet = rows.Where(r => r.Regime == "btc_down").Sum(r => r.Net);
                    nets[$"{split}_{side}_down"] = downNet;
                    row[$"{split.ToLowerInvariant()}_{side}"] = new
                    {
                        agg.Trades,
                        agg.Net,
                        agg.Pf,
                        agg.WinRate,
                        agg.Gross,
                        agg.Fees,
                        agg.Funding,
                        agg.Slip,
                        StopRate = rows.Count == 0 ? 0 : Math.Round(rows.Count(r => r.Stop) / (double)rows.Count, 4),
                        AvgHoldBars = rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.HoldBars), 1),
                        AvgMaePct = rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.Mae) * 100, 3),
                        AvgMfePct = rows.Count == 0 ? 0 : Math.Round(rows.Average(r => r.Mfe) * 100, 3),
                        NetBtcDown = Math.Round(downNet, 2),
                        NetByRegime = rows.GroupBy(r => r.Regime).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => new { Trades = g.Count(), Net = Math.Round(g.Sum(r => r.Net), 2) })
                    };
                }
            }

            row["decision"] = SideDecision(nets);
            output.Add(row);
        }

        return output;
    }

    private static string SideDecision(IReadOnlyDictionary<string, decimal> n)
    {
        bool Pos(string k) => n.GetValueOrDefault(k) > 0m;
        if (Pos("IS_long") && Pos("VAL_long") && Pos("IS_short") && Pos("VAL_short"))
        {
            return "SYMMETRIC: both sides net positive in IS and Validation.";
        }

        if (Pos("IS_long") && Pos("VAL_long") && !Pos("IS_short") && !Pos("VAL_short"))
        {
            return Pos("IS_short_down") && Pos("VAL_short_down")
                ? "STRICT_SHORT: longs positive; shorts only positive in BTC downtrends (IS and Validation)."
                : "LONG_ONLY: longs positive, shorts negative in IS and Validation.";
        }

        if (!Pos("IS_long") && !Pos("VAL_long") && Pos("IS_short") && Pos("VAL_short"))
        {
            return "SHORT_ONLY: shorts positive, longs negative in IS and Validation.";
        }

        if (!Pos("IS_long") && !Pos("VAL_long") && !Pos("IS_short") && !Pos("VAL_short"))
        {
            return "NEITHER: both sides lose after CONSERVATIVE costs.";
        }

        return "INCONSISTENT: side ranking flips between IS and Validation. No side change.";
    }

    // ---------- hypotheses ----------

    private static List<Dictionary<string, object?>> Hypotheses(Context ctx, IReadOnlyList<Plan> plans, Dictionary<string, List<TradeRow>> byKey, ExperimentRegistry registry)
    {
        var defs = new List<(string Id, string Variant, string Kind, Func<Plan, IReadOnlyList<TradeRow>, IReadOnlyList<TradeRow>>? Transform, string? ReplayVariant)>
        {
            ("H1", "alt longs blocked in BTC downtrend", "filter", (p, rows) => rows.Where(r => r.Short || r.Symbol == "BTCUSDT" || r.Regime != "btc_down").ToList(), null),
            ("H1", "alt longs blocked in BTC down, alt shorts blocked in BTC up", "filter", (p, rows) => rows.Where(r => r.Symbol == "BTCUSDT" || (r.Short ? r.Regime != "btc_up" : r.Regime != "btc_down")).ToList(), null),
            ("H2", "size × clamp(1.5 − ATR percentile, 0.5, 1.5)", "risk", (p, rows) => rows.Select(r => Scale(r, (decimal)Math.Clamp(1.5 - r.AtrRank, 0.5, 1.5))).ToList(), null),
            ("H3", "break-even after 1R", "filter", null, "H3_BE1R"),
            ("H3", "time stop (half the template hold, or a timeframe default)", "filter", null, "H3_TIME"),
            ("H4", "no shorts when funding ≤ −0.01%", "filter", (p, rows) => rows.Where(r => !r.Short || r.FundingAtEntry > -0.0001).ToList(), null),
            ("H4", "no shorts when funding ≤ −0.03%", "filter", (p, rows) => rows.Where(r => !r.Short || r.FundingAtEntry > -0.0003).ToList(), null),
            ("H4", "no shorts when funding ≤ −0.05%", "filter", (p, rows) => rows.Where(r => !r.Short || r.FundingAtEntry > -0.0005).ToList(), null),
            ("H5", "24h quote volume ≥ $10M", "filter", (p, rows) => rows.Where(r => r.QuoteVol24h >= 10e6).ToList(), null),
            ("H5", "24h quote volume ≥ $25M", "filter", (p, rows) => rows.Where(r => r.QuoteVol24h >= 25e6).ToList(), null),
            ("H5", "24h quote volume ≥ $50M", "filter", (p, rows) => rows.Where(r => r.QuoteVol24h >= 50e6).ToList(), null),
            ("H6", "max 3 same-side entries per bar, most liquid first", "risk", (p, rows) => rows
                .GroupBy(r => (r.Open, r.Short))
                .SelectMany(g => g.OrderByDescending(r => r.QuoteVol24h).Take(3))
                .ToList(), null)
        };

        var results = new List<Dictionary<string, object?>>();
        foreach (var plan in plans)
        {
            var all = byKey.GetValueOrDefault(plan.Key) ?? [];
            List<TradeRow> Pick(string split, string profile, string variant) =>
                all.Where(r => r.Split == split && r.Profile == profile && r.Variant == variant).ToList();
            var isBase = Pick("IS", "CONSERVATIVE", Baseline);
            var valBase = Pick("VAL", "CONSERVATIVE", Baseline);
            var stressBase = Pick("VAL", "STRESS", Baseline);
            if (valBase.Count < ResearchVerdict.MinimumValidationTrades)
            {
                continue;
            }

            var isSpan = SpanOf(ctx, plan.Key, "IS", Baseline);
            var valSpan = SpanOf(ctx, plan.Key, "VAL", Baseline);
            var aIs = Aggregate(isBase, isSpan);
            var aVal = Aggregate(valBase, valSpan);
            var aStress = Aggregate(stressBase, valSpan);
            foreach (var h in defs)
            {
                IReadOnlyList<TradeRow> vIs, vVal, vStress;
                if (h.ReplayVariant is { } rv)
                {
                    if (!IsMeanReversion(plan.Template))
                    {
                        continue;
                    }

                    vIs = Pick("IS", "CONSERVATIVE", rv);
                    vVal = Pick("VAL", "CONSERVATIVE", rv);
                    vStress = Pick("VAL", "STRESS", rv);
                }
                else
                {
                    if (h.Id == "H4" && plan.Side != StrategySides.Both || h.Id == "H1" && plan.BtcOnly)
                    {
                        continue;
                    }

                    vIs = h.Transform!(plan, isBase);
                    vVal = h.Transform!(plan, valBase);
                    vStress = h.Transform!(plan, stressBase);
                }

                var bIs = Aggregate(vIs, isSpan);
                var bVal = Aggregate(vVal, valSpan);
                var bStress = Aggregate(vStress, valSpan);
                var retained = valBase.Count == 0 ? 0 : vVal.Count / (double)valBase.Count;
                var (decision, why) = Decide(h.Kind, h.ReplayVariant is not null, aIs, bIs, aVal, bVal, aStress, bStress, retained);
                var variantKey = $"{h.Id}:{h.Variant}";
                Register(registry, plan, variantKey, "VAL", "CONSERVATIVE", bVal, MomentsOf(bVal), ctx);
                results.Add(new Dictionary<string, object?>
                {
                    ["hypothesis"] = h.Id,
                    ["variant"] = h.Variant,
                    ["template"] = plan.Template,
                    ["timeframe"] = plan.Tf,
                    ["retained"] = Math.Round(retained, 3),
                    ["isBase"] = Short(aIs),
                    ["isVariant"] = Short(bIs),
                    ["valBase"] = Short(aVal),
                    ["valVariant"] = Short(bVal),
                    ["stressBase"] = Short(aStress),
                    ["stressVariant"] = Short(bStress),
                    ["decision"] = decision,
                    ["why"] = why
                });
            }
        }

        return results;
    }

    private static object Short(Agg a) => new { a.Trades, a.Net, a.Pf, a.SharpeAnnual, a.MaxDdPct };

    private static TradeRow Scale(TradeRow r, decimal w) => r with
    {
        Net = r.Net * w,
        Gross = r.Gross * w,
        Fees = r.Fees * w,
        Funding = r.Funding * w,
        Slip = r.Slip * w,
        Notional = r.Notional * w
    };

    private static (string Decision, string Why) Decide(string kind, bool replay, Agg isB, Agg isV, Agg valB, Agg valV, Agg stB, Agg stV, double retained)
    {
        var fails = new List<string>();
        if (valV.Trades < ResearchVerdict.MinimumValidationTrades)
        {
            fails.Add($"only {valV.Trades} validation trades left");
        }

        if (kind == "filter")
        {
            if (!replay && retained < 0.5)
            {
                fails.Add($"keeps {retained:P0} of trades");
            }

            if (valV.Pf <= valB.Pf)
            {
                fails.Add($"Validation PF {valB.Pf:0.00}→{valV.Pf:0.00}");
            }

            if (valV.Net <= valB.Net)
            {
                fails.Add($"Validation net {valB.Net:0}→{valV.Net:0}");
            }

            if (isV.Pf < isB.Pf)
            {
                fails.Add($"IS PF {isB.Pf:0.00}→{isV.Pf:0.00}");
            }

            if (stV.Pf < stB.Pf)
            {
                fails.Add($"STRESS PF {stB.Pf:0.00}→{stV.Pf:0.00}");
            }
        }
        else
        {
            var sB = valB.SharpeAnnual ?? double.NegativeInfinity;
            var sV = valV.SharpeAnnual ?? double.NegativeInfinity;
            if (sV <= sB)
            {
                fails.Add($"Validation Sharpe {Fmt(valB.SharpeAnnual)}→{Fmt(valV.SharpeAnnual)}");
            }

            if (valV.MaxDdPct >= valB.MaxDdPct)
            {
                fails.Add($"Validation max DD {valB.MaxDdPct:0.00}%→{valV.MaxDdPct:0.00}%");
            }

            if ((isV.SharpeAnnual ?? double.NegativeInfinity) < (isB.SharpeAnnual ?? double.NegativeInfinity))
            {
                fails.Add($"IS Sharpe {Fmt(isB.SharpeAnnual)}→{Fmt(isV.SharpeAnnual)}");
            }
        }

        if (valV.Net <= 0m)
        {
            fails.Add("variant still loses on Validation");
        }

        return fails.Count == 0
            ? ("KEEP", "Improves Validation, holds on IS and STRESS.")
            : ("REJECT", string.Join("; ", fails));
    }

    // ---------- perturbation ----------

    private static async Task<Dictionary<string, StabilityScore>> Perturb(Context ctx, IReadOnlyList<Plan> candidates, IReadOnlyList<string> universe, int parallel, Dictionary<string, List<TradeRow>> byKey)
    {
        var output = new Dictionary<string, StabilityScore>();
        if (candidates.Count == 0)
        {
            return output;
        }

        var rows = new ConcurrentBag<TradeRow>();
        var pctx = new Context
        {
            Root = ctx.Root, CacheDir = ctx.CacheDir, FuturesRoot = ctx.FuturesRoot, Rows = rows, Spans = new ConcurrentDictionary<string, Span>(),
            Hashes = ctx.Hashes, BtcCloseTicks = ctx.BtcCloseTicks, BtcRegime = ctx.BtcRegime, DailyQuoteVolume = ctx.DailyQuoteVolume
        };
        var neighbours = candidates.ToDictionary(p => p.Key, p => Neighbours(p).ToList());
        var datasets = candidates.SelectMany(p => universe.Where(s => !p.BtcOnly || s == "BTCUSDT").Select(s => (Symbol: s, p.Tf))).Distinct().ToList();
        await ForEachDataset(pctx, datasets, parallel, (ds, _) =>
        {
            foreach (var plan in candidates.Where(p => p.Tf == ds.Tf && (!p.BtcOnly || ds.Symbol == "BTCUSDT")))
            {
                foreach (var (name, def) in neighbours[plan.Key])
                {
                    RunPlan(pctx, ds, plan, def, "P:" + name, includeIs: false, stress: false);
                }
            }
        });

        foreach (var plan in candidates)
        {
            var baseRows = (byKey.GetValueOrDefault(plan.Key) ?? []).Where(r => r.Split == "VAL" && r.Profile == "CONSERVATIVE" && r.Variant == Baseline).ToList();
            var basePf = Aggregate(baseRows, SpanOf(ctx, plan.Key, "VAL", Baseline)).Pf;
            var baseNet = baseRows.Sum(r => r.Net);
            var pfs = new List<decimal>();
            foreach (var (name, _) in neighbours[plan.Key])
            {
                var nRows = rows.Where(r => r.Key == plan.Key && r.Variant == "P:" + name && r.Profile == "CONSERVATIVE").ToList();
                if (nRows.Count == baseRows.Count && nRows.Sum(r => r.Net) == baseNet)
                {
                    continue;
                }

                pfs.Add(Aggregate(nRows, SpanOf(pctx, plan.Key, "VAL", "P:" + name)).Pf);
            }

            output[plan.Key] = Robustness.Stability(basePf, pfs);
        }

        return output;
    }

    private static IEnumerable<(string Name, StrategyDefinition Definition)> Neighbours(Plan plan)
    {
        var defaults = StrategyTemplates.DefaultsFor(plan.Template, true);
        foreach (var prop in typeof(StrategyTemplateParams).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var type = prop.PropertyType;
            if (type != typeof(int) && type != typeof(decimal) || !prop.CanWrite)
            {
                continue;
            }

            var value = Convert.ToDecimal(prop.GetValue(defaults), CultureInfo.InvariantCulture);
            if (value <= 0m)
            {
                continue;
            }

            foreach (var n in Robustness.Neighbours(value, type == typeof(int)))
            {
                var clone = defaults with { };
                prop.SetValue(clone, type == typeof(int) ? (int)n : n);
                StrategyDefinition? def = null;
                try
                {
                    def = Definition(plan, clone);
                }
                catch
                {
                }

                if (def is not null)
                {
                    yield return ($"{prop.Name}={n.ToString(CultureInfo.InvariantCulture)}", def);
                }
            }
        }
    }

    // ---------- universe ----------

    private static UniverseBias UniverseBiasFor(IReadOnlyDictionary<string, DateTimeOffset> firstBar, IReadOnlyList<string> universe)
    {
        // The first cached bar stands in for the listing date, but the cache itself starts at WindowStart: a coin
        // whose data begins within a day of that was already listed, not listed that day.
        var contracts = universe
            .Where(firstBar.ContainsKey)
            .Select(s => new ListedContract(
                s,
                firstBar[s] <= WindowStart.AddDays(1) ? WindowStart.AddDays(-PointInTimeUniverse.DefaultMinimumListingDays) : firstBar[s],
                null,
                "TRADING"))
            .ToList();
        return PointInTimeUniverse.Assess(contracts, WindowStart.AddDays(PointInTimeUniverse.DefaultMinimumListingDays), DateTimeOffset.UtcNow);
    }

    // ---------- report ----------

    private static string Render(Dictionary<string, object?> meta, List<Dictionary<string, object?>> scorecard, List<Dictionary<string, object?>> sides, List<Dictionary<string, object?>> hypotheses)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Audit strategy scorecard");
        sb.AppendLine();
        foreach (var (k, v) in meta)
        {
            sb.AppendLine($"- **{k}**: {(v is string s ? s : JsonSerializer.Serialize(v))}");
        }

        sb.AppendLine();
        sb.AppendLine("## Scorecard (Validation 20%, CONSERVATIVE unless noted)");
        sb.AppendLine();
        sb.AppendLine("| Template | TF | Status | Val trades | PF base | PF cons | PF stress | Net cons | Sharpe (ann.) | Sortino | Calmar | Max DD % | Breadth | DSR (family) | DSR (all trials) | BH | IS PF | OOS PF | Reasons |");
        sb.AppendLine("|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|---|");
        foreach (var row in scorecard.OrderBy(r => StatusRank(r["status"] as string)).ThenBy(r => r["template"]))
        {
            if (row["status"] as string == "NOT_TESTABLE")
            {
                sb.AppendLine($"| {row["template"]} | {row["timeframe"]} | NOT_TESTABLE | | | | | | | | | | | | | | | | {string.Join(" ", (IEnumerable<string?>)row["reasons"]!)} |");
                continue;
            }

            var val = (Agg)row["valConservative"]!;
            var vb = (Agg)row["valBase"]!;
            var vs = (Agg)row["valStress"]!;
            var ins = (Agg)row["isConservative"]!;
            var oos = row["oosConservative"] as Agg;
            sb.AppendLine($"| {row["template"]} | {row["timeframe"]} | {row["status"]} | {val.Trades} | {vb.Pf:0.00} | {val.Pf:0.00} | {vs.Pf:0.00} | {val.Net:0} | {Fmt(val.SharpeAnnual)} | {Fmt(val.Sortino)} | {Fmt(val.Calmar)} | {val.MaxDdPct:0.00} | {val.Breadth:P0} | {Fmt(row["deflatedSharpe"] as double?)} ({row["familyTrials"]}) | {Fmt(row["deflatedSharpeAllTrials"] as double?)} | {((bool)row["bhDiscovery"]! ? "yes" : "no")} | {ins.Pf:0.00} | {(oos is null ? "sealed" : oos.Pf.ToString("0.00", CultureInfo.InvariantCulture))} | {string.Join(" ", (IReadOnlyList<string>)row["reasons"]!)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Long vs short (CONSERVATIVE, net USDT; L = long, S = short)");
        sb.AppendLine();
        sb.AppendLine("| Template | TF | IS L net | IS S net | Val L net / PF | Val S net / PF | Val S fees | Val S funding | Val S stop % | Val S net in BTC down | Decision |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|");
        foreach (var row in sides)
        {
            dynamic isL = row["is_long"]!;
            dynamic isS = row["is_short"]!;
            dynamic vL = row["val_long"]!;
            dynamic vS = row["val_short"]!;
            sb.AppendLine($"| {row["template"]} | {row["timeframe"]} | {isL.Net:0} | {isS.Net:0} | {vL.Net:0} / {vL.Pf:0.00} | {vS.Net:0} / {vS.Pf:0.00} | {vS.Fees:0} | {vS.Funding:0} | {vS.StopRate:P0} | {vS.NetBtcDown:0} | {row["decision"]} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Hypotheses H1–H6 (keep/reject log)");
        sb.AppendLine();
        var summary = hypotheses.GroupBy(h => $"{h["hypothesis"]}: {h["variant"]}")
            .Select(g => $"| {g.Key} | {g.Count()} | {g.Count(x => (string)x["decision"]! == "KEEP")} |");
        sb.AppendLine("| Hypothesis | Tested rows | KEEP |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var line in summary)
        {
            sb.AppendLine(line);
        }

        sb.AppendLine();
        sb.AppendLine("| H | Variant | Template | TF | Retained | Val PF base→var | Val net base→var | Decision | Why |");
        sb.AppendLine("|---|---|---|---|---:|---|---|---|---|");
        foreach (var h in hypotheses)
        {
            dynamic vb = h["valBase"]!;
            dynamic vv = h["valVariant"]!;
            sb.AppendLine($"| {h["hypothesis"]} | {h["variant"]} | {h["template"]} | {h["timeframe"]} | {h["retained"]:P0} | {vb.Pf:0.00}→{vv.Pf:0.00} | {vb.Net:0}→{vv.Net:0} | {h["decision"]} | {h["why"]} |");
        }

        return sb.ToString();
    }

    private static int StatusRank(string? status) => status switch
    {
        ResearchStatuses.LiveCandidate => 0,
        ResearchStatuses.PaperCandidate => 1,
        ResearchStatuses.Promising => 2,
        ResearchStatuses.Weak => 3,
        ResearchStatuses.Research => 4,
        ResearchStatuses.Rejected => 5,
        _ => 6
    };

    private static string Fmt(double? v) => v is { } x && !double.IsInfinity(x) ? x.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";

    private static string Fmt(decimal? v) => v is { } x ? x.ToString("0.00", CultureInfo.InvariantCulture) : "n/a";

    // ---------- io ----------

    /// <summary>Common window start: the 15m cache begins here, so every timeframe is clipped to it.</summary>
    private static readonly DateTimeOffset WindowStart = new(2024, 9, 18, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The 1h/4h cache files hold only recent weeks; rebuild them from complete 15m groups.</summary>
    private static List<MarketCandle> LoadTf(string cacheDir, string symbol, string tf)
    {
        if (tf is "1h" or "4h")
        {
            var minutes = tf == "1h" ? 60 : 240;
            var per = minutes / 15;
            var source = KlineSeries.Normalize(Load(Path.Combine(cacheDir, $"{symbol}_15m.json")), out _);
            return source
                .Where(b => b.OpenTime >= WindowStart)
                .GroupBy(b => b.OpenTime.ToUnixTimeSeconds() / (minutes * 60))
                .Where(g => g.Count() == per)
                .Select(g =>
                {
                    var list = g.OrderBy(b => b.OpenTime).ToList();
                    return new MarketCandle
                    {
                        OpenTime = list[0].OpenTime,
                        CloseTime = list[^1].CloseTime,
                        Open = list[0].Open,
                        High = list.Max(b => b.High),
                        Low = list.Min(b => b.Low),
                        Close = list[^1].Close,
                        Volume = list.Sum(b => b.Volume),
                        IsClosed = true,
                        ExchangeTimestamp = list[^1].CloseTime
                    };
                })
                .OrderBy(b => b.OpenTime)
                .ToList();
        }

        return Load(Path.Combine(cacheDir, $"{symbol}_{tf}.json")).Where(b => b.OpenTime >= WindowStart).ToList();
    }

    private static List<MarketCandle> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        var bars = JsonSerializer.Deserialize<List<KlineDiskCache.CachedBar>>(stream) ?? [];
        return bars.Select(c => new MarketCandle
        {
            OpenTime = c.OpenTime,
            CloseTime = c.CloseTime,
            Open = c.Open,
            High = c.High,
            Low = c.Low,
            Close = c.Close,
            Volume = c.Volume,
            IsClosed = true,
            ExchangeTimestamp = c.CloseTime
        }).ToList();
    }

    private static Timeframe ParseTf(string tf) => tf switch
    {
        "1m" => Timeframe.OneMinute,
        "3m" => Timeframe.ThreeMinutes,
        "5m" => Timeframe.FiveMinutes,
        "15m" => Timeframe.FifteenMinutes,
        "30m" => Timeframe.ThirtyMinutes,
        "1h" => Timeframe.OneHour,
        "4h" => Timeframe.FourHours,
        "1d" => Timeframe.OneDay,
        _ => throw new ArgumentOutOfRangeException(nameof(tf), tf, null)
    };

    private static int Arg(string[] args, string key, int fallback)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) && v > 0 ? v : fallback;
    }

    private static IReadOnlyList<string> ArgList(string[] args, string key)
    {
        var i = Array.FindIndex(args, a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length
            ? args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }
}
