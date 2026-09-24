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

internal static class EdgeDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly Arm[] Arms =
    [
        new("ema_rsi_trend", "FROZEN_TREND", "FROZEN", "15m", ArmKind.Frozen),
        new("macd_trend", "FROZEN_TREND", "FROZEN", "15m", ArmKind.Frozen),
        new("donchian_breakout", "FROZEN_TREND", "FROZEN", "15m", ArmKind.Frozen),
        new("rsi_pullback", "FROZEN_MEAN", "FROZEN", "15m", ArmKind.Frozen),
        new("bollinger_reversion", "FROZEN_MEAN", "FROZEN", "15m", ArmKind.Frozen),
        new("FF-SWEEP-HOURLY", "PA_SWEEP", "FINAL_FIVE", "15m", ArmKind.FinalFive),
        new("FF-FAILED-DOWN", "PA_FAILED", "FINAL_FIVE", "15m", ArmKind.FinalFive),
        new("FF-EXPANSION-BOS", "PA_CONTINUATION", "FINAL_FIVE", "15m", ArmKind.FinalFive),
        new("FF-RANGE-RELEASE", "PA_CONTINUATION", "FINAL_FIVE", "1h", ArmKind.FinalFive),
        new("FF-RSI-QUIET", "PA_STRUCTURE", "FINAL_FIVE", "1h", ArmKind.FinalFive),
        new("CPA-SWEEP", "PA_SWEEP", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-PULLBACK", "PA_STRUCTURE", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-WM", "PA_STRUCTURE", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-COMPRESSION", "PA_CONTINUATION", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-MTF", "PA_STRUCTURE", "PHASE8", "5m", ArmKind.Contextual),
        new("CPA-FAILED_BREAKOUT", "PA_FAILED", "PHASE8", "5m", ArmKind.Contextual)
    ];

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        var dir = Path.Combine(root, "artifacts", "strategy-research", "edge-discovery");
        Directory.CreateDirectory(dir);
        var definitionHash = WriteManifest(dir);
        var observations = new List<EdgeObservation>();
        var btcHourly = await LoadOne(cacheDir, root, "BTCUSDT", "1h");
        var btcCache = new CausalIndicatorCache(btcHourly);
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            Console.WriteLine($"Edge discovery loading {symbol}.");
            var series = await LoadSymbol(cacheDir, root, symbol);
            var caches = series.ToDictionary(pair => pair.Key.Timeframe, pair => new CausalIndicatorCache(pair.Value), StringComparer.OrdinalIgnoreCase);
            var hourly = caches["1h"];
            var contextual = ContextualPriceActionSignals.BuildAll(caches);
            foreach (var arm in Arms)
            {
                var candles = series[(symbol, arm.Timeframe)];
                var signals = SignalsFor(arm, candles, caches, contextual);
                var trades = Replay(arm, symbol, candles, signals);
                var openIndex = new Dictionary<DateTimeOffset, int>();
                for (var i = 0; i < candles.Count; i++)
                {
                    openIndex[candles[i].OpenTime] = i;
                }

                var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
                foreach (var trade in trades)
                {
                    if (!openIndex.TryGetValue(trade.OpenedAt, out var fill) || fill <= 0)
                    {
                        continue;
                    }

                    var signal = fill - 1;
                    var phase = signal < insEnd ? "IS" : signal < valEnd ? "VALIDATION" : "OOS";
                    var block = Math.Clamp((signal * 4 / candles.Count) + 1, 1, 4);
                    observations.Add(EdgeFeatureBuilder.Build(
                        arm.Group,
                        arm.Family,
                        arm.CandidateId(symbol),
                        symbol,
                        arm.Timeframe,
                        trade.Side,
                        phase,
                        block,
                        trade.ClosedAt,
                        trade.PnL,
                        trade.Fees,
                        trade.SlippageCost,
                        caches[arm.Timeframe],
                        hourly,
                        btcCache,
                        signal));
                }

                Console.WriteLine($"{symbol} {arm.Id} trades={trades.Count}.");
            }
        }

        var phase4 = ReadPhase4(Path.Combine(root, "artifacts", "strategy-research", "futures-alpha-phase4", "trades.json"));
        var specs = ConditionSpecs();
        var judged = specs.Select(spec => Judge(spec, observations)).ToList();
        WriteJson(Path.Combine(dir, "condition-results.json"), judged);
        WriteJson(Path.Combine(dir, "phase4-baseline.json"), phase4);
        var answer = Answer(judged);
        WriteJson(Path.Combine(dir, "edge-summary.json"), new
        {
            DefinitionSha256 = definitionHash,
            Trades = observations.Count,
            Ready = observations.Count(x => x.Ready),
            ConditionsExamined = specs.Count,
            Answer = answer,
            LiveOff = true,
            PaperOff = true,
            ValidatedForPaper = false
        });
        WriteReport(root, definitionHash, observations, judged, phase4, answer);
        Console.WriteLine("EDGE DISCOVERY COMPLETE");
        Console.WriteLine(answer);
        return 0;
    }

    private static string WriteManifest(string dir)
    {
        var body = JsonSerializer.Serialize(new
        {
            EdgeFeatureCatalog.Version,
            EdgeFeatureCatalog.InterestingRule,
            EdgeFeatureCatalog.VolLowMax,
            EdgeFeatureCatalog.VolHighMin,
            EdgeFeatureCatalog.AdxRangeMax,
            EdgeFeatureCatalog.AdxTrendMin,
            EdgeFeatureCatalog.ContractionAtrRatio,
            EdgeFeatureCatalog.CompressionBars,
            EdgeFeatureCatalog.ReturnBars,
            EdgeFeatureCatalog.AltReturnGap,
            Arms = Arms.Select(x => new { x.Id, x.Group, x.ResearchFamily, x.Timeframe }).ToArray(),
            Symbols = FinalFiveCatalog.Symbols,
            UniverseNote = "Reconstruction is BTCUSDT, ETHUSDT, BNBUSDT only. BNB was already fixed by the volume rank. The larger universe is not rerun.",
            NearMiss = "Near-miss cards are the Phase 8 contextual variants. They are not replayed again, so they are not a second independent sample.",
            AtrPercentileScale = "Fraction in [0,1]. The Final Five quiet-RSI gate compared this fraction to 40, so that ATR gate did not bind. This phase does not change that strategy."
        }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, "hypothesis-manifest.json"), JsonSerializer.Serialize(new { Sha256 = hash, Body = JsonDocument.Parse(body).RootElement }, JsonOptions));
        return hash;
    }

    private static IReadOnlyList<SignalType> SignalsFor(
        Arm arm,
        IReadOnlyList<MarketCandle> candles,
        Dictionary<string, CausalIndicatorCache> caches,
        IReadOnlyList<ContextualSignalRow> contextual)
    {
        if (arm.Kind == ArmKind.Frozen)
        {
            return [];
        }

        if (arm.Kind == ArmKind.FinalFive)
        {
            return FinalFiveSignals.Build(arm.Id + "|" + arm.Timeframe, caches[arm.Timeframe], caches["1h"]);
        }

        var id = arm.Id switch
        {
            "CPA-SWEEP" => "CPA-SWEEP|BASELINE|5m",
            "CPA-PULLBACK" => "CPA-PULLBACK|BASELINE|5m",
            "CPA-WM" => "CPA-WM|BASELINE|5m",
            "CPA-COMPRESSION" => "CPA-COMPRESSION|CONTINUATION|5m",
            "CPA-MTF" => "CPA-MTF|BASELINE|5m",
            "CPA-FAILED_BREAKOUT" => "CPA-FAILED_BREAKOUT|BASELINE|5m",
            _ => arm.Id
        };
        return contextual.First(x => x.CandidateId == id).Signals;
    }

    private static List<ReplayTrade> Replay(Arm arm, string symbol, IReadOnlyList<MarketCandle> candles, IReadOnlyList<SignalType> signals)
    {
        if (candles.Count < 200)
        {
            return [];
        }

        var created = DateTimeOffset.Parse("2026-09-23T00:00:00Z", CultureInfo.InvariantCulture);
        var template = arm.Kind == ArmKind.Frozen ? arm.Id : StrategyTemplateKeys.EmaRsiTrend;
        var candidate = new ResearchCandidate(
            arm.CandidateId(symbol),
            template,
            1,
            "Unmodified existing rule. Edge discovery does not change it.",
            ResearchKinds.ParentFilter,
            template,
            "",
            [],
            "existing",
            "Model B",
            new ResearchFilters(),
            new ResearchNativeParams(),
            [arm.Timeframe],
            ["LONG", "SHORT"],
            [],
            created,
            "edge discovery",
            ResearchStatuses.Researching);
        var definition = ResearchRunner.DefinitionFor(candidate, arm.Timeframe);
        IStrategyEngine engine = arm.Kind == ArmKind.Frozen
            ? new ResearchStrategyEngine(candidate)
            : new PrecomputedResearchSignalEngine(signals);
        var settings = StrategyValidation.LowIsolatedRisk(candles[0].OpenTime, candles[^1].CloseTime);
        var cache = new CausalIndicatorCache(candles);
        var warmup = arm.Kind == ArmKind.Frozen ? StrategyValidation.WarmupBars(definition) : 0;
        return new BacktestReplay(engine).Run(definition, candles, settings, cache, warmup, candles.Count).Trades.ToList();
    }

    private static List<ConditionSpec> ConditionSpecs()
    {
        ConditionSpec Flag(string id, string definition, Func<EdgeObservation, bool?> flag) => new(id, definition, flag);
        var specs = new List<ConditionSpec>
        {
            Flag("VOL_LOW", "ATR(14) percentile over 50 bars is below 0.33 on the signal bar.", x => x.VolLow),
            Flag("VOL_NORMAL", "ATR percentile is from 0.33 up to but not including 0.67.", x => x.VolNormal),
            Flag("VOL_HIGH", "ATR percentile is at least 0.67.", x => x.VolHigh),
            Flag("ADX_RANGE", "ADX(14) is below 20.", x => x.AdxRange),
            Flag("ADX_TREND", "ADX(14) is at least 25.", x => x.AdxTrend),
            Flag("BAR_EXPANSION", "Signal-bar range is at least ATR(14).", x => x.Expansion),
            Flag("BAR_CONTRACTION", "Signal-bar range is below half of ATR(14).", x => x.Contraction),
            Flag("COMPRESSION_RELEASE", "Eight prior bars each have range below their own ATR, and the signal bar range is at least ATR.", x => x.CompressionRelease),
            Flag("HTF_ALIGNED", "Last closed 1h structure bias matches the trade side. Flat bias is excluded.", x => x.HtfAligned),
            Flag("HTF_OPPOSED", "Last closed 1h structure bias opposes the trade side.", x => x.HtfOpposed),
            Flag("BTC_TREND_ALIGNED", "BTC 1h ADX is at least 25 and the BTC 20-bar return matches the trade side.", x => x.BtcAligned),
            Flag("BTC_TREND_OPPOSED", "BTC 1h is trending and the BTC 20-bar return opposes the trade side.", x => x.BtcOpposed),
            Flag("BTC_VOL_HIGH", "BTC 1h ATR percentile is at least 0.67.", x => x.BtcVolHigh),
            Flag("BTC_VOL_LOW", "BTC 1h ATR percentile is below 0.33.", x => x.BtcVolLow),
            Flag("LONG", "Trade side is long. The out-set is short.", x => x.Side == "LONG" ? true : false),
            Flag("SHORT", "Trade side is short. The out-set is long.", x => x.Side == "SHORT" ? true : x.Side == "LONG" ? false : null),
            Flag("ALT_STRONGER", "Non-BTC 20-bar return exceeds the BTC return over the same clock window by 1 percentage point.", x => x.AltStronger),
            Flag("ALT_WEAKER", "Non-BTC 20-bar return trails the BTC return over the same clock window by 1 percentage point.", x => x.AltWeaker),
            Flag("SESSION_ASIA", "Signal close is 00:00–06:59 UTC.", x => x.Asia),
            Flag("SESSION_EUROPE", "Signal close is 07:00–14:59 UTC.", x => x.Europe),
            Flag("SESSION_US", "Signal close is 15:00–23:59 UTC.", x => x.Us)
        };
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var captured = day;
            specs.Add(Flag("DOW_" + day.ToString().ToUpperInvariant(), "Signal close falls on " + day + " UTC.", x => x.Dow is null ? null : x.Dow == captured));
        }

        return specs;
    }

    private static ConditionJudgement Judge(ConditionSpec spec, IReadOnlyList<EdgeObservation> rows)
    {
        var applicable = rows.Where(x => spec.In(x) is not null).ToList();
        var inside = applicable.Where(x => spec.In(x) == true).ToList();
        var outside = applicable.Where(x => spec.In(x) == false).ToList();
        var inn = Totals(inside);
        var outt = Totals(outside);
        var groups = inside.GroupBy(x => x.Group).Select(g => (g.Key, Totals(g.ToList()))).Where(x => x.Item2.Trades >= EdgeFeatureCatalog.MinimumGroupTrades && x.Item2.Net > 0m).Select(x => x.Key).Distinct().ToArray();
        var symbols = inside.GroupBy(x => x.Symbol).Select(g => (g.Key, Totals(g.ToList()))).Where(x => x.Item2.Trades >= EdgeFeatureCatalog.MinimumSymbolTrades && x.Item2.Net > 0m).Select(x => x.Key).ToArray();
        var blocks = Enumerable.Range(1, 4).Count(block =>
        {
            var cell = Totals(inside.Where(x => x.Block == block).ToList());
            return cell.Trades >= EdgeFeatureCatalog.MinimumBlockTrades && cell.Net > 0m;
        });
        var isTotals = Totals(inside.Where(x => x.Phase == "IS").ToList());
        var valTotals = Totals(inside.Where(x => x.Phase == "VALIDATION").ToList());
        var oosTotals = Totals(inside.Where(x => x.Phase == "OOS").ToList());
        var separates = inn.Trades >= 50 && inn.ProfitFactor > 1m && inn.Net > 0m && outt.Trades >= 50 && outt.ProfitFactor < 1m;
        var interesting = inn.Trades >= EdgeFeatureCatalog.MinimumInterestingTrades
            && inn.ProfitFactor > 1m
            && inn.Net > 0m
            && outt.Trades >= EdgeFeatureCatalog.MinimumInterestingTrades
            && outt.ProfitFactor < 1m
            && groups.Length >= 2
            && symbols.Length >= 2
            && blocks >= 3
            && isTotals.Trades >= EdgeFeatureCatalog.MinimumIsTrades
            && isTotals.Net > 0m
            && valTotals.Trades >= EdgeFeatureCatalog.MinimumValidationTrades
            && valTotals.Net > 0m;
        var partial = !interesting && separates && symbols.Length >= 2 && blocks >= 2;
        var label = interesting ? "EXPLORATORY" : partial ? "INSUFFICIENT_EVIDENCE" : "NOISE";
        return new ConditionJudgement(spec.Id, spec.Definition, label, inn, outt, isTotals, valTotals, oosTotals, groups, symbols, blocks, Stress(inside));
    }

    private static string Answer(IReadOnlyList<ConditionJudgement> judged)
    {
        if (judged.Any(x => x.Label == "EXPLORATORY"))
        {
            return "YES — exploratory evidence";
        }

        if (judged.Any(x => x.Label == "INSUFFICIENT_EVIDENCE"))
        {
            return "PARTIAL — promising but insufficient";
        }

        return "NO — no repeatable condition found";
    }

    private static Phase4Summary ReadPhase4(string path)
    {
        if (!File.Exists(path))
        {
            return new Phase4Summary(0, []);
        }

        var rows = JsonSerializer.Deserialize<List<Phase4Row>>(File.ReadAllText(path), JsonOptions) ?? [];
        var kept = rows.Where(x =>
            x.CostLabel == "BASE"
            && x.CandidateId is "funding_basis_rv|baseline|continuation" or "funding_extreme_momentum_exhaustion|baseline|continuation"
            && x.Phase is "IS" or "VALIDATION" or "OOS").ToList();
        var cells = kept.GroupBy(x => x.Phase + "|" + x.Regime + "|" + x.Side)
            .Select(g =>
            {
                var pnl = g.Select(x => x.PnL).ToList();
                var wins = pnl.Where(x => x > 0m).Sum();
                var losses = pnl.Where(x => x < 0m).Sum(x => Math.Abs(x));
                return new Phase4Cell(g.Key, g.Count(), wins, losses, pnl.Sum());
            })
            .OrderBy(x => x.Key)
            .ToList();
        return new Phase4Summary(kept.Count, cells);
    }

    private static void WriteReport(
        string root,
        string hash,
        IReadOnlyList<EdgeObservation> rows,
        IReadOnlyList<ConditionJudgement> judged,
        Phase4Summary phase4,
        string answer)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Edge discovery");
        sb.AppendLine();
        sb.AppendLine($"Definition SHA-256 `{hash}`.");
        sb.AppendLine();
        sb.AppendLine("Every relationship below is EXPLORATORY. None is VALIDATED_FOR_PAPER. No strategy was created or changed.");
        sb.AppendLine();
        sb.AppendLine("## 1. Trades analyzed");
        sb.AppendLine();
        sb.AppendLine($"Reconstructed cost-inclusive trades: {rows.Count}. Feature-ready: {rows.Count(x => x.Ready)}.");
        sb.AppendLine($"Phase 4 baseline trades read from the existing file, not rerun: {phase4.Trades}.");
        sb.AppendLine();
        sb.AppendLine("## 2. Families");
        sb.AppendLine();
        sb.AppendLine("Frozen Five templates at 15m, unmodified. Final Five primary ids at their pre-registered timeframe. Phase 8 baselines for sweep, pullback, W/M, compression continuation, MTF, and failed breakout at 5m. Near-miss is the same Phase 8 book and was not counted twice. Funding baselines are summarized from the existing Phase 4 trade file.");
        sb.AppendLine();
        sb.AppendLine("Independent groups used by the gate: FROZEN_TREND, FROZEN_MEAN, PA_SWEEP, PA_FAILED, PA_CONTINUATION, PA_STRUCTURE. Sweep and failed-breakout variants inside one group are related, not independent.");
        sb.AppendLine();
        sb.AppendLine("## 3. Features");
        sb.AppendLine();
        sb.AppendLine("Taken on the completed signal bar, before the next-bar fill: ATR percentile, ADX, bar range versus ATR, eight-bar compression then release, 1h structure bias, BTC 1h trend and ATR percentile, same-clock BTC relative return, UTC session, UTC weekday. No future bar enters a feature.");
        sb.AppendLine();
        sb.AppendLine("## 4. Regimes");
        sb.AppendLine();
        sb.AppendLine("Orthogonal bins, not a crossed grid: volatility low/normal/high, ADX range/trend, bar expansion/contraction, compression-release, hourly alignment, BTC trend alignment, BTC volatility. " + judged.Count + " conditions were scored, including 7 weekdays. They were not crossed with each other.");
        sb.AppendLine();
        sb.AppendLine(EdgeFeatureCatalog.InterestingRule);
        sb.AppendLine();
        sb.AppendLine("| Condition | Label | In n | In PF | In net | In exp | Out n | Out PF | Out net | Groups | Symbols | Blocks | IS net | VAL net | OOS net |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var row in judged)
        {
            sb.AppendLine($"| {row.Id} | {row.Label} | {row.Inside.Trades} | {Pf(row.Inside)} | {Money(row.Inside.Net)} | {Money(row.Inside.Expectancy)} | {row.Outside.Trades} | {Pf(row.Outside)} | {Money(row.Outside.Net)} | {row.Groups.Length} | {string.Join(',', row.Symbols)} | {row.PositiveBlocks} | {Money(row.Is.Net)} | {Money(row.Validation.Net)} | {Money(row.Oos.Net)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 5. Time");
        sb.AppendLine();
        sb.AppendLine("Blocks are equal index quarters of each symbol series. IS, validation, and OOS use the existing chronological split. A late OOS profit that is absent from IS and validation does not pass the gate.");
        sb.AppendLine();
        sb.AppendLine("## 6. Symbols");
        sb.AppendLine();
        sb.AppendLine("BTC, ETH, and BNB only. BNB was not chosen for this phase. The previous larger universe was not reconstructed. A condition needs two of these three coins.");
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var cell = Totals(rows.Where(x => x.Symbol == symbol).ToList());
            sb.AppendLine($"- {symbol}: n={cell.Trades} PF={Pf(cell)} net={Money(cell.Net)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Long and short");
        sb.AppendLine();
        foreach (var side in new[] { "LONG", "SHORT" })
        {
            var cell = Totals(rows.Where(x => x.Side == side).ToList());
            sb.AppendLine($"- {side}: n={cell.Trades} PF={Pf(cell)} net={Money(cell.Net)} expectancy={Money(cell.Expectancy)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 8. FF-SWEEP-HOURLY|15m");
        sb.AppendLine();
        AppendDeep(sb, rows, "FF-SWEEP-HOURLY");
        sb.AppendLine();
        sb.AppendLine("## 9. FF-FAILED-DOWN|15m");
        sb.AppendLine();
        AppendDeep(sb, rows, "FF-FAILED-DOWN");
        sb.AppendLine();
        sb.AppendLine("## 10. Phase 8");
        sb.AppendLine();
        foreach (var family in new[] { "CPA-SWEEP", "CPA-PULLBACK", "CPA-WM", "CPA-COMPRESSION", "CPA-MTF", "CPA-FAILED_BREAKOUT" })
        {
            var cell = Totals(rows.Where(x => x.Family == family).ToList());
            var oos = Totals(rows.Where(x => x.Family == family && x.Phase == "OOS").ToList());
            sb.AppendLine($"- {family}: all n={cell.Trades} PF={Pf(cell)} net={Money(cell.Net)}; OOS n={oos.Trades} PF={Pf(oos)} net={Money(oos.Net)}");
        }

        sb.AppendLine();
        sb.AppendLine("Phase 4 existing labels, baseline continuation only, BASE cost. This taxonomy is the old mutually exclusive regime tag. It is not crossed into the bins above.");
        sb.AppendLine();
        sb.AppendLine("| Cell | n | PF | Net |");
        sb.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var cell in phase4.Cells.Where(x => x.Key.StartsWith("OOS|", StringComparison.Ordinal)))
        {
            var pf = cell.Losses == 0m ? "n/a" : (cell.Wins / cell.Losses).ToString("0.000", CultureInfo.InvariantCulture);
            sb.AppendLine($"| {cell.Key} | {cell.Trades} | {pf} | {Money(cell.Net)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Cross-strategy");
        sb.AppendLine();
        sb.AppendLine("A shared condition counts only when two independent groups both have positive in-set net with at least 25 trades. Related price-action variants inside one group do not qualify as two families.");
        sb.AppendLine();
        sb.AppendLine("## 12. Edge clusters");
        sb.AppendLine();
        var clusters = judged.Where(x => x.Label is "EXPLORATORY" or "INSUFFICIENT_EVIDENCE").OrderByDescending(x => x.Inside.Trades).Take(5).ToList();
        if (clusters.Count == 0)
        {
            sb.AppendLine("No condition separated winners from losers under the pre-registered rule. There is no edge cluster to name.");
        }

        foreach (var cluster in clusters)
        {
            sb.AppendLine($"### {cluster.Id}");
            sb.AppendLine();
            sb.AppendLine($"Label: `{cluster.Label}`.");
            sb.AppendLine();
            sb.AppendLine($"- Definition: {cluster.Definition}");
            sb.AppendLine($"- Trades: {cluster.Inside.Trades}. Profit factor {Pf(cluster.Inside)}. Expectancy {Money(cluster.Inside.Expectancy)}. Net {Money(cluster.Inside.Net)}. Closed-trade drawdown {Money(cluster.Inside.Drawdown)}%.");
            sb.AppendLine($"- Out-set: n={cluster.Outside.Trades} PF={Pf(cluster.Outside)} net={Money(cluster.Outside.Net)}.");
            sb.AppendLine($"- Symbols: {string.Join(", ", cluster.Symbols)}.");
            sb.AppendLine($"- Positive blocks: {cluster.PositiveBlocks} of 4.");
            sb.AppendLine($"- IS net {Money(cluster.Is.Net)} (n={cluster.Is.Trades}). Validation net {Money(cluster.Validation.Net)} (n={cluster.Validation.Trades}). OOS net {Money(cluster.Oos.Net)} (n={cluster.Oos.Trades}).");
            sb.AppendLine($"- Groups with positive net: {string.Join(", ", cluster.Groups)}.");
            sb.AppendLine($"- Approximate cost stress on the same fills: 1.25x net {Money(cluster.Stress.Mild)} , 1.5x net {Money(cluster.Stress.High)} , 2.0x net {Money(cluster.Stress.Stress)} . Stops were not re-simulated.");
            sb.AppendLine($"- Evidence for: the in-set profit factor is above 1 and the out-set is below 1 on this reconstructed book.");
            sb.AppendLine($"- Evidence against: the label is {cluster.Label}. It is not a strategy and it was not confirmed on the larger universe.");
            sb.AppendLine();
        }

        sb.AppendLine("## 13. Evidence for each cluster");
        sb.AppendLine();
        sb.AppendLine("Listed under each cluster. Where no cluster exists, there is no supporting cell.");
        sb.AppendLine();
        sb.AppendLine("## 14. Evidence against each cluster");
        sb.AppendLine();
        sb.AppendLine("Listed under each cluster. The out-set, the chronological split, and the group count are the checks.");
        sb.AppendLine();
        sb.AppendLine("## 15. Multiple testing");
        sb.AppendLine();
        sb.AppendLine($"{judged.Count} pre-registered conditions were scored on one reconstructed sample. Weekdays and sessions are included in that count. Conditions were not crossed, and no threshold was moved after the results. A green cell in this list is still exploratory.");
        sb.AppendLine();
        sb.AppendLine("## 16. Data limits");
        sb.AppendLine();
        sb.AppendLine("1h history is about two years. 15m is about one year. 5m is about one year. Funding cashflow is not inside the Frozen, Phase 8, or Final Five replays. Phase 4 trades do include recorded funding. Open interest, liquidations, and the order book were not used. ATR percentile is a 0–1 fraction. The larger symbol universe was not rebuilt.");
        sb.AppendLine();
        sb.AppendLine("## Answer");
        sb.AppendLine();
        sb.AppendLine(answer);
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. No production, risk, execution, or enablement change.");
        File.WriteAllText(Path.Combine(root, "docs", "EDGE_DISCOVERY_REPORT.md"), sb.ToString());
    }

    private static void AppendDeep(StringBuilder sb, IReadOnlyList<EdgeObservation> rows, string family)
    {
        var all = rows.Where(x => x.Family == family).ToList();
        var oos = all.Where(x => x.Phase == "OOS").ToList();
        var cell = Totals(all);
        var oosCell = Totals(oos);
        sb.AppendLine($"All blocks: n={cell.Trades} PF={Pf(cell)} net={Money(cell.Net)}. OOS: n={oosCell.Trades} PF={Pf(oosCell)} net={Money(oosCell.Net)}.");
        sb.AppendLine();
        sb.AppendLine("| Slice | OOS n | OOS PF | OOS net | Winner share | Loser share |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        void Line(string name, Func<EdgeObservation, bool?> flag)
        {
            var sample = oos.Where(x => flag(x) == true).ToList();
            var totals = Totals(sample);
            var winners = oos.Where(x => x.Pnl > 0m).ToList();
            var losers = oos.Where(x => x.Pnl < 0m).ToList();
            var winShare = winners.Count == 0 ? "n/a" : winners.Count(x => flag(x) == true) + "/" + winners.Count;
            var loseShare = losers.Count == 0 ? "n/a" : losers.Count(x => flag(x) == true) + "/" + losers.Count;
            sb.AppendLine($"| {name} | {totals.Trades} | {Pf(totals)} | {Money(totals.Net)} | {winShare} | {loseShare} |");
        }

        Line("LONG", x => x.Side == "LONG");
        Line("SHORT", x => x.Side == "SHORT" ? true : false);
        Line("VOL_HIGH", x => x.VolHigh);
        Line("VOL_LOW", x => x.VolLow);
        Line("ADX_TREND", x => x.AdxTrend);
        Line("ADX_RANGE", x => x.AdxRange);
        Line("EXPANSION", x => x.Expansion);
        Line("HTF_ALIGNED", x => x.HtfAligned);
        Line("BTC_ALIGNED", x => x.BtcAligned);
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var sample = Totals(oos.Where(x => x.Symbol == symbol).ToList());
            sb.AppendLine($"| {symbol} | {sample.Trades} | {Pf(sample)} | {Money(sample.Net)} |  |  |");
        }

        for (var block = 1; block <= 4; block++)
        {
            var sample = Totals(all.Where(x => x.Block == block).ToList());
            sb.AppendLine($"- Block {block}: n={sample.Trades} PF={Pf(sample)} net={Money(sample.Net)}");
        }
    }

    private static Cell Totals(IReadOnlyList<EdgeObservation> rows)
    {
        var wins = rows.Where(x => x.Pnl > 0m).Sum(x => x.Pnl);
        var losses = rows.Where(x => x.Pnl < 0m).Sum(x => Math.Abs(x.Pnl));
        var net = rows.Sum(x => x.Pnl);
        var equity = 1000m;
        var peak = equity;
        var dd = 0m;
        foreach (var row in rows.OrderBy(x => x.ClosedAt))
        {
            equity += row.Pnl;
            if (equity > peak)
            {
                peak = equity;
            }

            if (peak > 0m)
            {
                dd = Math.Max(dd, (peak - equity) / peak * 100m);
            }
        }

        var pf = rows.Count == 0 || losses == 0m ? 0m : wins / losses;
        if (rows.Count > 0 && losses == 0m && wins > 0m)
        {
            pf = 999m;
        }

        return new Cell(rows.Count, net, rows.Count == 0 ? 0m : net / rows.Count, pf, dd);
    }

    private static StressCell Stress(IReadOnlyList<EdgeObservation> rows)
    {
        decimal Scale(decimal multiplier) => rows.Sum(x => x.Pnl - ((multiplier - 1m) * (x.Fees + x.Slippage)));
        return new StressCell(Scale(1.25m), Scale(1.5m), Scale(2m));
    }

    private static string Pf(Cell cell) =>
        cell.Trades == 0 ? "n/a" : cell.ProfitFactor >= 999m ? "no-losses" : cell.ProfitFactor.ToString("0.000", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static async Task<Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> LoadSymbol(string cacheDir, string root, string symbol)
    {
        var map = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var tf in new[] { "5m", "15m", "1h" })
        {
            map[(symbol, tf)] = await LoadOne(cacheDir, root, symbol, tf);
        }

        return map;
    }

    private static async Task<IReadOnlyList<MarketCandle>> LoadOne(string cacheDir, string root, string symbol, string timeframe)
    {
        var coverage = JsonSerializer.Deserialize<CoverageFile>(
            await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Coverage artifact missing.");
        var range = coverage.Coverage.First(x =>
            string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase));
        var bars = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, timeframe);
        return bars.Where(x => x.OpenTime >= range.RequestedFrom && x.CloseTime <= range.RequestedTo && x.IsClosed)
            .OrderBy(x => x.OpenTime)
            .ToList();
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private sealed record Arm(string Id, string Group, string ResearchFamily, string Timeframe, ArmKind Kind)
    {
        public string Family => Id;
        public string CandidateId(string symbol) => Id + "|" + Timeframe + "|" + symbol;
    }

    private enum ArmKind { Frozen, FinalFive, Contextual }

    private sealed record ConditionSpec(string Id, string Definition, Func<EdgeObservation, bool?> In);

    private sealed record Cell(int Trades, decimal Net, decimal Expectancy, decimal ProfitFactor, decimal Drawdown);

    private sealed record StressCell(decimal Mild, decimal High, decimal Stress);

    private sealed record ConditionJudgement(
        string Id,
        string Definition,
        string Label,
        Cell Inside,
        Cell Outside,
        Cell Is,
        Cell Validation,
        Cell Oos,
        string[] Groups,
        string[] Symbols,
        int PositiveBlocks,
        StressCell Stress);

    private sealed record Phase4Row(string CandidateId, string Phase, string CostLabel, string Side, decimal PnL, string Regime);

    private sealed record Phase4Cell(string Key, int Trades, decimal Wins, decimal Losses, decimal Net);

    private sealed record Phase4Summary(int Trades, List<Phase4Cell> Cells);

    private sealed record CoverageFile(IReadOnlyList<CoverageSlice> Coverage);

    private sealed record CoverageSlice(string Symbol, string Timeframe, DateTimeOffset RequestedFrom, DateTimeOffset RequestedTo);
}
