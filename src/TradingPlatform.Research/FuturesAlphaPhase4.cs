using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed record Phase4Spec(
    string CandidateId,
    string FamilyId,
    string TemplateKey,
    string Role,
    string Tag,
    bool Neighborhood,
    string BaselineId,
    StrategyTemplateParams Params);

public sealed record Phase4TradeRow(
    string CandidateId,
    string Symbol,
    string Timeframe,
    string Phase,
    string CostLabel,
    DateTimeOffset OpenedAt,
    string Side,
    decimal PnL,
    decimal Fees,
    decimal Slippage,
    decimal FundingPnl,
    decimal GrossPnl,
    string Regime);

public sealed record Phase4Bootstrap(
    string CandidateId,
    int Trades,
    int Draws,
    int Seed,
    decimal MeanExpectancy,
    decimal ExpectancyLo,
    decimal ExpectancyHi,
    decimal WinRate,
    decimal WinRateLo,
    decimal WinRateHi,
    decimal NetPnl,
    decimal NetPnlLo,
    decimal NetPnlHi,
    string Notes);

public static class FuturesAlphaPhase4
{
    public const int MinimumCombinedOosTrades = 50;
    public const int MinimumBookTrades = 20;
    public const int Phase3EnhancedHypotheses = 19;
    public const int Phase3Survivors = 2;
    public const int BootstrapDraws = 1000;
    public const int BootstrapSeed = 4;
    public const int ChronologicalBlocks = 4;

    public static readonly string[] CostGrid =
    [
        ResearchCostLabels.Base,
        ResearchCostLabels.Mild,
        ResearchCostLabels.High,
        ResearchCostLabels.Stress
    ];

    public static IReadOnlyList<Phase4Spec> FrozenSpecs()
    {
        var a = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingBasisRv, false) with
        {
            ZScoreEntry = 2m,
            FundingLookback = 24,
            EntryLookback = 40,
            AllowedSide = StrategySides.Both,
            FundingHypothesis = "continuation",
            OiHypothesis = "continuation"
        };
        var b = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingExtremeMomentumExhaustion, false) with
        {
            FundingExtremePercentile = 0.10m,
            PriceDisplacementAtr = 1.5m,
            AllowedSide = StrategySides.Both,
            FundingHypothesis = "continuation",
            OiHypothesis = "continuation"
        };
        return
        [
            Spec("funding_basis_rv", StrategyTemplateKeys.FundingBasisRv, "enhanced", "frozen", false, a with { UseFuturesFilter = true }),
            Spec("funding_basis_rv", StrategyTemplateKeys.FundingBasisRv, "baseline", "frozen", false, a with { UseFuturesFilter = false }),
            Spec("funding_extreme_momentum_exhaustion", StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "enhanced", "frozen", false, b with { UseFuturesFilter = true }),
            Spec("funding_extreme_momentum_exhaustion", StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "baseline", "frozen", false, b with { UseFuturesFilter = false })
        ];
    }

    public static IReadOnlyList<Phase4Spec> NeighborhoodSpecs()
    {
        var frozen = FrozenSpecs().Where(s => s.Role == "enhanced").ToList();
        var a = frozen.First(s => s.FamilyId == "funding_basis_rv");
        var b = frozen.First(s => s.FamilyId == "funding_extreme_momentum_exhaustion");
        return
        [
            a with { CandidateId = "funding_basis_rv|enhanced|z1.75", Tag = "z1.75", Neighborhood = true, Params = a.Params with { ZScoreEntry = 1.75m } },
            a with { CandidateId = "funding_basis_rv|enhanced|z2.25", Tag = "z2.25", Neighborhood = true, Params = a.Params with { ZScoreEntry = 2.25m } },
            b with { CandidateId = "funding_extreme_momentum_exhaustion|enhanced|p075", Tag = "p075", Neighborhood = true, Params = b.Params with { FundingExtremePercentile = 0.075m } },
            b with { CandidateId = "funding_extreme_momentum_exhaustion|enhanced|p125", Tag = "p125", Neighborhood = true, Params = b.Params with { FundingExtremePercentile = 0.125m } }
        ];
    }

    public static (IReadOnlyList<ResearchBookResult> Books, IReadOnlyList<Phase4TradeRow> Trades) Evaluate(
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> timeframes,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), StrategyFuturesSeries> futures,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>> fundingBySymbol,
        int maxParallel = 2)
    {
        var jobs = FrozenSpecs()
            .SelectMany(spec => symbols.SelectMany(symbol => timeframes.Select(tf => (spec, symbol, tf, neighborhood: false))))
            .Concat(NeighborhoodSpecs().SelectMany(spec => symbols.SelectMany(symbol => timeframes.Select(tf => (spec, symbol, tf, neighborhood: true)))))
            .ToArray();
        var books = new List<ResearchBookResult>();
        var trades = new List<Phase4TradeRow>();
        var gate = new object();
        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallel) },
            job =>
            {
                var (jobBooks, jobTrades) = EvaluateOne(job.spec, job.symbol, job.tf, series, futures, fundingBySymbol, job.neighborhood);
                lock (gate)
                {
                    books.AddRange(jobBooks);
                    trades.AddRange(jobTrades);
                }
            });
        return (
            books.OrderBy(r => r.CandidateId).ThenBy(r => r.Symbol).ThenBy(r => r.Timeframe).ThenBy(r => r.Phase).ThenBy(r => r.CostLabel).ToList(),
            trades.OrderBy(t => t.OpenedAt).ToList());
    }

    public static string RenderMarkdown(
        IReadOnlyList<ResearchBookResult> books,
        IReadOnlyList<Phase4TradeRow> trades,
        IReadOnlyList<string> notes,
        IReadOnlyList<Phase4Bootstrap> bootstrap)
    {
        var sb = new StringBuilder();
        var frozen = FrozenSpecs();
        sb.AppendLine("# Futures alpha Phase 4 report");
        sb.AppendLine();
        sb.AppendLine("Deep validation of two Phase 3 continuation hypotheses. LIVE = OFF. Risk Engine unchanged. Frozen five unchanged. No 528-universe run. No VALIDATED_FOR_PAPER. No OOS parameter tuning. No OI.");
        sb.AppendLine();
        sb.AppendLine("## 1. Frozen Phase 3 replication");
        sb.AppendLine("Parameters are the Phase 3 pre-registered defaults. Continuation direction logic is unchanged. Neighborhood values were evaluated on IS only.");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            WriteSlice(sb, books, spec.CandidateId, "OOS", ResearchCostLabels.Base, "OOS BASE frozen");
        }

        sb.AppendLine();
        sb.AppendLine("## 2. Historical coverage");
        foreach (var note in notes.Where(n => n.StartsWith("COVERAGE", StringComparison.Ordinal) || n.Contains("funding n=", StringComparison.Ordinal) || n.Contains("bars=", StringComparison.Ordinal)))
        {
            sb.AppendLine($"- {note}");
        }

        sb.AppendLine();
        sb.AppendLine("## 3. Chronological blocks");
        sb.AppendLine("Four contiguous blocks over the full available sample. PF is summed win/loss PnL, never an average of PFs.");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            sb.AppendLine($"### {spec.CandidateId}");
            for (var i = 1; i <= ChronologicalBlocks; i++)
            {
                WriteSlice(sb, books, spec.CandidateId, $"BLOCK{i}", ResearchCostLabels.Base, $"BLOCK{i}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 4. Walk-forward");
        sb.AppendLine("Test windows only. Empty window = NO_TRADES.");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            sb.AppendLine($"### {spec.CandidateId}");
            var wf = books.Where(b => b.CandidateId == spec.CandidateId && b.Phase.StartsWith("WF", StringComparison.Ordinal) && b.CostLabel == ResearchCostLabels.Base)
                .GroupBy(b => b.Phase)
                .OrderBy(g => g.Key);
            foreach (var g in wf)
            {
                var rows = g.ToList();
                WriteTotals(sb, g.Key, Combine(rows), rows);
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 5–6. LONG / SHORT and trade count (OOS BASE frozen enhanced)");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            var oos = Slice(books, spec.CandidateId, "OOS", ResearchCostLabels.Base);
            var oosTrades = trades.Where(t => t.CandidateId == spec.CandidateId && t.Phase == "OOS" && t.CostLabel == ResearchCostLabels.Base).ToList();
            var longT = ResearchDiagnostics.CombinePf(oos.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
            var shortT = ResearchDiagnostics.CombinePf(oos.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
            var uniqueSymbols = oosTrades.Select(t => t.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var uniqueDays = oosTrades.Select(t => t.OpenedAt.UtcDateTime.Date).Distinct().Count();
            var uniqueWeeks = oosTrades.Select(t => $"{ISOWeek.GetYear(t.OpenedAt.UtcDateTime)}-{ISOWeek.GetWeekOfYear(t.OpenedAt.UtcDateTime):00}").Distinct().Count();
            sb.AppendLine($"- `{spec.CandidateId}` trades={oosTrades.Count} uniqueSymbols={uniqueSymbols} uniqueDays={uniqueDays} uniqueWeeks={uniqueWeeks}");
            sb.AppendLine($"  LONG n={longT.Trades} PF {Pf(longT)} exp={N(longT.Expectancy)} wr={N(longT.WinRate)}; SHORT n={shortT.Trades} PF {Pf(shortT)} exp={N(shortT.Expectancy)} wr={N(shortT.WinRate)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Symbols");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            sb.AppendLine($"### {spec.CandidateId}");
            WriteSymbols(sb, books, spec.CandidateId);
        }

        sb.AppendLine();
        sb.AppendLine("## 8. Regimes");
        sb.AppendLine("Causal 40-bar classifier at fill time. HIGH_VOL / LOW_VOL take precedence over trend labels. n<20 = INSUFFICIENT_DATA.");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            sb.AppendLine($"### {spec.CandidateId}");
            var rows = trades.Where(t => t.CandidateId == spec.CandidateId && t.Phase == "OOS" && t.CostLabel == ResearchCostLabels.Base).ToList();
            foreach (var regime in new[] { "STRONG_BULL", "BULL", "RANGE", "BEAR", "STRONG_BEAR", "HIGH_VOL", "LOW_VOL", "TRANSITION" })
            {
                var slice = rows.Where(t => t.Regime == regime).ToList();
                if (slice.Count < 20)
                {
                    sb.AppendLine($"- {regime}: INSUFFICIENT_DATA n={slice.Count}");
                    continue;
                }

                var totals = PnlTotals.FromTrades(slice.Select(AsReplayTrade));
                sb.AppendLine($"- {regime}: n={totals.Trades} PF {Pf(totals)} exp={N(totals.Expectancy)}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 9. Cost stress (OOS Combined)");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            sb.Append($"- `{spec.CandidateId}`");
            decimal? basePf = null;
            var fragile = false;
            foreach (var cost in CostGrid)
            {
                var totals = Combine(Slice(books, spec.CandidateId, "OOS", cost));
                var (state, pf) = ResearchPf.From(totals);
                if (cost == ResearchCostLabels.Base)
                {
                    basePf = pf;
                }

                if (cost == ResearchCostLabels.High && basePf is > 1m && (pf is null || pf < 1m))
                {
                    fragile = true;
                }

                sb.Append($" | {cost} n={totals.Trades} PF {ResearchPf.Render(state, pf)} exp={N(totals.Expectancy)}");
            }

            sb.AppendLine(fragile ? " **COST_FRAGILE**" : "");
        }

        sb.AppendLine();
        sb.AppendLine("## 10. Funding costs");
        sb.AppendLine("Identity: GrossPnl − Fees − Slippage − FundingPaid = NetPnl. FundingPaid > 0 means the book paid funding.");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            var oosTrades = trades.Where(t => t.CandidateId == spec.CandidateId && t.Phase == "OOS" && t.CostLabel == ResearchCostLabels.Base).ToList();
            var gross = oosTrades.Sum(t => t.GrossPnl);
            var fees = oosTrades.Sum(t => t.Fees);
            var slip = oosTrades.Sum(t => t.Slippage);
            var fund = -oosTrades.Sum(t => t.FundingPnl);
            var net = oosTrades.Sum(t => t.PnL);
            var recon = gross - fees - slip - fund;
            sb.AppendLine($"- `{spec.CandidateId}` gross={N(gross)} fees={N(fees)} slippage={N(slip)} fundingPaid={N(fund)} net={N(net)} recon={N(recon)} gap={N(net - recon)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Baseline comparison (OOS BASE)");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            var enhRows = Slice(books, spec.CandidateId, "OOS", ResearchCostLabels.Base);
            var baseRows = Slice(books, spec.BaselineId, "OOS", ResearchCostLabels.Base);
            var enhanced = Combine(enhRows);
            var baseline = Combine(baseRows);
            var (es, ep) = ResearchPf.From(enhanced);
            var (bs, bp) = ResearchPf.From(baseline);
            var enhDd = enhRows.Select(NoteDecimal("dd=")).DefaultIfEmpty(0m).Max();
            var baseDd = baseRows.Select(NoteDecimal("dd=")).DefaultIfEmpty(0m).Max();
            sb.AppendLine($"- `{spec.CandidateId}` vs `{spec.BaselineId}`: ΔPF={Delta(ep, bp)} ΔExp={N(enhanced.Expectancy - baseline.Expectancy)} ΔWR={N(enhanced.WinRate - baseline.WinRate)} ΔNet={N(enhanced.NetPnl - baseline.NetPnl)} ΔDrawdown={N(enhDd - baseDd)} enhanced PF {ResearchPf.Render(es, ep)} baseline PF {ResearchPf.Render(bs, bp)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 12. Parameter stability (IS only)");
        sb.AppendLine("Neighborhood was not used to pick OOS parameters. Frozen defaults remain the OOS configuration.");
        foreach (var family in frozen.Where(s => s.Role == "enhanced"))
        {
            var isFrozen = Combine(Slice(books, family.CandidateId, "IS", ResearchCostLabels.Base));
            sb.AppendLine($"- `{family.CandidateId}` IS frozen n={isFrozen.Trades} PF {Pf(isFrozen)} exp={N(isFrozen.Expectancy)}");
            var neighborIds = NeighborhoodSpecs().Where(s => s.FamilyId == family.FamilyId).Select(s => s.CandidateId);
            var neighborExp = new List<decimal> { isFrozen.Expectancy };
            foreach (var id in neighborIds)
            {
                var row = Combine(Slice(books, id, "IS_PARAM", ResearchCostLabels.Base));
                neighborExp.Add(row.Expectancy);
                sb.AppendLine($"  - `{id}` IS_PARAM n={row.Trades} PF {Pf(row)} exp={N(row.Expectancy)}");
            }

            var stable = neighborExp.Count >= 3 && (neighborExp.All(x => x > 0m) || neighborExp.All(x => x <= 0m));
            sb.AppendLine($"  - {(stable ? "PARAMETER_STABLE" : "PARAMETER_FRAGILE")} on IS expectancy sign.");
        }

        sb.AppendLine();
        sb.AppendLine("## 13. Bootstrap uncertainty");
        sb.AppendLine("Seeded bootstrap of OOS BASE trade PnL. Not a proof of future profitability.");
        foreach (var row in bootstrap)
        {
            sb.AppendLine($"- `{row.CandidateId}` n={row.Trades} draws={row.Draws} seed={row.Seed}; exp {N(row.MeanExpectancy)} [{N(row.ExpectancyLo)}, {N(row.ExpectancyHi)}]; WR {N(row.WinRate)} [{N(row.WinRateLo)}, {N(row.WinRateHi)}]; net {N(row.NetPnl)} [{N(row.NetPnlLo)}, {N(row.NetPnlHi)}]");
        }

        sb.AppendLine();
        sb.AppendLine("## 14. Multiple-testing caveat");
        sb.AppendLine($"Phase 3 tested {Phase3EnhancedHypotheses} enhanced hypotheses (8 families × continuation/contrarian plus small extras, plus `vp_vwap_reversion`). {Phase3Survivors} continuation hypotheses entered Phase 4. Observed edge can plausibly include selection bias. This is not statistical significance and not a profitability claim.");
        sb.AppendLine();
        sb.AppendLine("## 15. Failure modes");
        sb.AppendLine("- Phase 3 OOS was ~3 weeks; Phase 4 uses the maximum ingested history but OOS is still the last 20% of that window.");
        sb.AppendLine("- One-direction or one-symbol concentration. SHORT sample on exhaustion may remain thin.");
        sb.AppendLine("- Cost fragility at 1.5x. Parameter neighborhood evaluated on IS only.");
        sb.AppendLine("- No OI. Taker not used. No fabricated series.");
        sb.AppendLine();
        sb.AppendLine("## 16. Final research status");
        foreach (var spec in frozen.Where(s => s.Role == "enhanced"))
        {
            sb.AppendLine($"- `{spec.CandidateId}`: {AssignGate(books, trades, spec)}");
        }

        sb.AppendLine();
        sb.AppendLine("VALIDATED_FOR_PAPER is not assigned. LIVE remains OFF. Risk Engine was not modified.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static IReadOnlyList<Phase4Bootstrap> Bootstrap(IReadOnlyList<Phase4TradeRow> trades)
    {
        var rows = new List<Phase4Bootstrap>();
        foreach (var spec in FrozenSpecs().Where(s => s.Role == "enhanced"))
        {
            var pnls = trades.Where(t => t.CandidateId == spec.CandidateId && t.Phase == "OOS" && t.CostLabel == ResearchCostLabels.Base)
                .Select(t => t.PnL)
                .ToList();
            rows.Add(BootstrapOne(spec.CandidateId, pnls));
        }

        return rows;
    }

    public static void WriteJson(string path, object payload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    public static object SymbolsPayload(IReadOnlyList<ResearchBookResult> books)
    {
        var rows = new List<object>();
        foreach (var spec in FrozenSpecs().Where(s => s.Role == "enhanced"))
        {
            var oos = Slice(books, spec.CandidateId, "OOS", ResearchCostLabels.Base);
            foreach (var g in oos.GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                var totals = Combine(g.ToList());
                var (state, pf) = ResearchPf.From(totals);
                rows.Add(new
                {
                    spec.CandidateId,
                    Symbol = g.Key,
                    totals.Trades,
                    ProfitFactorState = state,
                    ProfitFactor = pf,
                    totals.Expectancy,
                    totals.NetPnl,
                    Long = totals.WinningTrades,
                    Status = totals.Trades < MinimumBookTrades ? ResearchStatuses.InsufficientData : ResearchStatuses.Researching
                });
            }
        }

        return rows;
    }

    public static object RegimePayload(IReadOnlyList<Phase4TradeRow> trades)
    {
        var rows = new List<object>();
        foreach (var spec in FrozenSpecs().Where(s => s.Role == "enhanced"))
        {
            var oos = trades.Where(t => t.CandidateId == spec.CandidateId && t.Phase == "OOS" && t.CostLabel == ResearchCostLabels.Base);
            foreach (var g in oos.GroupBy(t => t.Regime))
            {
                var list = g.ToList();
                var totals = list.Count == 0 ? PnlTotals.Empty : PnlTotals.FromTrades(list.Select(AsReplayTrade));
                rows.Add(new
                {
                    spec.CandidateId,
                    Regime = g.Key,
                    totals.Trades,
                    ProfitFactor = ResearchPf.Render(ResearchPf.From(totals).State, ResearchPf.From(totals).Ratio),
                    totals.Expectancy,
                    Status = totals.Trades < 20 ? ResearchStatuses.InsufficientData : ResearchStatuses.Researching
                });
            }
        }

        return rows;
    }

    public static object CostPayload(IReadOnlyList<ResearchBookResult> books)
    {
        var rows = new List<object>();
        foreach (var spec in FrozenSpecs().Where(s => s.Role == "enhanced"))
        {
            foreach (var cost in CostGrid)
            {
                var totals = Combine(Slice(books, spec.CandidateId, "OOS", cost));
                var (state, pf) = ResearchPf.From(totals);
                rows.Add(new { spec.CandidateId, Cost = cost, totals.Trades, ProfitFactorState = state, ProfitFactor = pf, totals.Expectancy, totals.NetPnl, totals.Fees });
            }
        }

        return rows;
    }

    public static object WalkForwardPayload(IReadOnlyList<ResearchBookResult> books) =>
        FrozenSpecs().Where(s => s.Role == "enhanced").SelectMany(spec =>
            books.Where(b => b.CandidateId == spec.CandidateId && b.Phase.StartsWith("WF", StringComparison.Ordinal) && b.CostLabel == ResearchCostLabels.Base)
                .GroupBy(b => b.Phase)
                .Select(g =>
                {
                    var totals = Combine(g.ToList());
                    var (state, pf) = ResearchPf.From(totals);
                    return new { spec.CandidateId, Phase = g.Key, totals.Trades, ProfitFactorState = state, ProfitFactor = pf, totals.Expectancy, totals.NetPnl };
                })).ToList();

    private static (List<ResearchBookResult> Books, List<Phase4TradeRow> Trades) EvaluateOne(
        Phase4Spec spec,
        string symbol,
        string timeframe,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), StrategyFuturesSeries> futuresMap,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>> fundingBySymbol,
        bool neighborhood)
    {
        if (!series.TryGetValue((symbol, timeframe), out var candles) || candles.Count == 0)
        {
            return ([Skip(spec, symbol, timeframe, neighborhood ? "IS_PARAM" : "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "No candles.")], []);
        }

        var closed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
        futuresMap.TryGetValue((symbol, timeframe), out var futures);
        futures ??= new StrategyFuturesSeries();
        if (spec.Role == "enhanced" && spec.FamilyId == "funding_basis_rv"
            && (futures.FundingRate is null || futures.FundingRate.All(v => v is null) || futures.NormalizedBasis is null || futures.NormalizedBasis.All(v => v is null)))
        {
            return ([Skip(spec, symbol, timeframe, neighborhood ? "IS_PARAM" : "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Funding or basis missing. Not fabricated.")], []);
        }

        if (spec.Role == "enhanced" && spec.FamilyId == "funding_extreme_momentum_exhaustion"
            && (futures.FundingRate is null || futures.FundingRate.All(v => v is null)))
        {
            return ([Skip(spec, symbol, timeframe, neighborhood ? "IS_PARAM" : "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Funding missing. Not fabricated.")], []);
        }

        var barsPerDay = BarsPerDay(timeframe);
        var definition = StrategyValidation.Definition(spec.TemplateKey, timeframe, spec.Params with
        {
            TemplateKey = spec.TemplateKey,
            AllowedSide = StrategySides.Both,
            Timeframe = timeframe,
            FundingLookback = Math.Max(spec.Params.FundingLookback, barsPerDay * 3)
        });
        var warmup = StrategyValidation.WarmupBars(definition);
        if (closed.Count < warmup + StrategyValidation.MinimumEvaluatedBars)
        {
            return ([Skip(spec, symbol, timeframe, "IS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, $"{closed.Count} bars.")], []);
        }

        var cache = new CausalIndicatorCache(closed);
        var settlements = (fundingBySymbol.GetValueOrDefault(symbol) ?? [])
            .Select(f => new ReplayFundingSettlement(f.FundingTime, f.FundingRate))
            .OrderBy(f => f.FundingTime)
            .ToList();
        var books = new List<ResearchBookResult>();
        var trades = new List<Phase4TradeRow>();
        var windows = neighborhood
            ? new[] { ("IS_PARAM", 0, StrategyValidation.ChronologicalSplitIndices(closed.Count).InSampleEnd) }
            : BuildWindows(closed.Count, warmup);

        foreach (var cost in neighborhood ? new[] { ResearchCostLabels.Base } : WindowCosts(windows))
        {
            foreach (var (phase, from, to) in windows)
            {
                if (phase.StartsWith("WF", StringComparison.Ordinal) && cost != ResearchCostLabels.Base)
                {
                    continue;
                }

                if (phase.StartsWith("BLOCK", StringComparison.Ordinal) && cost != ResearchCostLabels.Base)
                {
                    continue;
                }

                if (to - Math.Max(from, warmup) < StrategyValidation.MinimumEvaluatedBars)
                {
                    books.Add(Skip(spec, symbol, timeframe, phase, cost, ResearchStatuses.InsufficientData, "window too short"));
                    continue;
                }

                var settings = ResearchRunner.CostScaledRisk(closed[Math.Max(from, 0)].OpenTime, closed[Math.Min(to, closed.Count) - 1].CloseTime, cost);
                var replay = new BacktestReplay(new StrategyEngine()).Run(
                    definition, closed, settings, cache, Math.Max(from, warmup), to, null, futures, settlements);
                var seed = new ResearchBookResult(
                    spec.CandidateId, symbol, timeframe, phase, cost, ResearchStatuses.Researching,
                    to - from, 0, 0, 0, 0, 0, "NO_TRADES",
                    null, 0, 0, 0, 0, 0,
                    null, null, null, null, null, null, null, null, null, null, null, null,
                    ResearchDiagnostics.ClassifyAtSignal(closed, Math.Min(closed.Count - 1, Math.Max(from, warmup))),
                    [
                        $"fundingPaid={replay.FundingPaid.ToString("0.00", CultureInfo.InvariantCulture)}",
                        $"slippage={replay.SlippagePaid.ToString("0.00", CultureInfo.InvariantCulture)}",
                        $"gross={replay.GrossPnl.ToString("0.00", CultureInfo.InvariantCulture)}",
                        replay.CostNotes,
                        $"identity={(BacktestReplay.AccountingIdentityHolds(replay) ? "OK" : "GAP")}",
                        $"days={replay.Trades.Select(t => t.OpenedAt.UtcDateTime.Date).Distinct().Count()}",
                        $"weeks={replay.Trades.Select(t => $"{System.Globalization.ISOWeek.GetYear(t.OpenedAt.UtcDateTime)}-{System.Globalization.ISOWeek.GetWeekOfYear(t.OpenedAt.UtcDateTime):00}").Distinct().Count()}",
                        $"long={replay.Long.Trades}",
                        $"short={replay.Short.Trades}",
                        $"dd={replay.MaximumDrawdown.ToString("0.00", CultureInfo.InvariantCulture)}"
                    ]);
                ResearchDiagnostics.Fill(seed, replay, closed, out var filled);
                var status = filled.TradeCount == 0
                    ? ResearchStatuses.NoTrades
                    : filled.TradeCount < MinimumBookTrades
                        ? ResearchStatuses.InsufficientData
                        : ResearchStatuses.Researching;
                books.Add(filled with { Status = status, Fees = replay.FeesPaid });
                foreach (var trade in replay.Trades)
                {
                    var idx = IndexAt(closed, trade.OpenedAt);
                    trades.Add(new Phase4TradeRow(
                        spec.CandidateId, symbol, timeframe, phase, cost, trade.OpenedAt, trade.Side, trade.PnL,
                        trade.Fees, trade.SlippageCost, trade.FundingPnl, trade.GrossPnl, MapRegime(ResearchDiagnostics.ClassifyAtSignal(closed, idx))));
                }
            }
        }

        return (books, trades);
    }

    private static IReadOnlyList<(string Phase, int From, int To)> BuildWindows(int count, int warmup)
    {
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(count);
        var rows = new List<(string, int, int)>
        {
            ("IS", 0, insEnd),
            ("VALIDATION", insEnd, valEnd),
            ("OOS", valEnd, count)
        };
        var usable = Math.Max(0, count - warmup);
        var block = Math.Max(1, usable / ChronologicalBlocks);
        for (var i = 0; i < ChronologicalBlocks; i++)
        {
            var from = warmup + i * block;
            var to = i == ChronologicalBlocks - 1 ? count : warmup + (i + 1) * block;
            rows.Add(($"BLOCK{i + 1}", from, to));
        }

        var train = Math.Min(Math.Max(warmup, count / 3), Math.Max(warmup, count / 2));
        var test = Math.Max(20, count / 10);
        var available = Math.Max(0, count - train - test);
        var step = available <= 0 ? test : Math.Max(test, available / 7);
        var wf = 0;
        foreach (var window in StrategyValidation.WalkForwardWindows(count, train, test, step).Take(8))
        {
            var testStart = window.Start + train;
            var testEnd = window.Start + window.Length;
            if (testStart >= warmup && testEnd <= count)
            {
                rows.Add(($"WF{wf}", testStart, testEnd));
                wf++;
            }
        }

        return rows;
    }

    private static IEnumerable<string> WindowCosts(IReadOnlyList<(string Phase, int From, int To)> windows)
    {
        _ = windows;
        return CostGrid;
    }

    private static Phase4Spec Spec(string family, string template, string role, string tag, bool neighborhood, StrategyTemplateParams p) =>
        new($"{family}|{role}|continuation", family, template, role, tag, neighborhood, $"{family}|baseline|continuation", p);

    private static string AssignGate(IReadOnlyList<ResearchBookResult> books, IReadOnlyList<Phase4TradeRow> trades, Phase4Spec spec)
    {
        var oos = Slice(books, spec.CandidateId, "OOS", ResearchCostLabels.Base);
        var totals = Combine(oos);
        var longT = ResearchDiagnostics.CombinePf(oos.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
        var shortT = ResearchDiagnostics.CombinePf(oos.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
        var high = Combine(Slice(books, spec.CandidateId, "OOS", ResearchCostLabels.High));
        var baseline = Combine(Slice(books, spec.BaselineId, "OOS", ResearchCostLabels.Base));
        var (state, pf) = ResearchPf.From(totals);
        var (_, highPf) = ResearchPf.From(high);
        var oosTrades = trades.Where(t => t.CandidateId == spec.CandidateId && t.Phase == "OOS" && t.CostLabel == ResearchCostLabels.Base).ToList();
        var days = oosTrades.Select(t => t.OpenedAt.UtcDateTime.Date).Distinct().Count();
        var symbolShare = SymbolConcentration(oos);
        if (totals.Trades < MinimumCombinedOosTrades)
        {
            return $"{ResearchStatuses.InsufficientData} n={totals.Trades}";
        }

        var fragile = pf is > 1m && (highPf is null || highPf < 1m);
        var oneSide = Math.Min(longT.Trades, shortT.Trades) < 10;
        var incremental = totals.Expectancy > baseline.Expectancy;
        var positive = totals.Expectancy > 0m && pf is > 1m;
        if (!positive)
        {
            return $"{ResearchStatuses.OosFailed} n={totals.Trades} PF {ResearchPf.Render(state, pf)} exp={N(totals.Expectancy)}";
        }

        if (fragile)
        {
            return $"{ResearchStatuses.CostFragile} n={totals.Trades}";
        }

        if (oneSide)
        {
            return $"RESEARCHING; one-side thin LONG={longT.Trades} SHORT={shortT.Trades}";
        }

        if (symbolShare >= 0.70m)
        {
            return $"{ResearchStatuses.SymbolFragile}; top-two |net| share={N(symbolShare * 100m)}%";
        }

        if (days < 20)
        {
            return $"RESEARCHING; time-concentrated uniqueDays≈{days}";
        }

        if (!incremental)
        {
            return "RESEARCHING; baseline not weaker on expectancy";
        }

        return $"{ResearchStatuses.Researching} n={totals.Trades} PF {ResearchPf.Render(state, pf)} (not profitable, not promoted)";
    }

    private static decimal SymbolConcentration(IReadOnlyList<ResearchBookResult> oos)
    {
        var nets = oos.GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => Math.Abs(g.Sum(x => x.NetPnl)))
            .OrderByDescending(x => x)
            .ToList();
        var sum = nets.Sum();
        return sum <= 0m ? 0m : nets.Take(2).Sum() / sum;
    }

    private static Phase4Bootstrap BootstrapOne(string id, List<decimal> pnls)
    {
        if (pnls.Count == 0)
        {
            return new Phase4Bootstrap(id, 0, BootstrapDraws, BootstrapSeed, 0, 0, 0, 0, 0, 0, 0, 0, 0, "NO_TRADES");
        }

        var rng = new Random(BootstrapSeed);
        var means = new decimal[BootstrapDraws];
        var win = new decimal[BootstrapDraws];
        var nets = new decimal[BootstrapDraws];
        var n = pnls.Count;
        for (var d = 0; d < BootstrapDraws; d++)
        {
            decimal sum = 0m;
            var wins = 0;
            for (var i = 0; i < n; i++)
            {
                var x = pnls[rng.Next(n)];
                sum += x;
                if (x > 0m)
                {
                    wins++;
                }
            }

            means[d] = sum / n;
            win[d] = (decimal)wins / n * 100m;
            nets[d] = sum;
        }

        Array.Sort(means);
        Array.Sort(win);
        Array.Sort(nets);
        var lo = (int)(0.025m * (BootstrapDraws - 1));
        var hi = (int)(0.975m * (BootstrapDraws - 1));
        var mean = pnls.Average();
        var wr = pnls.Count(x => x > 0m) / (decimal)n * 100m;
        var net = pnls.Sum();
        return new Phase4Bootstrap(id, n, BootstrapDraws, BootstrapSeed, mean, means[lo], means[hi], wr, win[lo], win[hi], net, nets[lo], nets[hi], "Deterministic seeded bootstrap of OOS BASE trades.");
    }

    private static void WriteSlice(StringBuilder sb, IReadOnlyList<ResearchBookResult> books, string id, string phase, string cost, string label)
    {
        var rows = Slice(books, id, phase, cost);
        WriteTotals(sb, $"{label} `{id}`", Combine(rows), rows);
    }

    private static void WriteTotals(StringBuilder sb, string label, PnlTotals totals, IReadOnlyList<ResearchBookResult>? rows = null)
    {
        var (state, pf) = ResearchPf.From(totals);
        var longT = rows is null ? PnlTotals.Empty : ResearchDiagnostics.CombinePf(rows.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
        var shortT = rows is null ? PnlTotals.Empty : ResearchDiagnostics.CombinePf(rows.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
        var dd = rows?.Select(NoteDecimal("dd=")).DefaultIfEmpty(0m).Max() ?? 0m;
        sb.AppendLine($"- {label}: n={totals.Trades} PF {ResearchPf.Render(state, pf)} exp={N(totals.Expectancy)} net={N(totals.NetPnl)} dd={N(dd)} LONG PF {Pf(longT)} SHORT PF {Pf(shortT)}");
    }

    private static void WriteSymbols(StringBuilder sb, IReadOnlyList<ResearchBookResult> books, string id)
    {
        var oos = Slice(books, id, "OOS", ResearchCostLabels.Base);
        var per = oos.GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Symbol: g.Key, Totals: Combine(g.ToList())))
            .Where(x => x.Totals.Trades > 0)
            .ToList();
        var finite = per.Select(x => x.Totals.ProfitFactor).Where(p => p.IsFinite).Select(p => p.Ratio).OrderBy(x => x).ToList();
        var above = per.Count(x => x.Totals.ProfitFactor.IsFinite && x.Totals.ProfitFactor.Ratio > 1m || x.Totals.ProfitFactor.Kind == ProfitFactorKind.NoLosses);
        var below = per.Count - above;
        sb.AppendLine($"- symbols with trades={per.Count} profitable={above} losing={below} medianPF={(finite.Count == 0 ? "n/a" : N(finite[finite.Count / 2]))} meanPF={(finite.Count == 0 ? "n/a" : N(finite.Average()))} best={(finite.Count == 0 ? "n/a" : N(finite[^1]))} worst={(finite.Count == 0 ? "n/a" : N(finite[0]))} topTwoShare={N(SymbolConcentration(oos) * 100m)}%");
        foreach (var row in per.OrderBy(x => x.Symbol))
        {
            var symbolRows = oos.Where(x => string.Equals(x.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase)).ToList();
            var longT = ResearchDiagnostics.CombinePf(symbolRows.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
            var shortT = ResearchDiagnostics.CombinePf(symbolRows.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
            sb.AppendLine($"  - {row.Symbol}: n={row.Totals.Trades} PF {Pf(row.Totals)} exp={N(row.Totals.Expectancy)} net={N(row.Totals.NetPnl)} LONG PF {Pf(longT)} SHORT PF {Pf(shortT)}");
        }
    }

    private static List<ResearchBookResult> Slice(IReadOnlyList<ResearchBookResult> books, string id, string phase, string cost) =>
        books.Where(b => b.CandidateId == id && b.Phase == phase && b.CostLabel == cost).ToList();

    private static PnlTotals Combine(IReadOnlyList<ResearchBookResult> rows) =>
        ResearchDiagnostics.CombinePf(rows.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());

    private static string Pf(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return $"{ResearchPf.Render(state, ratio)} n={totals.Trades}";
    }

    private static string Delta(decimal? a, decimal? b) =>
        a is null || b is null ? "n/a" : N(a.Value - b.Value);

    private static string N(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static Func<ResearchBookResult, decimal> NoteDecimal(string prefix) =>
        row =>
        {
            var note = row.Notes.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            return note is not null && decimal.TryParse(note[prefix.Length..], NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v
                : 0m;
        };

    private static string MapRegime(string raw) => raw switch
    {
        "HIGH_VOLATILITY" => "HIGH_VOL",
        "LOW_VOLATILITY" => "LOW_VOL",
        _ => raw
    };

    private static int BarsPerDay(string timeframe) => timeframe.Trim().ToLowerInvariant() switch
    {
        "5m" => 288,
        "15m" => 96,
        "1h" => 24,
        _ => 24
    };

    private static int IndexAt(IReadOnlyList<MarketCandle> candles, DateTimeOffset time)
    {
        for (var i = 0; i < candles.Count; i++)
        {
            if (candles[i].OpenTime >= time || candles[i].CloseTime >= time)
            {
                return i;
            }
        }

        return Math.Max(0, candles.Count - 1);
    }

    private static ReplayTrade AsReplayTrade(Phase4TradeRow t) =>
        new(t.OpenedAt, t.OpenedAt, 1m, 1m, 1m, t.PnL, t.Fees, "phase4", t.Side);

    private static ResearchBookResult Skip(
        Phase4Spec spec,
        string symbol,
        string timeframe,
        string phase,
        string cost,
        string status,
        string note) =>
        new(
            spec.CandidateId, symbol, timeframe, phase, cost, status,
            0, 0, 0, 0, 0, 0, "NO_TRADES",
            null, 0, 0, 0, 0, 0,
            null, null, null, null, null, null, null, null, null, null, null, null,
            "TRANSITION",
            [note]);
}
