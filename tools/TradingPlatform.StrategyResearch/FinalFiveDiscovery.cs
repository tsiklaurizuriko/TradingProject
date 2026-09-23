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

internal static class FinalFiveDiscovery
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static async Task<int> RunAsync(string root, string cacheDir, string[] args)
    {
        var dir = Path.Combine(root, "artifacts", "strategy-research", "final-five");
        Directory.CreateDirectory(dir);
        var definitionHash = WriteManifest(dir);
        WriteDefinitions(root, definitionHash);
        var maxParallel = Math.Clamp(ParseInt(args, "--max-parallel", 2), 1, 4);
        var candidates = Candidates();
        var books = new List<ResearchBookResult>();
        var signals = new Dictionary<(string CandidateId, string Symbol, string Timeframe), SignalType[]>();
        var seriesBySymbol = new Dictionary<string, Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            Console.WriteLine($"Final five loading {symbol}.");
            var series = await LoadSeries(cacheDir, root, symbol);
            seriesBySymbol[symbol] = series;
            var caches = series.ToDictionary(pair => pair.Key, pair => new CausalIndicatorCache(pair.Value));
            var precomputed = new Dictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>>();
            foreach (var id in FinalFiveCatalog.AllCandidateIds)
            {
                var tf = id.Split('|')[1];
                var entry = caches[(symbol, tf)];
                var hourly = caches[(symbol, "1h")];
                var built = FinalFiveSignals.Build(id, entry, hourly);
                signals[(id, symbol, tf)] = built;
                precomputed[(id, symbol, tf)] = built;
            }

            var rows = ResearchRunner.Evaluate(new ResearchRunRequest
            {
                Phase = "DISCOVERY",
                Candidates = candidates,
                Symbols = [symbol],
                Timeframes = FinalFiveCatalog.Timeframes,
                Series = series,
                CostLabels = [ResearchCostLabels.Base],
                MaxParallel = maxParallel,
                UseLowIsolated = true,
                HonorSuggestedStops = false,
                SkipWalkForward = true,
                IndicatorCaches = caches,
                PrecomputedSignals = precomputed,
                DataSnapshot = new ResearchDataSnapshot([ResearchDatasets.Ohlcv, ResearchDatasets.CompletedHtf])
            });
            books.AddRange(rows);
            WriteJson(Path.Combine(dir, "is-validation-results.json"), books);
            Console.WriteLine($"{symbol}: IS/VALIDATION books={books.Count}.");
        }

        var decisions = Decide(books);
        var survivorPayload = new
        {
            Version = FinalFiveCatalog.Version,
            FrozenAtUtc = DateTimeOffset.UtcNow,
            HypothesisManifestSha256 = definitionHash,
            SelectionRule = FinalFiveCatalog.SurvivorRule,
            OosReadBeforeFreeze = false,
            Decisions = decisions,
            SurvivorCandidateIds = decisions.Where(x => x.Passed).Select(x => x.CandidateId).ToArray()
        };
        var survivorJson = JsonSerializer.Serialize(survivorPayload, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(dir, "survivor-manifest.json"), survivorJson);
        await File.WriteAllTextAsync(Path.Combine(dir, "pre-oos-manifest.json"), survivorJson);
        Console.WriteLine($"Pre-OOS survivors: {survivorPayload.SurvivorCandidateIds.Length}. OOS starts only after this file exists.");

        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var series = seriesBySymbol[symbol];
            var caches = series.ToDictionary(pair => pair.Key, pair => new CausalIndicatorCache(pair.Value));
            var precomputed = signals
                .Where(x => string.Equals(x.Key.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(x => x.Key, x => (IReadOnlyList<SignalType>)x.Value);
            books.AddRange(Evaluate(symbol, series, caches, precomputed, candidates, maxParallel, ResearchPhases.Oos, FinalFiveCatalog.CostLabels, blocks: false, walkForward: false));
            books.AddRange(Evaluate(symbol, series, caches, precomputed, candidates, maxParallel, "BLOCKS", [ResearchCostLabels.Base], blocks: true, walkForward: false));
            books.AddRange(Evaluate(symbol, series, caches, precomputed, candidates, maxParallel, ResearchPhases.WalkForward, [ResearchCostLabels.Base], blocks: false, walkForward: true));

            WriteJson(Path.Combine(dir, "oos-results.json"), books.Where(x => x.Phase == "OOS").ToList());
            Console.WriteLine($"{symbol}: OOS/walk-forward checkpoint books={books.Count}.");
        }

        WriteJson(Path.Combine(dir, "walk-forward-results.json"), books.Where(x => x.Phase.StartsWith("WF", StringComparison.Ordinal)).ToList());
        WriteJson(Path.Combine(dir, "chronological-blocks.json"), books.Where(x => x.Phase.StartsWith("BLOCK", StringComparison.Ordinal)).ToList());
        WriteJson(Path.Combine(dir, "cost-stress.json"), books.Where(x => x.Phase == "OOS").ToList());
        WriteJson(Path.Combine(dir, "symbol-robustness.json"), SymbolRows(books));
        var portfolio = Portfolio(signals, seriesBySymbol);
        WriteJson(Path.Combine(dir, "portfolio-replay.json"), portfolio);
        WriteJson(Path.Combine(dir, "final-five-summary.json"), new
        {
            DefinitionSha256 = definitionHash,
            SurvivorSha256 = Sha256(survivorJson),
            LiveOff = true,
            PaperOff = true,
            ValidatedForPaper = false,
            Survivors = survivorPayload.SurvivorCandidateIds,
            Funding = "Existing Model B replay includes fees and slippage. It does not apply a funding cashflow. No funding series was fabricated."
        });
        WriteResearchReport(root, definitionHash, Sha256(survivorJson), decisions, books, portfolio);
        Console.WriteLine("FINAL FIVE ALPHA RESEARCH COMPLETE");
        return 0;
    }

    private static string WriteManifest(string dir)
    {
        var body = JsonSerializer.Serialize(new
        {
            FinalFiveCatalog.Version,
            FinalFiveCatalog.Symbols,
            FinalFiveCatalog.ThirdSymbolRule,
            FinalFiveCatalog.Timeframes,
            FinalFiveCatalog.SurvivorRule,
            FinalFiveCatalog.CostLabels,
            Primary = FinalFiveCatalog.Primary,
            Rejected = FinalFiveCatalog.RejectedBeforeRun,
            Causality = "Signal at index i uses entry candles 0..i and the last higher-timeframe candle whose CloseTime is <= the entry CloseTime. A later bar cannot change an earlier signal.",
            Entry = "Completed candle. The existing replay fills the next bar open.",
            Exit = "Model B Isolated LOW 2% stop and 4% target. HonorSuggestedStops is false. Exits were not searched.",
            LiveOff = true,
            PaperPromotionOff = true
        }, JsonOptions);
        var hash = Sha256(body);
        File.WriteAllText(Path.Combine(dir, "hypothesis-manifest.json"), JsonSerializer.Serialize(new { Sha256 = hash, Body = JsonDocument.Parse(body).RootElement }, JsonOptions));
        return hash;
    }

    private static void WriteDefinitions(string root, string hash)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Final five strategy definitions");
        sb.AppendLine();
        sb.AppendLine($"Definition SHA-256 `{hash}`.");
        sb.AppendLine();
        sb.AppendLine(FinalFiveCatalog.ThirdSymbolRule);
        sb.AppendLine();
        sb.AppendLine(FinalFiveCatalog.SurvivorRule);
        sb.AppendLine();
        sb.AppendLine("These definitions are research signals. They are not in the operator catalog, paper is off, and live is off.");
        sb.AppendLine();
        foreach (var row in FinalFiveCatalog.Primary)
        {
            sb.AppendLine($"## {row.CandidateId}");
            sb.AppendLine();
            sb.AppendLine($"- Name: {row.Name}");
            sb.AppendLine($"- Side rule: {row.SideRule}");
            sb.AppendLine($"- Entry timeframe: {row.EntryTimeframe}. The same rule is also scored on 1m, 3m, 5m, 15m, and 1h as robustness. A robustness timeframe cannot replace the primary id.");
            sb.AppendLine($"- Signal: {row.Entry}");
            sb.AppendLine($"- Confirmation: {row.Confirmation}");
            sb.AppendLine($"- Context: {row.Context}");
            sb.AppendLine($"- Exit: {row.Exit}");
            sb.AppendLine($"- Why this is not a renamed prior strategy: {row.Why}");
            sb.AppendLine();
        }

        sb.AppendLine("## Rejected before the run");
        sb.AppendLine();
        foreach (var row in FinalFiveCatalog.RejectedBeforeRun)
        {
            sb.AppendLine($"- `{row.Id}` {row.Name} {row.Reason}");
        }

        File.WriteAllText(Path.Combine(root, "docs", "FINAL_FIVE_STRATEGY_DEFINITIONS.md"), sb.ToString());
    }

    private static List<FinalDecision> Decide(IReadOnlyList<ResearchBookResult> books)
    {
        var decisions = new List<FinalDecision>();
        foreach (var row in FinalFiveCatalog.Primary)
        {
            var slice = books.Where(x => x.CandidateId == row.CandidateId && x.CostLabel == ResearchCostLabels.Base).ToList();
            var ins = Combine(slice.Where(x => x.Phase == "IS"));
            var val = Combine(slice.Where(x => x.Phase == "VALIDATION"));
            var symbols = slice.Count(x => x.Phase == "VALIDATION" && x.TradeCount > 0);
            var passed = ins.Trades >= 30
                && (ins.Negative == 0m && ins.Positive > 0m || ins.Negative > 0m && ins.Positive / ins.Negative >= 0.90m)
                && val.Trades >= 20
                && val.Negative > 0m
                && val.Positive / val.Negative > 1m
                && val.Net > 0m
                && symbols >= 2;
            decisions.Add(new FinalDecision(row.CandidateId, ins.Trades, Pf(ins), ins.Net, val.Trades, Pf(val), val.Net, symbols, passed, passed ? "Passed the frozen pre-OOS gate." : "Failed the frozen pre-OOS gate. OOS is reported and does not promote it."));
        }

        return decisions;
    }

    private static object Portfolio(
        Dictionary<(string CandidateId, string Symbol, string Timeframe), SignalType[]> signals,
        Dictionary<string, Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> seriesBySymbol)
    {
        var intents = new List<OccupancyIntent>();
        var bars = new List<OccupancyBar>();
        var seenBars = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hypothesis in FinalFiveCatalog.Primary)
        {
            foreach (var symbol in FinalFiveCatalog.Symbols)
            {
                var tf = hypothesis.EntryTimeframe;
                if (!signals.TryGetValue((hypothesis.CandidateId, symbol, tf), out var row))
                {
                    continue;
                }

                var candles = seriesBySymbol[symbol][(symbol, tf)];
                if (seenBars.Add(symbol + "|" + tf))
                {
                    for (var i = 0; i < candles.Count; i++)
                    {
                        bars.Add(new OccupancyBar(candles[i].OpenTime, candles[i].CloseTime, symbol, candles[i].Open, candles[i].High, candles[i].Low, candles[i].Close));
                    }
                }

                var (_, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
                for (var i = valEnd; i < candles.Count - 1; i++)
                {
                    if (row[i] is not (SignalType.Buy or SignalType.Sell))
                    {
                        continue;
                    }

                    intents.Add(new OccupancyIntent(candles[i].CloseTime, candles[i + 1].OpenTime, hypothesis.CandidateId, symbol, row[i], candles[i + 1].Open));
                }
            }
        }

        if (intents.Count == 0 || bars.Count == 0)
        {
            return new { Trades = 0, Net = 0m, MaxDrawdownPercent = 0m, SameCoinRejects = 0, SlotRejects = 0, HeatRejects = 0 };
        }

        var from = bars.Min(x => x.OpenTime);
        var to = bars.Max(x => x.CloseTime);
        var replay = PortfolioOccupancyReplay.Run(intents, bars, StrategyValidation.LowIsolatedRisk(from, to));
        return new
        {
            TradeCount = replay.Trades.Count,
            Net = replay.NetProfit,
            replay.FinalEquity,
            replay.MaximumDrawdownPercent,
            replay.SameCoinRejects,
            replay.SlotRejects,
            replay.HeatRejects,
            Note = "OOS-window signals of the five primary ids on one Isolated book. This replay does not change the survivor list."
        };
    }

    private static IReadOnlyList<object> SymbolRows(IReadOnlyList<ResearchBookResult> books) =>
        books.Where(x => x.Phase is "IS" or "VALIDATION" or "OOS" && x.CostLabel == ResearchCostLabels.Base)
            .Select(x => new
            {
                x.CandidateId,
                x.Symbol,
                x.Timeframe,
                x.Phase,
                x.TradeCount,
                x.ProfitFactor,
                x.NetPnl,
                Long = x.LongTotals,
                Short = x.ShortTotals
            })
            .Cast<object>()
            .ToList();

    private static void WriteResearchReport(
        string root,
        string definitionHash,
        string survivorHash,
        IReadOnlyList<FinalDecision> decisions,
        IReadOnlyList<ResearchBookResult> books,
        object portfolio)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FINAL FIVE ALPHA RESEARCH COMPLETE");
        sb.AppendLine();
        sb.AppendLine($"Definition SHA-256 `{definitionHash}`. Survivor manifest SHA-256 `{survivorHash}`.");
        sb.AppendLine();
        sb.AppendLine("## A. What previous research showed");
        sb.AppendLine();
        sb.AppendLine("No prior family was VALIDATED_FOR_PAPER. The frozen five lost money after costs. Wave-2 mechanisms were rejected. Funding and open interest looked better on short windows and failed, or lacked history. Price-action context raised some profit factors and cut the trade count. Sweep-strict and compression continuation reached OOS with thin, concentrated, walk-forward-poor books. Shorts were often the weaker side. Five-trade profit factors were treated as noise.");
        sb.AppendLine();
        sb.AppendLine("## B. Hypotheses considered");
        sb.AppendLine();
        sb.AppendLine($"{FinalFiveCatalog.RejectedBeforeRun.Count + FinalFiveCatalog.Primary.Count} hypotheses were written down. {FinalFiveCatalog.RejectedBeforeRun.Count} were rejected before any backtest. {FinalFiveCatalog.Primary.Count} were implemented. Each implemented rule was also scored on 1m, 3m, 5m, 15m, and 1h. Those extra rows are robustness. They were not allowed to replace the pre-registered primary id.");
        sb.AppendLine();
        sb.AppendLine("Multiple testing: twenty-five candidate ids were scored, and five mechanisms were the only ones eligible for the survivor list. A green OOS cell among that set is expected by chance. OOS was not used to change a threshold, a side, or which timeframe is primary.");
        sb.AppendLine();
        sb.AppendLine("## C. Eliminated before testing");
        sb.AppendLine();
        foreach (var row in FinalFiveCatalog.RejectedBeforeRun)
        {
            sb.AppendLine($"- `{row.Id}`: {row.Reason}");
        }

        sb.AppendLine();
        sb.AppendLine("## D. Pre-OOS gate");
        sb.AppendLine();
        sb.AppendLine(FinalFiveCatalog.SurvivorRule);
        sb.AppendLine();
        sb.AppendLine("| Id | IS n | IS PF | IS net | VAL n | VAL PF | VAL net | VAL symbols | Passed |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var row in decisions)
        {
            sb.AppendLine($"| {row.CandidateId} | {row.IsTrades} | {row.IsPf} | {Money(row.IsNet)} | {row.ValidationTrades} | {row.ValidationPf} | {Money(row.ValidationNet)} | {row.ValidationSymbols} | {row.Passed} |");
        }

        sb.AppendLine();
        sb.AppendLine("## E. OOS");
        sb.AppendLine();
        sb.AppendLine("BASE cost. Drawdown is the worst per-symbol book drawdown in percent of that book's equity. It is not a combined three-coin equity curve. The combined curve is in section I.");
        sb.AppendLine();
        AppendPhase(sb, books, "OOS", ResearchCostLabels.Base);
        sb.AppendLine();
        sb.AppendLine("Chronological quarters (BLOCK1 through BLOCK4), BASE cost, primary ids. A block is the same rule on an earlier slice of the same series. It is not a new strategy.");
        sb.AppendLine();
        AppendBlocks(sb, books);
        sb.AppendLine();
        sb.AppendLine("## F. Walk-forward");
        sb.AppendLine();
        AppendWalkForward(sb, books);
        sb.AppendLine();
        sb.AppendLine("## G. Cost stress");
        sb.AppendLine();
        foreach (var cost in FinalFiveCatalog.CostLabels)
        {
            sb.AppendLine($"### {cost}");
            sb.AppendLine();
            AppendPhase(sb, books, "OOS", cost);
            sb.AppendLine();
        }

        sb.AppendLine("## H. Symbol robustness");
        sb.AppendLine();
        sb.AppendLine("OOS BASE by coin. BNBUSDT was chosen by the pre-existing volume rank, before these numbers existed.");
        sb.AppendLine();
        AppendSymbols(sb, books);
        sb.AppendLine();
        sb.AppendLine("The regime tag on each book is the classification of the first evaluated bar of that window, not a per-trade regime. On these three coins the OOS window mostly opens as LOW_VOLATILITY, so this run does not show trend, range, and high-volatility behavior separately.");
        sb.AppendLine();
        sb.AppendLine("## I. Portfolio");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine(JsonSerializer.Serialize(portfolio, JsonOptions));
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## J. The five strategies");
        sb.AppendLine();
        foreach (var hypothesis in FinalFiveCatalog.Primary)
        {
            var decision = decisions.First(x => x.CandidateId == hypothesis.CandidateId);
            var oos = Combine(books.Where(x => x.CandidateId == hypothesis.CandidateId && x.Phase == "OOS" && x.CostLabel == ResearchCostLabels.Base));
            var label = decision.Passed ? "PRE_OOS_PASS" : "INSUFFICIENT_EVIDENCE";
            sb.AppendLine($"### {hypothesis.CandidateId} — {hypothesis.Name}");
            sb.AppendLine();
            sb.AppendLine($"Label: `{label}`. Not VALIDATED_FOR_PAPER.");
            sb.AppendLine();
            sb.AppendLine("| Item | Value |");
            sb.AppendLine("| --- | --- |");
            sb.AppendLine($"| Side | {hypothesis.SideRule} |");
            sb.AppendLine($"| Entry | {hypothesis.Entry} |");
            sb.AppendLine($"| Confirmation | {hypothesis.Confirmation} |");
            sb.AppendLine($"| Context | {hypothesis.Context} |");
            sb.AppendLine($"| Exit | {hypothesis.Exit} |");
            sb.AppendLine($"| IS | n={decision.IsTrades} PF={decision.IsPf} net={Money(decision.IsNet)} |");
            sb.AppendLine($"| Validation | n={decision.ValidationTrades} PF={decision.ValidationPf} net={Money(decision.ValidationNet)} symbols={decision.ValidationSymbols} |");
            sb.AppendLine($"| OOS BASE | n={oos.Trades} PF={Pf(oos)} net={Money(oos.Net)} |");
            sb.AppendLine($"| Difference | {hypothesis.Why} |");
            sb.AppendLine();
        }

        sb.AppendLine("## Answers");
        sb.AppendLine();
        sb.AppendLine("1. Each strategy exploits one behavior: hourly-aligned sweep reclaim, hourly-aligned expansion, quiet-range RSI reclaim, a failed downside break, or a 1h contraction release.");
        sb.AppendLine("2. They are not renames. Each drops a stack or a regime that already failed, or moves the question off the 5m book that walk-forward could not support.");
        sb.AppendLine("3. The failures they address are listed on each definition: cost-eaten trends, RSI in trends, short sweeps, confirmation stacks, and the untuned 1h triangle.");
        sb.AppendLine("4. Evidence is the IS, validation, OOS, walk-forward, and cost tables above. Empty or tiny samples are not evidence.");
        sb.AppendLine("5. Anything that misses the pre-OOS gate is weak even if one OOS cell is green.");
        sb.AppendLine("6. 1m and 3m history in the cache is about 180 days. 5m and 15m are about one year. 1h is about two years. Funding cashflow is not in the Model B replay. Open interest, liquidations, and the order book were not used.");
        sb.AppendLine("7. Ready for PAPER: no.");
        sb.AppendLine("8. VALIDATED_FOR_PAPER: no.");
        sb.AppendLine("9. LIVE allowed: no.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. Price Action LIVE = OFF. Scalping LIVE = OFF. No production risk, execution, Isolated margin, or portfolio-risk change. No Binance order was sent by this run.");
        File.WriteAllText(Path.Combine(root, "docs", "FINAL_FIVE_ALPHA_RESEARCH.md"), sb.ToString());
    }

    private static void AppendPhase(StringBuilder sb, IReadOnlyList<ResearchBookResult> books, string phase, string cost)
    {
        sb.AppendLine("| Id | n | PF | Net | Expectancy | Win rate | Long n/PF | Short n/PF | Worst book DD % |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | --- | --- | ---: |");
        foreach (var hypothesis in FinalFiveCatalog.Primary)
        {
            var sample = books.Where(x => x.CandidateId == hypothesis.CandidateId && x.Phase == phase && x.CostLabel == cost && x.Status != ResearchStatuses.SkippedTimeframe).ToList();
            var totals = Combine(sample);
            var dd = sample.Count == 0 ? 0m : sample.Max(x => x.MaximumDrawdown ?? 0m);
            sb.AppendLine($"| {hypothesis.CandidateId} | {totals.Trades} | {Pf(totals)} | {Money(totals.Net)} | {Money(totals.Trades == 0 ? 0 : totals.Net / totals.Trades)} | {Win(sample)} | {SideCell(sample, longSide: true)} | {SideCell(sample, longSide: false)} | {Money(dd)} |");
        }
    }

    private static void AppendBlocks(StringBuilder sb, IReadOnlyList<ResearchBookResult> books)
    {
        sb.AppendLine("| Id | Block | n | PF | Net |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var hypothesis in FinalFiveCatalog.Primary)
        {
            for (var block = 1; block <= 4; block++)
            {
                var phase = "BLOCK" + block;
                var totals = Combine(books.Where(x => x.CandidateId == hypothesis.CandidateId && x.Phase == phase && x.CostLabel == ResearchCostLabels.Base));
                sb.AppendLine($"| {hypothesis.CandidateId} | {phase} | {totals.Trades} | {Pf(totals)} | {Money(totals.Net)} |");
            }
        }
    }

    private static void AppendSymbols(StringBuilder sb, IReadOnlyList<ResearchBookResult> books)
    {
        sb.AppendLine("| Id | Coin | OOS n | OOS PF | OOS net |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var hypothesis in FinalFiveCatalog.Primary)
        {
            foreach (var symbol in FinalFiveCatalog.Symbols)
            {
                var totals = Combine(books.Where(x =>
                    x.CandidateId == hypothesis.CandidateId
                    && x.Symbol == symbol
                    && x.Phase == "OOS"
                    && x.CostLabel == ResearchCostLabels.Base
                    && x.Status != ResearchStatuses.SkippedTimeframe));
                sb.AppendLine($"| {hypothesis.CandidateId} | {symbol} | {totals.Trades} | {Pf(totals)} | {Money(totals.Net)} |");
            }
        }
    }

    private static string SideCell(IReadOnlyList<ResearchBookResult> rows, bool longSide)
    {
        var trades = rows.Sum(x => (longSide ? x.LongTotals : x.ShortTotals)?.Trades ?? 0);
        var positive = rows.Sum(x => (longSide ? x.LongTotals : x.ShortTotals)?.PositivePnlSum ?? 0m);
        var negative = rows.Sum(x => (longSide ? x.LongTotals : x.ShortTotals)?.AbsoluteNegativePnlSum ?? 0m);
        var pf = trades == 0 ? "n/a" : negative == 0m ? (positive > 0m ? "no-losses" : "n/a") : (positive / negative).ToString("0.000", CultureInfo.InvariantCulture);
        return $"{trades}/{pf}";
    }

    private static void AppendWalkForward(StringBuilder sb, IReadOnlyList<ResearchBookResult> books)
    {
        sb.AppendLine("| Id | Windows | n | PF | Net | Expectancy |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var hypothesis in FinalFiveCatalog.Primary)
        {
            var rows = books.Where(x => x.CandidateId == hypothesis.CandidateId && x.Phase.StartsWith("WF", StringComparison.Ordinal) && x.CostLabel == ResearchCostLabels.Base).ToList();
            var totals = Combine(rows);
            sb.AppendLine($"| {hypothesis.CandidateId} | {rows.Count} | {totals.Trades} | {Pf(totals)} | {Money(totals.Net)} | {Money(totals.Trades == 0 ? 0 : totals.Net / totals.Trades)} |");
        }
    }

    private static string Win(IReadOnlyList<ResearchBookResult> rows)
    {
        var trades = rows.Sum(x => x.TradeCount);
        var wins = rows.Sum(x => x.WinningTrades);
        return trades == 0 ? "n/a" : (wins / (decimal)trades).ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static Totals Combine(IEnumerable<ResearchBookResult> rows)
    {
        var totals = new Totals();
        foreach (var row in rows)
        {
            totals.Trades += row.TradeCount;
            totals.Net += row.NetPnl;
            totals.Positive += row.PositivePnlSum;
            totals.Negative += row.AbsoluteNegativePnlSum;
        }

        return totals;
    }

    private static string Pf(Totals totals) =>
        totals.Trades == 0 ? "n/a"
        : totals.Negative == 0m ? (totals.Positive > 0m ? "no-losses" : "n/a")
        : (totals.Positive / totals.Negative).ToString("0.000", CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static IReadOnlyList<ResearchBookResult> Evaluate(
        string symbol,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache> caches,
        IReadOnlyDictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>> precomputed,
        IReadOnlyList<ResearchCandidate> candidates,
        int maxParallel,
        string phase,
        IReadOnlyList<string> costs,
        bool blocks,
        bool walkForward) =>
        ResearchRunner.Evaluate(new ResearchRunRequest
        {
            Phase = phase,
            Candidates = candidates,
            Symbols = [symbol],
            Timeframes = FinalFiveCatalog.Timeframes,
            Series = series,
            CostLabels = costs,
            MaxParallel = maxParallel,
            UseLowIsolated = true,
            HonorSuggestedStops = false,
            SkipWalkForward = !walkForward,
            IncludeChronologicalBlocks = blocks,
            IncludeWalkForward = walkForward,
            IndicatorCaches = caches,
            PrecomputedSignals = precomputed,
            DataSnapshot = new ResearchDataSnapshot([ResearchDatasets.Ohlcv, ResearchDatasets.CompletedHtf])
        });

    private static async Task<Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> LoadSeries(string cacheDir, string root, string symbol)
    {
        var coverage = JsonSerializer.Deserialize<CoverageFile>(
            await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json")),
            JsonOptions) ?? throw new InvalidOperationException("Phase 7 coverage artifact is missing.");
        var map = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var tf in FinalFiveCatalog.Timeframes)
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

    private static IReadOnlyList<ResearchCandidate> Candidates()
    {
        var created = DateTimeOffset.Parse("2026-09-23T00:00:00Z", CultureInfo.InvariantCulture);
        return FinalFiveCatalog.AllCandidateIds.Select(id =>
        {
            var tf = id.Split('|')[1];
            var primary = FinalFiveCatalog.Primary.First(x => FinalFiveCatalog.Mechanism(x.CandidateId) == FinalFiveCatalog.Mechanism(id));
            return new ResearchCandidate(
                id,
                StrategyTemplateKeys.PaStructureBreak,
                1,
                primary.Name + ". " + primary.Entry,
                ResearchKinds.Native,
                StrategyTemplateKeys.PaStructureBreak,
                "final-five",
                ["closed-OHLCV"],
                primary.Entry,
                primary.Exit,
                new ResearchFilters(ContextTimeframe: "1h"),
                new ResearchNativeParams(),
                [tf],
                ["LONG", "SHORT"],
                [],
                created,
                "Final five research. Not a production template.",
                ResearchStatuses.Researching,
                FinalFiveCatalog.Version);
        }).ToArray();
    }

    private static void WriteJson<T>(string path, T value) =>
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private static string Sha256(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int ParseInt(string[] args, string name, int fallback)
    {
        var index = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) ? value : fallback;
    }

    private sealed class Totals
    {
        public int Trades { get; set; }
        public decimal Net { get; set; }
        public decimal Positive { get; set; }
        public decimal Negative { get; set; }
    }

    private sealed record FinalDecision(
        string CandidateId,
        int IsTrades,
        string IsPf,
        decimal IsNet,
        int ValidationTrades,
        string ValidationPf,
        decimal ValidationNet,
        int ValidationSymbols,
        bool Passed,
        string Reason);

    private sealed record CoverageFile(IReadOnlyList<CoverageSlice> Coverage);

    private sealed record CoverageSlice(string Symbol, string Timeframe, DateTimeOffset RequestedFrom, DateTimeOffset RequestedTo);
}
