using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed record FuturesAlphaSpec(
    string CandidateId,
    string FamilyId,
    string TemplateKey,
    string Role,
    string Hypothesis,
    bool RequiresOi,
    bool RequiresFunding,
    bool RequiresBasis,
    string BaselineId,
    StrategyTemplateParams Params);

public static class FuturesAlphaPilot
{
    public const int MinimumCombinedOosTrades = 50;
    public const int MinimumBookTrades = 20;
    public const int OiLookbackDays = 29;

    public static IReadOnlyList<FuturesAlphaSpec> Specs()
    {
        var rows = new List<FuturesAlphaSpec>();
        void Add(string family, string template, string role, string hyp, bool oi, bool fund, bool basis, string baseline, StrategyTemplateParams p)
        {
            var direction = hyp.Contains("contrarian", StringComparison.OrdinalIgnoreCase)
                || hyp.Contains("reversal", StringComparison.OrdinalIgnoreCase)
                ? "contrarian"
                : "continuation";
            rows.Add(new FuturesAlphaSpec(
                $"{family}|{role}|{hyp}",
                family,
                template,
                role,
                hyp,
                oi,
                fund,
                basis,
                baseline,
                p with
                {
                    TemplateKey = template,
                    AllowedSide = StrategySides.Both,
                    FundingHypothesis = direction,
                    OiHypothesis = direction,
                    UseFuturesFilter = role == "enhanced"
                }));
        }

        var c1 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingOiReversal, false) with
        {
            FundingExtremePercentile = 0.10m,
            OiExtremePercentile = 0.90m,
            PriceDisplacementAtr = 1.5m,
            EntryLookback = 20
        };
        Add("funding_oi_reversal", StrategyTemplateKeys.FundingOiReversal, "enhanced", "contrarian", true, true, false, "funding_oi_reversal|baseline|contrarian", c1);
        Add("funding_oi_reversal", StrategyTemplateKeys.FundingOiReversal, "enhanced", "continuation", true, true, false, "funding_oi_reversal|baseline|continuation", c1);
        Add("funding_oi_reversal", StrategyTemplateKeys.FundingOiReversal, "baseline", "contrarian", false, false, false, "funding_oi_reversal|baseline|contrarian", c1);
        Add("funding_oi_reversal", StrategyTemplateKeys.FundingOiReversal, "baseline", "continuation", false, false, false, "funding_oi_reversal|baseline|continuation", c1);
        Add("funding_oi_reversal", StrategyTemplateKeys.FundingOiReversal, "enhanced", "contrarian_p05", true, true, false, "funding_oi_reversal|baseline|contrarian",
            c1 with { FundingExtremePercentile = 0.05m, FundingHypothesis = "contrarian" });

        var c2 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingBasisRv, false) with { ZScoreEntry = 2m, FundingLookback = 24, EntryLookback = 40 };
        Add("funding_basis_rv", StrategyTemplateKeys.FundingBasisRv, "enhanced", "contrarian", false, true, true, "funding_basis_rv|baseline|contrarian", c2);
        Add("funding_basis_rv", StrategyTemplateKeys.FundingBasisRv, "enhanced", "continuation", false, true, true, "funding_basis_rv|baseline|continuation", c2);
        Add("funding_basis_rv", StrategyTemplateKeys.FundingBasisRv, "baseline", "contrarian", false, false, false, "funding_basis_rv|baseline|contrarian", c2);
        Add("funding_basis_rv", StrategyTemplateKeys.FundingBasisRv, "baseline", "continuation", false, false, false, "funding_basis_rv|baseline|continuation", c2);

        var c3 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingPriceMomentum, false) with { FundingExtremePercentile = 0.10m, EntryLookback = 20 };
        Add("funding_price_momentum", StrategyTemplateKeys.FundingPriceMomentum, "enhanced", "contrarian", false, true, false, "funding_price_momentum|baseline|contrarian", c3);
        Add("funding_price_momentum", StrategyTemplateKeys.FundingPriceMomentum, "enhanced", "continuation", false, true, false, "funding_price_momentum|baseline|continuation", c3);
        Add("funding_price_momentum", StrategyTemplateKeys.FundingPriceMomentum, "baseline", "contrarian", false, false, false, "funding_price_momentum|baseline|contrarian", c3);
        Add("funding_price_momentum", StrategyTemplateKeys.FundingPriceMomentum, "baseline", "continuation", false, false, false, "funding_price_momentum|baseline|continuation", c3);

        var c4 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.OiPriceVolumeRegime, false);
        Add("oi_price_volume_regime", StrategyTemplateKeys.OiPriceVolumeRegime, "enhanced", "contrarian", true, false, false, "oi_price_volume_regime|baseline|contrarian", c4);
        Add("oi_price_volume_regime", StrategyTemplateKeys.OiPriceVolumeRegime, "enhanced", "continuation", true, false, false, "oi_price_volume_regime|baseline|continuation", c4);
        Add("oi_price_volume_regime", StrategyTemplateKeys.OiPriceVolumeRegime, "baseline", "contrarian", false, false, false, "oi_price_volume_regime|baseline|contrarian", c4);
        Add("oi_price_volume_regime", StrategyTemplateKeys.OiPriceVolumeRegime, "baseline", "continuation", false, false, false, "oi_price_volume_regime|baseline|continuation", c4);

        var c5 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingExtremeMomentumExhaustion, false) with
        {
            FundingExtremePercentile = 0.10m,
            PriceDisplacementAtr = 1.5m
        };
        Add("funding_extreme_momentum_exhaustion", StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "enhanced", "contrarian", false, true, false, "funding_extreme_momentum_exhaustion|baseline|contrarian", c5);
        Add("funding_extreme_momentum_exhaustion", StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "enhanced", "continuation", false, true, false, "funding_extreme_momentum_exhaustion|baseline|continuation", c5);
        Add("funding_extreme_momentum_exhaustion", StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "baseline", "contrarian", false, false, false, "funding_extreme_momentum_exhaustion|baseline|contrarian", c5);
        Add("funding_extreme_momentum_exhaustion", StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "baseline", "continuation", false, false, false, "funding_extreme_momentum_exhaustion|baseline|continuation", c5);

        var c6 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.BasisMeanReversion, false) with { ZScoreEntry = 2m, EntryLookback = 40 };
        Add("basis_mean_reversion", StrategyTemplateKeys.BasisMeanReversion, "enhanced", "contrarian", false, false, true, "basis_mean_reversion|baseline|contrarian", c6);
        Add("basis_mean_reversion", StrategyTemplateKeys.BasisMeanReversion, "enhanced", "continuation", false, false, true, "basis_mean_reversion|baseline|continuation", c6);
        Add("basis_mean_reversion", StrategyTemplateKeys.BasisMeanReversion, "enhanced", "contrarian_z25", false, false, true, "basis_mean_reversion|baseline|contrarian", c6 with { ZScoreEntry = 2.5m });
        Add("basis_mean_reversion", StrategyTemplateKeys.BasisMeanReversion, "baseline", "contrarian", false, false, false, "basis_mean_reversion|baseline|contrarian", c6);
        Add("basis_mean_reversion", StrategyTemplateKeys.BasisMeanReversion, "baseline", "continuation", false, false, false, "basis_mean_reversion|baseline|continuation", c6);

        var c7 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.FundingBasisVwap, false) with
        {
            FundingExtremePercentile = 0.10m,
            ZScoreEntry = 2m,
            MaxVwapDistanceAtr = 1.5m
        };
        Add("funding_basis_vwap", StrategyTemplateKeys.FundingBasisVwap, "enhanced", "contrarian", false, true, true, "funding_basis_vwap|baseline|contrarian", c7);
        Add("funding_basis_vwap", StrategyTemplateKeys.FundingBasisVwap, "enhanced", "continuation", false, true, true, "funding_basis_vwap|baseline|continuation", c7);
        Add("funding_basis_vwap", StrategyTemplateKeys.FundingBasisVwap, "baseline", "contrarian", false, false, false, "funding_basis_vwap|baseline|contrarian", c7);
        Add("funding_basis_vwap", StrategyTemplateKeys.FundingBasisVwap, "baseline", "continuation", false, false, false, "funding_basis_vwap|baseline|continuation", c7);

        var c8 = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.OiBreakoutConfirmation, false) with
        {
            DonchianLength = 20,
            BreakoutRelativeVolume = 1.2m,
            OiChangeThreshold = 0.02m
        };
        Add("oi_breakout_confirmation", StrategyTemplateKeys.OiBreakoutConfirmation, "enhanced", "continuation", true, false, false, "oi_breakout_confirmation|baseline|continuation", c8);
        Add("oi_breakout_confirmation", StrategyTemplateKeys.OiBreakoutConfirmation, "enhanced", "contrarian", true, false, false, "oi_breakout_confirmation|baseline|contrarian", c8);
        Add("oi_breakout_confirmation", StrategyTemplateKeys.OiBreakoutConfirmation, "baseline", "continuation", false, false, false, "oi_breakout_confirmation|baseline|continuation", c8);
        Add("oi_breakout_confirmation", StrategyTemplateKeys.OiBreakoutConfirmation, "baseline", "contrarian", false, false, false, "oi_breakout_confirmation|baseline|contrarian", c8);

        Add("vp_vwap_reversion", StrategyTemplateKeys.VpVwapReversion, "enhanced", "default", false, false, false, "vp_vwap_reversion|enhanced|default",
            StrategyTemplates.DefaultsFor(StrategyTemplateKeys.VpVwapReversion, false));
        return rows;
    }

    public static IReadOnlyList<ResearchBookResult> Evaluate(
        IReadOnlyList<string> symbols,
        IReadOnlyList<string> timeframes,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), StrategyFuturesSeries> futures,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>> fundingBySymbol,
        DateTimeOffset windowEnd,
        int maxParallel = 2)
    {
        var specs = Specs();
        var jobs = specs.SelectMany(spec => symbols.SelectMany(symbol => timeframes.Select(tf => (spec, symbol, tf)))).ToArray();
        var rows = new List<ResearchBookResult>();
        var gate = new object();
        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallel) },
            job =>
            {
                var slice = EvaluateOne(job.spec, job.symbol, job.tf, series, futures, fundingBySymbol, windowEnd);
                lock (gate)
                {
                    rows.AddRange(slice);
                }
            });
        return rows.OrderBy(r => r.CandidateId).ThenBy(r => r.Symbol).ThenBy(r => r.Timeframe).ThenBy(r => r.Phase).ToList();
    }

    public static string RenderMarkdown(IReadOnlyList<ResearchBookResult> books, IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        var specs = Specs();
        sb.AppendLine("# Futures alpha Phase 3 report");
        sb.AppendLine();
        sb.AppendLine("Focused futures-specific pilot. LIVE = OFF. Risk Engine unchanged. Frozen five unchanged. No 528-universe run.");
        sb.AppendLine("No OOS parameter tuning. No VALIDATED_FOR_PAPER. No fabricated data. Taker flow was not used.");
        sb.AppendLine();
        sb.AppendLine("## 1. Data availability");
        sb.AppendLine("- AVAILABLE (90d, 10 coins, 5m/15m/1h): OHLCV, Funding (settled `fundingTime`), Mark, Index, Basis.");
        sb.AppendLine("- OI: ~29 days only. Results that use OI are labeled **OI_SAMPLE_LIMITED**. Not compared as equal-confidence 90-day books.");
        sb.AppendLine("- Taker flow: INSUFFICIENT_DATA. Not a Phase 3 candidate.");
        sb.AppendLine("- Liquidations, depth, causal pair universe: DATA_UNAVAILABLE.");
        sb.AppendLine();
        sb.AppendLine("## 2. Sample period");
        sb.AppendLine("- Funding / basis / mark / index / OHLCV candidates: requested ~90 days.");
        sb.AppendLine("- OI candidates: last 29 days of the same end date. Window was not silently treated as 90 days.");
        sb.AppendLine();
        sb.AppendLine("## 3. Candidate definitions");
        sb.AppendLine("- `funding_oi_reversal`: extreme funding + extreme OI + ATR displacement + exhaustion + reversal candle.");
        sb.AppendLine("- `funding_basis_rv`: co-extreme funding z and basis z. Directional only; market-neutral pairs not testable on a single book.");
        sb.AppendLine("- `funding_price_momentum`: EMA trend + volume + price momentum + funding extreme.");
        sb.AppendLine("- `oi_price_volume_regime`: expansion-state continuation vs contrarian vs price/volume baseline. Eight-state table is not a cost-inclusive trade book; empty cells = INSUFFICIENT_DATA.");
        sb.AppendLine("- `funding_extreme_momentum_exhaustion`: funding tail + weakening momentum + reversal candle (no OI).");
        sb.AppendLine("- `basis_mean_reversion`: normalized-basis z at 2.0 and 2.5, reversion vs continuation separately.");
        sb.AppendLine("- `funding_basis_vwap`: funding tail + basis z + VWAP deviation.");
        sb.AppendLine("- `oi_breakout_confirmation`: Donchian + volume, with vs without OI expansion.");
        sb.AppendLine("- `vp_vwap_reversion`: secondary check with frozen/default parameters. Not retuned on Phase 1 OOS.");
        sb.AppendLine("- Taker flow was not run (INSUFFICIENT_DATA).");
        sb.AppendLine();
        sb.AppendLine("## 4. Formulas");
        sb.AppendLine("- Continuation vs contrarian are **independent** hypotheses. Positive funding is not hard-coded SHORT.");
        sb.AppendLine("- Funding z/percentile/cumulative use settled rates with `fundingTime <= CloseTime`. Lookback is at least 3 calendar days of bars per timeframe (not OOS-tuned).");
        sb.AppendLine("- Basis = MarkClose − IndexClose; NormalizedBasis = Basis / IndexClose; matching closeTime only. No large-gap fill.");
        sb.AppendLine("- PF = Σ winning net / |Σ losing net|. Empty = NO_TRADES. No PF=99. Combined PF is summed PnL, never an average of PFs.");
        sb.AppendLine();
        sb.AppendLine("## 5. Baselines");
        sb.AppendLine("Each futures-enhanced candidate is compared to the same price/volume rules with `UseFuturesFilter=false` (no Funding/OI/Basis required). Incremental value is enhanced − baseline, not the raw enhanced PF.");
        sb.AppendLine();
        sb.AppendLine("## 6–7. Enhanced strategies and OOS BASE Combined");
        foreach (var family in specs.Select(s => s.FamilyId).Distinct())
        {
            sb.AppendLine($"### {family}");
            var familySpecs = specs.Where(s => s.FamilyId == family).ToList();
            foreach (var spec in familySpecs)
            {
                var oos = books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
                var totals = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
                var (state, pf) = ResearchPf.From(totals);
                var longT = ResearchDiagnostics.CombinePf(oos.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
                var shortT = ResearchDiagnostics.CombinePf(oos.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
                var days = UniqueDays(oos);
                var weeks = UniqueWeeks(oos);
                var symbols = oos.Select(x => x.Symbol).Distinct().Count();
                var status = CatalogStatus(spec, oos, totals);
                var oi = spec.RequiresOi ? " OI_SAMPLE_LIMITED" : "";
                sb.AppendLine($"- `{spec.CandidateId}` role={spec.Role} hyp={spec.Hypothesis}: {status}{oi}; n={totals.Trades} PF {ResearchPf.Render(state, pf)}; exp={totals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)}; LONG PF {Pf(longT)}; SHORT PF {Pf(shortT)}; symbols={symbols} days={days} weeks={weeks}; fees={totals.Fees.ToString("0.00", CultureInfo.InvariantCulture)}");
            }

            sb.AppendLine();
            sb.AppendLine("Incremental (enhanced − baseline) OOS BASE Combined:");
            foreach (var enhanced in familySpecs.Where(s => s.Role == "enhanced"))
            {
                var baseSpec = familySpecs.FirstOrDefault(s => s.CandidateId == enhanced.BaselineId)
                    ?? familySpecs.FirstOrDefault(s => s.Role == "baseline" && s.Hypothesis == enhanced.Hypothesis);
                if (baseSpec is null || baseSpec.CandidateId == enhanced.CandidateId)
                {
                    continue;
                }

                var a = Combine(books, enhanced.CandidateId);
                var b = Combine(books, baseSpec.CandidateId);
                sb.AppendLine($"- `{enhanced.CandidateId}` vs `{baseSpec.CandidateId}`: ΔPF={Delta(a.Pf, b.Pf)} ΔExp={((a.Exp - b.Exp).ToString("0.00", CultureInfo.InvariantCulture))} ΔWR={((a.Wr - b.Wr).ToString("0.00", CultureInfo.InvariantCulture))} ΔNet={((a.Net - b.Net).ToString("0.00", CultureInfo.InvariantCulture))} ΔDD={((a.Dd - b.Dd).ToString("0.00", CultureInfo.InvariantCulture))}");
            }

            sb.AppendLine();
        }

        sb.AppendLine("## 8. Cost stress (OOS Combined)");
        sb.AppendLine("BASE = frozen Model B fees/slippage. HIGH = 1.5x. STRESS = 2x. A book that is PF>1 at BASE and PF<1 at HIGH is **COST_FRAGILE**.");
        foreach (var spec in specs.Where(s => s.Role == "enhanced"))
        {
            sb.Append($"- `{spec.CandidateId}`");
            var fragile = false;
            decimal? basePf = null;
            foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.High, ResearchCostLabels.Stress })
            {
                var totals = ResearchDiagnostics.CombinePf(
                    books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == cost)
                        .Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
                var (state, pf) = ResearchPf.From(totals);
                if (cost == ResearchCostLabels.Base)
                {
                    basePf = pf;
                }

                if (cost == ResearchCostLabels.High && basePf is > 1m && (pf is null || pf < 1m))
                {
                    fragile = true;
                }

                sb.Append($" | {cost} n={totals.Trades} PF {ResearchPf.Render(state, pf)}");
            }

            sb.AppendLine(fragile ? " **COST_FRAGILE**" : "");
        }

        sb.AppendLine();
        sb.AppendLine("## 9. Sample size");
        sb.AppendLine($"Combined OOS trade count below {MinimumCombinedOosTrades} is INSUFFICIENT_DATA. Per-book n < {MinimumBookTrades} is not treated as evidence. 10–30 trades are not accepted.");
        foreach (var spec in specs)
        {
            var oos = books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var totals = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var traded = oos.Where(x => x.TradeCount > 0).ToList();
            sb.AppendLine($"- `{spec.CandidateId}`: n={totals.Trades} symbols={traded.Select(x => x.Symbol).Distinct().Count()} days={UniqueDays(oos)} weeks={UniqueWeeks(oos)} LONG={NoteSum(oos, "long=")} SHORT={NoteSum(oos, "short=")}{(spec.RequiresOi ? " OI_SAMPLE_LIMITED" : "")}");
        }

        sb.AppendLine();
        sb.AppendLine("## 10. LONG / SHORT");
        sb.AppendLine("Side PFs are summed from side totals. A candidate dominated by one side is not interesting for Phase 4.");
        foreach (var spec in specs.Where(s => s.Role == "enhanced"))
        {
            var oos = books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var longT = ResearchDiagnostics.CombinePf(oos.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
            var shortT = ResearchDiagnostics.CombinePf(oos.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
            sb.AppendLine($"- `{spec.CandidateId}` LONG n={longT.Trades} PF {Pf(longT)} exp={longT.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)}; SHORT n={shortT.Trades} PF {Pf(shortT)} exp={shortT.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Funding impact");
        sb.AppendLine("Replay applies settled funding to open Isolated notional when `fundingTime` falls in `(prevClose, close]` and `fundingTime >= fillTime`. CostNotes=INCLUDING_FUNDING. Baseline price-only books still include funding cashflows while a position is open (same Isolated carrying cost).");
        var fundingPaid = books.Where(b => b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).Sum(NoteDecimal("fundingPaid="));
        sb.AppendLine($"- Combined OOS BASE fundingPaid (sum of book notes) = {fundingPaid.ToString("0.00", CultureInfo.InvariantCulture)} (positive = paid).");
        sb.AppendLine();
        sb.AppendLine("## 12. OI limitations");
        sb.AppendLine("Public Binance `openInterestHist` ≈ 29 days. OI families are **OI_SAMPLE_LIMITED**. They are not 90-day evidence. A candidate that would require 90 days of OI is DATA_UNAVAILABLE. OI was not extrapolated.");
        sb.AppendLine();
        sb.AppendLine("## 13. Basis results");
        foreach (var spec in specs.Where(s => s.FamilyId is "funding_basis_rv" or "basis_mean_reversion" or "funding_basis_vwap"))
        {
            var oos = books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var totals = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (state, pf) = ResearchPf.From(totals);
            sb.AppendLine($"- `{spec.CandidateId}`: {CatalogStatus(spec, oos, totals)}; n={totals.Trades} PF {ResearchPf.Render(state, pf)} exp={totals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 14. Incremental information");
        sb.AppendLine("Futures features are useful only if enhanced − baseline is robust after costs. See Δ lines under each family above.");
        sb.AppendLine();
        sb.AppendLine("## 15. Failure modes");
        sb.AppendLine("- Look-ahead: features use index i only; fundingTime must be ≤ CloseTime; OI/basis nulls stay null.");
        sb.AppendLine("- Sparse unique funding prints on 5m (aligned step function). 3-day bar lookback is the frozen default.");
        sb.AppendLine("- OI 29d vs 90d OHLCV: do not rank OI books against 90-day books.");
        sb.AppendLine("- One-symbol or one-side domination, tiny OOS windows, and COST_FRAGILE at 1.5x.");
        sb.AppendLine("- Market-neutral C2 pairs were not fabricated.");
        sb.AppendLine();
        sb.AppendLine("## 16–18. Phase 4 gate");
        sb.AppendLine("VALIDATED_FOR_PAPER is not assigned in this phase. Interesting only if: n≥50 combined OOS, positive expectancy, cost-inclusive, not 1.5x-fragile, both sides not empty, not one-symbol dominated, incremental Δ vs baseline.");
        var interesting = new List<string>();
        var rejected = new List<string>();
        var moreData = new List<string>();
        foreach (var spec in specs.Where(s => s.Role == "enhanced"))
        {
            var oos = books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
            var totals = ResearchDiagnostics.CombinePf(oos.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var status = CatalogStatus(spec, oos, totals);
            var longT = ResearchDiagnostics.CombinePf(oos.Select(s => s.LongTotals).Where(t => t is not null).Cast<PnlTotals>());
            var shortT = ResearchDiagnostics.CombinePf(oos.Select(s => s.ShortTotals).Where(t => t is not null).Cast<PnlTotals>());
            var high = ResearchDiagnostics.CombinePf(
                books.Where(b => b.CandidateId == spec.CandidateId && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.High)
                    .Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
            var (_, basePf) = ResearchPf.From(totals);
            var (_, highPf) = ResearchPf.From(high);
            var fragile = basePf is > 1m && (highPf is null || highPf < 1m);
            var oneSide = Math.Min(longT.Trades, shortT.Trades) < 10;
            if (spec.RequiresOi)
            {
                moreData.Add($"{spec.CandidateId} ({status}, OI_SAMPLE_LIMITED)");
            }
            else if (status is ResearchStatuses.InsufficientData or ResearchStatuses.NoTrades or ResearchStatuses.DataUnavailable)
            {
                moreData.Add($"{spec.CandidateId} ({status})");
            }
            else if (fragile)
            {
                rejected.Add($"{spec.CandidateId} (COST_FRAGILE, n={totals.Trades})");
            }
            else if (oneSide)
            {
                rejected.Add($"{spec.CandidateId} (one-side dominated LONG={longT.Trades} SHORT={shortT.Trades})");
            }
            else if (totals.Expectancy > 0m && totals.Trades >= MinimumCombinedOosTrades)
            {
                interesting.Add($"{spec.CandidateId} ({status}, n={totals.Trades})");
            }
            else
            {
                rejected.Add($"{spec.CandidateId} ({status}, n={totals.Trades}, exp={totals.Expectancy.ToString("0.00", CultureInfo.InvariantCulture)})");
            }
        }

        sb.AppendLine("### 16. Worth Phase 4 research (not promotion)");
        if (interesting.Count == 0)
        {
            sb.AppendLine("- None met the Phase 3 interesting bar.");
        }
        else
        {
            foreach (var line in interesting)
            {
                sb.AppendLine($"- {line}");
            }
        }

        sb.AppendLine("### 17. Rejected for Phase 4");
        foreach (var line in rejected)
        {
            sb.AppendLine($"- {line}");
        }

        sb.AppendLine("### 18. Requiring more data");
        foreach (var line in moreData)
        {
            sb.AppendLine($"- {line}");
        }

        sb.AppendLine();
        sb.AppendLine("## LIVE / Risk");
        sb.AppendLine("- LIVE remains OFF.");
        sb.AppendLine("- Risk Engine was not modified.");
        sb.AppendLine();
        sb.AppendLine("## Run notes");
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        return sb.ToString();
    }

    public static void WriteJson(string path, IReadOnlyList<ResearchBookResult> books)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(books, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static List<ResearchBookResult> EvaluateOne(
        FuturesAlphaSpec spec,
        string symbol,
        string timeframe,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        IReadOnlyDictionary<(string Symbol, string Timeframe), StrategyFuturesSeries> futuresMap,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>> fundingBySymbol,
        DateTimeOffset windowEnd)
    {
        if (!series.TryGetValue((symbol, timeframe), out var candles) || candles.Count == 0)
        {
            return [Skip(spec, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "No candles.")];
        }

        var closed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
        var notes = new List<string>();
        if (spec.RequiresOi)
        {
            var oiStart = windowEnd.AddDays(-OiLookbackDays);
            closed = closed.Where(c => c.CloseTime >= oiStart).ToList();
            notes.Add("OI_SAMPLE_LIMITED");
            notes.Add($"OI window clamped to {OiLookbackDays}d ending {windowEnd:yyyy-MM-dd}.");
        }

        futuresMap.TryGetValue((symbol, timeframe), out var futures);
        futures ??= new StrategyFuturesSeries();
        if (spec.RequiresOi && spec.Role == "enhanced" && (futures.OpenInterest is null || futures.OpenInterest.All(v => v is null)))
        {
            return [Skip(spec, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "OI_HISTORICAL_DATA_LIMITATION. Not fabricated.")];
        }

        if (spec.RequiresFunding && spec.Role == "enhanced" && (futures.FundingRate is null || futures.FundingRate.All(v => v is null)))
        {
            return [Skip(spec, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Funding missing. Not fabricated.")];
        }

        if (spec.RequiresBasis && spec.Role == "enhanced" && (futures.NormalizedBasis is null || futures.NormalizedBasis.All(v => v is null)))
        {
            return [Skip(spec, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.DataUnavailable, "Basis missing. Not fabricated.")];
        }

        var barsPerDay = BarsPerDay(timeframe);
        var definition = StrategyValidation.Definition(spec.TemplateKey, timeframe, spec.Params with
        {
            TemplateKey = spec.TemplateKey,
            AllowedSide = StrategySides.Both,
            Timeframe = timeframe,
            FundingLookback = spec.RequiresFunding || spec.Role == "enhanced"
                ? Math.Max(spec.Params.FundingLookback, barsPerDay * 3)
                : spec.Params.FundingLookback,
            OiLookback = spec.RequiresOi
                ? Math.Max(spec.Params.OiLookback, barsPerDay)
                : spec.Params.OiLookback
        });
        var warmup = StrategyValidation.WarmupBars(definition);
        if (closed.Count < warmup + StrategyValidation.MinimumEvaluatedBars)
        {
            return [Skip(spec, symbol, timeframe, "IS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, $"{closed.Count} bars after window clamp.")];
        }

        var cache = new CausalIndicatorCache(closed);
        var aligned = futures;
        if (closed.Count != candles.Count)
        {
            aligned = new StrategyFuturesSeries
            {
                OpenInterest = SliceAlign(futures.OpenInterest, candles.Count, closed, candles),
                FundingRate = SliceAlign(futures.FundingRate, candles.Count, closed, candles),
                MarkPrice = SliceAlign(futures.MarkPrice, candles.Count, closed, candles),
                IndexPrice = SliceAlign(futures.IndexPrice, candles.Count, closed, candles),
                NormalizedBasis = SliceAlign(futures.NormalizedBasis, candles.Count, closed, candles)
            };
        }

        var settlements = (fundingBySymbol.GetValueOrDefault(symbol) ?? [])
            .Select(f => new ReplayFundingSettlement(f.FundingTime, f.FundingRate))
            .OrderBy(f => f.FundingTime)
            .ToList();
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(closed.Count);
        var rows = new List<ResearchBookResult>();
        foreach (var cost in new[] { ResearchCostLabels.Base, ResearchCostLabels.High, ResearchCostLabels.Stress })
        {
            foreach (var (phase, from, to) in new (string Phase, int From, int To)[]
                     {
                         ("IS", 0, insEnd),
                         ("VALIDATION", insEnd, valEnd),
                         ("OOS", valEnd, closed.Count)
                     })
            {
                if (to - Math.Max(from, warmup) < StrategyValidation.MinimumEvaluatedBars)
                {
                    rows.Add(Skip(spec, symbol, timeframe, phase, cost, ResearchStatuses.InsufficientData, "window too short"));
                    continue;
                }

                var settings = ResearchRunner.CostScaledRisk(closed[Math.Max(from, 0)].OpenTime, closed[Math.Min(to, closed.Count) - 1].CloseTime, cost);
                var replay = new BacktestReplay(new StrategyEngine()).Run(
                    definition, closed, settings, cache, Math.Max(from, warmup), to, null, aligned, settlements);
                var seed = new ResearchBookResult(
                    spec.CandidateId, symbol, timeframe, phase, cost, ResearchStatuses.Researching,
                    to - from, 0, 0, 0, 0, 0, "NO_TRADES",
                    null, 0, 0, 0, 0, 0,
                    null, null, null, null, null, null, null, null, null, null, null, null,
                    ResearchDiagnostics.ClassifyAtSignal(closed, Math.Min(closed.Count - 1, Math.Max(from, warmup))),
                    notes.Concat([
                        $"fundingPaid={replay.FundingPaid.ToString("0.00", CultureInfo.InvariantCulture)}",
                        replay.CostNotes,
                        $"days={UniqueDays(replay.Trades)}",
                        $"weeks={UniqueWeeks(replay.Trades)}",
                        $"long={replay.Long.Trades}",
                        $"short={replay.Short.Trades}",
                        $"dd={replay.MaximumDrawdown.ToString("0.00", CultureInfo.InvariantCulture)}"
                    ]).ToList());
                ResearchDiagnostics.Fill(seed, replay, closed, out var filled);
                var status = Assign(filled, phase, spec);
                rows.Add(filled with { Status = status, Fees = replay.FeesPaid });
            }
        }

        return rows;
    }

    private static IReadOnlyList<decimal?>? SliceAlign(
        IReadOnlyList<decimal?>? full,
        int originalCount,
        IReadOnlyList<MarketCandle> sliced,
        IReadOnlyList<MarketCandle> original)
    {
        if (full is null)
        {
            return null;
        }

        if (sliced.Count == original.Count || full.Count != originalCount)
        {
            return full.Count == sliced.Count ? full : null;
        }

        var map = new Dictionary<DateTimeOffset, int>();
        for (var i = 0; i < original.Count; i++)
        {
            map[original[i].CloseTime] = i;
        }

        var copy = new decimal?[sliced.Count];
        for (var i = 0; i < sliced.Count; i++)
        {
            if (map.TryGetValue(sliced[i].CloseTime, out var src) && src < full.Count)
            {
                copy[i] = full[src];
            }
        }

        return copy;
    }

    private static string Assign(ResearchBookResult row, string phase, FuturesAlphaSpec spec)
    {
        if (row.TradeCount == 0)
        {
            return ResearchStatuses.NoTrades;
        }

        if (row.TradeCount < MinimumBookTrades)
        {
            return ResearchStatuses.InsufficientData;
        }

        if (string.Equals(phase, "OOS", StringComparison.OrdinalIgnoreCase)
            && row.ProfitFactorState == "NORMAL"
            && row.ProfitFactor is { } pf
            && pf < 1m)
        {
            return ResearchStatuses.OosFailed;
        }

        return spec.RequiresOi ? ResearchStatuses.Researching : ResearchStatuses.Researching;
    }

    private static string CatalogStatus(FuturesAlphaSpec spec, List<ResearchBookResult> oos, PnlTotals totals)
    {
        if (oos.Count == 0)
        {
            return ResearchStatuses.InsufficientData;
        }

        if (oos.All(x => x.Status == ResearchStatuses.DataUnavailable))
        {
            return ResearchStatuses.DataUnavailable;
        }

        if (totals.Trades < MinimumCombinedOosTrades)
        {
            return ResearchStatuses.InsufficientData;
        }

        if (totals.Expectancy <= 0m)
        {
            return ResearchStatuses.OosFailed;
        }

        return ResearchStatuses.Researching;
    }

    private static (decimal? Pf, decimal Exp, decimal Wr, decimal Net, decimal Dd) Combine(IReadOnlyList<ResearchBookResult> books, string id)
    {
        var slice = books.Where(b => b.CandidateId == id && b.Phase == "OOS" && b.CostLabel == ResearchCostLabels.Base).ToList();
        var totals = ResearchDiagnostics.CombinePf(
            slice.Select(s => s.CombinedTotals).Where(t => t is not null).Cast<PnlTotals>());
        var (state, pf) = ResearchPf.From(totals);
        _ = state;
        var dd = slice.Select(NoteDecimal("dd=")).DefaultIfEmpty(0m).Max();
        return (pf, totals.Expectancy, totals.WinRate, totals.NetPnl, dd);
    }

    private static Func<ResearchBookResult, decimal> NoteDecimal(string prefix) =>
        row =>
        {
            var note = row.Notes.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            return note is not null && decimal.TryParse(note[prefix.Length..], NumberStyles.Any, CultureInfo.InvariantCulture, out var v)
                ? v
                : 0m;
        };

    private static int NoteSum(IEnumerable<ResearchBookResult> rows, string prefix)
    {
        var n = 0;
        foreach (var row in rows)
        {
            var note = row.Notes.FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            if (note is not null && int.TryParse(note[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                n += v;
            }
        }

        return n;
    }

    private static int BarsPerDay(string timeframe) => timeframe.Trim().ToLowerInvariant() switch
    {
        "5m" => 288,
        "15m" => 96,
        "1h" => 24,
        _ => 24
    };

    private static string Delta(decimal? a, decimal? b)
    {
        if (a is null || b is null)
        {
            return "n/a";
        }

        return (a.Value - b.Value).ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static string Pf(PnlTotals totals)
    {
        var (state, ratio) = ResearchPf.From(totals);
        return $"{ResearchPf.Render(state, ratio)} n={totals.Trades}";
    }

    private static int UniqueDays(IEnumerable<ResearchBookResult> oos)
    {
        var n = 0;
        foreach (var row in oos)
        {
            var note = row.Notes.FirstOrDefault(x => x.StartsWith("days=", StringComparison.Ordinal));
            if (note is not null && int.TryParse(note[5..], out var d))
            {
                n = Math.Max(n, d);
            }
        }

        return n;
    }

    private static int UniqueWeeks(IEnumerable<ResearchBookResult> oos)
    {
        var n = 0;
        foreach (var row in oos)
        {
            var note = row.Notes.FirstOrDefault(x => x.StartsWith("weeks=", StringComparison.Ordinal));
            if (note is not null && int.TryParse(note[6..], out var d))
            {
                n = Math.Max(n, d);
            }
        }

        return n;
    }

    private static int UniqueDays(IReadOnlyList<ReplayTrade> trades) =>
        trades.Select(t => t.OpenedAt.UtcDateTime.Date).Distinct().Count();

    private static int UniqueWeeks(IReadOnlyList<ReplayTrade> trades) =>
        trades.Select(t => ISOWeek(t.OpenedAt)).Distinct().Count();

    private static string ISOWeek(DateTimeOffset t) =>
        $"{ISOWeekYear(t)}-{System.Globalization.ISOWeek.GetWeekOfYear(t.UtcDateTime):00}";

    private static int ISOWeekYear(DateTimeOffset t) => System.Globalization.ISOWeek.GetYear(t.UtcDateTime);

    private static ResearchBookResult Skip(
        FuturesAlphaSpec spec,
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
            spec.RequiresOi ? ["OI_SAMPLE_LIMITED", note] : [note]);
}
