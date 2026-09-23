using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed class ResearchRunRequest
{
    public required string Phase { get; init; }
    public required IReadOnlyList<ResearchCandidate> Candidates { get; init; }
    public required IReadOnlyList<string> Symbols { get; init; }
    public required IReadOnlyList<string> Timeframes { get; init; }
    public required IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> Series { get; init; }
    public IReadOnlyList<string> CostLabels { get; init; } = [ResearchCostLabels.Base, ResearchCostLabels.High, ResearchCostLabels.Stress];
    public HashSet<string> Done { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Force { get; init; }
    public int MaxParallel { get; init; } = 1;
    public ResearchDataSnapshot DataSnapshot { get; init; } = new();
    public bool UseLowIsolated { get; init; }
    public bool HonorSuggestedStops { get; init; }
    public bool SkipWalkForward { get; init; }
    public bool IncludeChronologicalBlocks { get; init; }
    public bool IncludeWalkForward { get; init; }
    public IReadOnlyDictionary<(string Symbol, string Timeframe), CausalIndicatorCache>? IndicatorCaches { get; init; }
    public IReadOnlyDictionary<(string CandidateId, string Symbol, string Timeframe), IReadOnlyList<SignalType>>? PrecomputedSignals { get; init; }
}

public static class ResearchRunner
{
    public const int MinimumRegimeTrades = 20;

    public static IReadOnlyList<ResearchBookResult> Evaluate(ResearchRunRequest request)
    {
        var jobs = request.Candidates
            .SelectMany(candidate => request.Symbols.SelectMany(symbol => request.Timeframes.Select(timeframe => (candidate, symbol, timeframe))))
            .ToArray();
        var rows = new List<ResearchBookResult>();
        var gate = new object();
        var parallel = Math.Max(1, request.MaxParallel);
        Parallel.ForEach(
            jobs,
            new ParallelOptions { MaxDegreeOfParallelism = parallel },
            job =>
            {
                var slice = EvaluateJob(request, job.candidate, job.symbol, job.timeframe);
                lock (gate)
                {
                    rows.AddRange(slice);
                }
            });
        return rows;
    }

    private static List<ResearchBookResult> EvaluateJob(
        ResearchRunRequest request,
        ResearchCandidate candidate,
        string symbol,
        string timeframe)
    {
        var rows = new List<ResearchBookResult>();
        try
        {
            return EvaluateJobCore(request, candidate, symbol, timeframe, rows);
        }
        catch (Exception ex)
        {
            rows.Add(Skip(
                candidate,
                symbol,
                timeframe,
                "OOS",
                ResearchCostLabels.Base,
                ResearchStatuses.ImplementationError,
                ex.Message));
            return rows;
        }
    }

    private static List<ResearchBookResult> EvaluateJobCore(
        ResearchRunRequest request,
        ResearchCandidate candidate,
        string symbol,
        string timeframe,
        List<ResearchBookResult> rows)
    {
        if (!candidate.SupportedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(Skip(candidate, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.SkippedTimeframe, "Candidate does not support this timeframe."));
            return rows;
        }

        if (!string.IsNullOrWhiteSpace(candidate.FrozenSymbol)
            && !string.Equals(candidate.FrozenSymbol, symbol, StringComparison.OrdinalIgnoreCase))
        {
            rows.Add(Skip(
                candidate,
                symbol,
                timeframe,
                "OOS",
                ResearchCostLabels.Base,
                ResearchStatuses.Researching,
                $"Frozen to {candidate.FrozenSymbol} only; skipped {symbol}."));
            return rows;
        }

        var templateKey = string.IsNullOrWhiteSpace(candidate.ParentTemplateKey)
            ? candidate.NativeKey
            : candidate.ParentTemplateKey;
        var dataStatus = ResearchDataCatalog.SkipStatus(templateKey, request.DataSnapshot);
        if (dataStatus is ResearchStatuses.DataUnavailable or ResearchStatuses.InsufficientData)
        {
            rows.Add(Skip(
                candidate,
                symbol,
                timeframe,
                "OOS",
                ResearchCostLabels.Base,
                dataStatus,
                $"RequiredData [{string.Join(", ", ResearchDataCatalog.Required(templateKey))}] {dataStatus}. Not fabricated."));
            return rows;
        }

        if (!request.Series.TryGetValue((symbol, timeframe), out var candles) || candles.Count == 0)
        {
            rows.Add(Skip(candidate, symbol, timeframe, "OOS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "No candles."));
            return rows;
        }

        var closed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
        var definition = DefinitionFor(candidate, timeframe);
        var warmup = StrategyValidation.WarmupBars(definition);
        if (closed.Count < warmup + StrategyValidation.MinimumEvaluatedBars)
        {
            rows.Add(Skip(candidate, symbol, timeframe, "IS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, $"{closed.Count} bars, warmup {warmup}."));
            return rows;
        }

        var cache = request.IndicatorCaches?.GetValueOrDefault((symbol, timeframe))
            ?? new CausalIndicatorCache(closed);
        CausalIndicatorCache? htf = null;
        var htfName = ResolveHigherTimeframe(candidate, timeframe);
        if (!string.IsNullOrWhiteSpace(htfName) && request.Series.TryGetValue((symbol, htfName), out var htfBars))
        {
            htf = request.IndicatorCaches?.GetValueOrDefault((symbol, htfName))
                ?? new CausalIndicatorCache(htfBars.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
        }

        CausalIndicatorCache? confirmation = null;
        CausalIndicatorCache? contextCache = null;
        if (!string.IsNullOrWhiteSpace(candidate.Filters.ConfirmationTimeframe))
        {
            if (!request.Series.TryGetValue((symbol, candidate.Filters.ConfirmationTimeframe), out var confirmationBars))
            {
                rows.Add(Skip(candidate, symbol, timeframe, "IS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "MTF confirmation series unavailable."));
                return rows;
            }

            confirmation = request.IndicatorCaches?.GetValueOrDefault((symbol, candidate.Filters.ConfirmationTimeframe))
                ?? new CausalIndicatorCache(confirmationBars.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
        }

        if (!string.IsNullOrWhiteSpace(candidate.Filters.ContextTimeframe))
        {
            if (!request.Series.TryGetValue((symbol, candidate.Filters.ContextTimeframe), out var contextBars))
            {
                rows.Add(Skip(candidate, symbol, timeframe, "IS", ResearchCostLabels.Base, ResearchStatuses.InsufficientData, "MTF context series unavailable."));
                return rows;
            }

            contextCache = request.IndicatorCaches?.GetValueOrDefault((symbol, candidate.Filters.ContextTimeframe))
                ?? new CausalIndicatorCache(contextBars.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
        }

        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(closed.Count);
        foreach (var cost in request.CostLabels)
        {
            foreach (var (phase, from, to) in Windows(request.Phase, closed.Count, warmup, insEnd, valEnd, request.IncludeChronologicalBlocks))
            {
                var key = $"{candidate.CandidateId}|{symbol}|{timeframe}|{phase}|{cost}";
                if (!request.Force && request.Done.Contains(key))
                {
                    continue;
                }

                if (to - Math.Max(from, warmup) < StrategyValidation.MinimumEvaluatedBars)
                {
                    rows.Add(Skip(candidate, symbol, timeframe, phase, cost, ResearchStatuses.InsufficientData, $"window too short ({Math.Max(0, to - Math.Max(from, warmup))} bars)."));
                    continue;
                }

                var engine = ResolveEngine(request, candidate, symbol, timeframe, confirmation, contextCache);
                var settings = CostScaledRisk(request, closed[Math.Max(from, 0)].OpenTime, closed[Math.Min(to, closed.Count) - 1].CloseTime, cost, candidate);
                if (StrategyTemplateKeys.IsScalping(templateKey) && settings.MaxHoldBars <= 0)
                {
                    settings = settings with { MaxHoldBars = ScalpingCatalog.MaxHoldBars(timeframe) };
                }

                var signalFrom = Math.Max(from, warmup);
                var replay = new BacktestReplay(engine).Run(definition, closed, settings, cache, signalFrom, to, htf);
                var seed = new ResearchBookResult(
                    candidate.CandidateId,
                    symbol,
                    timeframe,
                    phase,
                    cost,
                    ResearchStatuses.Researching,
                    to - from,
                    0, 0, 0, 0, 0,
                    "NO_TRADES",
                    null, 0, 0, 0, 0, 0,
                    null, null, null, null, null, null, null, null, null, null, null, null,
                    ResearchDiagnostics.ClassifyAtSignal(closed, Math.Min(closed.Count - 1, Math.Max(from, warmup))),
                    []);
                ResearchDiagnostics.Fill(seed, replay, closed, out var filled);
                rows.Add(filled with { Status = AssignStatus(filled, phase) });
            }

            if ((IncludesWalkForward(request.Phase) || request.IncludeWalkForward) && !request.SkipWalkForward)
            {
                var train = Math.Min(400, Math.Max(warmup, Math.Max(80, closed.Count / 3)));
                var test = Math.Min(80, Math.Max(20, closed.Count / 10));
                var span = train + test;
                var available = Math.Max(0, closed.Count - span);
                var step = available <= 0 ? test : Math.Max(test, available / 7);
                var wfIndex = 0;
                foreach (var window in StrategyValidation.WalkForwardWindows(closed.Count, train, test, step).Take(8))
                {
                    var testStart = window.Start + train;
                    var testEnd = window.Start + window.Length;
                    if (testStart < warmup || testEnd > closed.Count)
                    {
                        continue;
                    }

                    var wfPhase = $"WF{wfIndex}";
                    var key = $"{candidate.CandidateId}|{symbol}|{timeframe}|{wfPhase}|{cost}";
                    if (!request.Force && request.Done.Contains(key))
                    {
                        wfIndex++;
                        continue;
                    }

                    var engine = ResolveEngine(request, candidate, symbol, timeframe, confirmation, contextCache);
                    var settings = CostScaledRisk(request, closed[testStart].OpenTime, closed[testEnd - 1].CloseTime, cost, candidate);
                    if (StrategyTemplateKeys.IsScalping(templateKey) && settings.MaxHoldBars <= 0)
                    {
                        settings = settings with { MaxHoldBars = ScalpingCatalog.MaxHoldBars(timeframe) };
                    }

                    var replay = new BacktestReplay(engine).Run(definition, closed, settings, cache, testStart, testEnd, htf);
                    var seed = new ResearchBookResult(
                        candidate.CandidateId,
                        symbol,
                        timeframe,
                        wfPhase,
                        cost,
                        ResearchStatuses.Researching,
                        testEnd - testStart,
                        0, 0, 0, 0, 0,
                        "NO_TRADES",
                        null, 0, 0, 0, 0, 0,
                        null, null, null, null, null, null, null, null, null, null, null, null,
                        ResearchDiagnostics.ClassifyAtSignal(closed, testStart),
                        ["Walk-forward TEST window only. Empty window PF is NO_TRADES, not 0."]);
                    ResearchDiagnostics.Fill(seed, replay, closed, out var filled);
                    rows.Add(filled with { Status = AssignStatus(filled, wfPhase) });
                    wfIndex++;
                }
            }
        }

        return rows;
    }

    private static IStrategyEngine ResolveEngine(
        ResearchRunRequest request,
        ResearchCandidate candidate,
        string symbol,
        string timeframe,
        CausalIndicatorCache? confirmation,
        CausalIndicatorCache? context)
    {
        if (request.PrecomputedSignals?.GetValueOrDefault((candidate.CandidateId, symbol, timeframe)) is { } signals)
        {
            return new PrecomputedResearchSignalEngine(signals);
        }

        return context is null
            ? new ResearchStrategyEngine(candidate)
            : new PriceActionMtfResearchEngine(candidate, confirmation, context);
    }

    public static ReplaySettings CostScaledRisk(DateTimeOffset from, DateTimeOffset to, string costLabel) =>
        CostScaledRisk(new ResearchRunRequest
        {
            Phase = ResearchPhases.Pilot,
            Candidates = [],
            Symbols = [],
            Timeframes = [],
            Series = new Dictionary<(string, string), IReadOnlyList<TradingPlatform.Domain.Market.MarketCandle>>()
        }, from, to, costLabel);

    public static ReplaySettings CostScaledRisk(ResearchRunRequest request, DateTimeOffset from, DateTimeOffset to, string costLabel, ResearchCandidate? candidate = null)
    {
        var baseline = request.UseLowIsolated
            ? StrategyValidation.LowIsolatedRisk(from, to)
            : StrategyValidation.FrozenRisk(from, to);
        var m = ResearchCostLabels.Multiplier(costLabel);
        var scaled = baseline with
        {
            FeePercent = baseline.FeePercent * m,
            SlippagePercent = baseline.SlippagePercent * m,
            HonorSuggestedStops = request.HonorSuggestedStops
        };
        return ApplyCandidateBook(scaled, candidate);
    }

    public static ReplaySettings IsolatedBookFor(ResearchCandidate candidate, DateTimeOffset from, DateTimeOffset to)
    {
        var baseline = StrategyValidation.LowIsolatedRisk(from, to);
        return ApplyCandidateBook(baseline, candidate);
    }

    public static ReplaySettings ApplyCandidateBook(ReplaySettings baseline, ResearchCandidate? candidate)
    {
        if (candidate is null)
        {
            return baseline;
        }

        var next = baseline;
        if (candidate.StopLossPercent > 0m)
        {
            next = next with { StopLossPercent = candidate.StopLossPercent };
        }

        if (candidate.TakeProfitPercent > 0m)
        {
            next = next with { TakeProfitPercent = candidate.TakeProfitPercent };
        }

        if (candidate.MaxHoldBars > 0)
        {
            next = next with { MaxHoldBars = candidate.MaxHoldBars };
        }

        return next;
    }

    public static StrategyDefinition DefinitionFor(ResearchCandidate candidate, string timeframe)
    {
        var template = candidate.ParentTemplateKey ?? StrategyTemplateKeys.EmaRsiTrend;
        var parameters = StrategyTemplates.DefaultsFor(template, true) with
        {
            TemplateKey = template,
            AllowedSide = StrategySides.Both,
            Timeframe = timeframe
        };
        var json = StrategyTemplates.Build(candidate.CandidateId, candidate.CandidateVersion, parameters);
        var definition = new StrategyDefinitionValidator().Parse(json);
        definition.Name = candidate.CandidateId;
        definition.Version = candidate.CandidateVersion;
        definition.Timeframe = timeframe;
        definition.AllowedSide = StrategySides.Both;
        return definition;
    }

    public static string ResolveHigherTimeframe(ResearchCandidate candidate, string entryTimeframe)
    {
        var raw = candidate.Filters.HigherTimeframe;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        return string.Equals(raw, "next", StringComparison.OrdinalIgnoreCase)
            ? ResearchRegistry.NextHigherTimeframe(entryTimeframe)
            : raw;
    }

    private static IEnumerable<(string Phase, int From, int To)> Windows(
        string phase,
        int count,
        int warmup,
        int insEnd,
        int valEnd,
        bool includeChronologicalBlocks)
    {
        var all = IsFullRun(phase);
        var discovery = string.Equals(phase, "DISCOVERY", StringComparison.OrdinalIgnoreCase);
        if (all || discovery || string.Equals(phase, ResearchPhases.Is, StringComparison.OrdinalIgnoreCase))
        {
            yield return ("IS", 0, insEnd);
        }

        if (all || discovery || string.Equals(phase, ResearchPhases.Validation, StringComparison.OrdinalIgnoreCase))
        {
            yield return ("VALIDATION", insEnd, valEnd);
        }

        if (all || string.Equals(phase, ResearchPhases.Oos, StringComparison.OrdinalIgnoreCase))
        {
            yield return ("OOS", valEnd, count);
        }

        if (includeChronologicalBlocks)
        {
            for (var block = 0; block < 4; block++)
            {
                var from = block * count / 4;
                var to = (block + 1) * count / 4;
                yield return ($"BLOCK{block + 1}", from, to);
            }
        }
    }

    private static bool IsFullRun(string phase) =>
        string.Equals(phase, ResearchPhases.Pilot, StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, ResearchPhases.Two, StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "2", StringComparison.OrdinalIgnoreCase)
        || string.Equals(phase, "ALL", StringComparison.OrdinalIgnoreCase);

    private static bool IncludesWalkForward(string phase) =>
        IsFullRun(phase)
        || string.Equals(phase, ResearchPhases.WalkForward, StringComparison.OrdinalIgnoreCase);

    private static string AssignStatus(ResearchBookResult row, string phase)
    {
        // Research CLI never auto-promotes. VALIDATED_FOR_PAPER is not assigned here.
        if (row.TradeCount == 0)
        {
            return ResearchStatuses.NoTrades;
        }

        if (string.Equals(phase, "VALIDATION", StringComparison.OrdinalIgnoreCase)
            && row.ProfitFactorState == "NORMAL"
            && row.ProfitFactor is { } pf
            && pf < 1m)
        {
            return ResearchStatuses.ValidationFailed;
        }

        if (string.Equals(phase, "OOS", StringComparison.OrdinalIgnoreCase)
            && row.ProfitFactorState == "NORMAL"
            && row.ProfitFactor is { } oos
            && oos < 1m)
        {
            return ResearchStatuses.OosFailed;
        }

        if (string.Equals(phase, "IS", StringComparison.OrdinalIgnoreCase)
            && row.Expectancy > 0m
            && row.ProfitFactor is > 1m)
        {
            return ResearchStatuses.IsPromising;
        }

        return ResearchStatuses.Researching;
    }

    private static ResearchBookResult Skip(
        ResearchCandidate candidate,
        string symbol,
        string timeframe,
        string phase,
        string cost,
        string status,
        string note) =>
        new(
            candidate.CandidateId,
            symbol,
            timeframe,
            phase,
            cost,
            status,
            0, 0, 0, 0, 0, 0,
            "NO_TRADES",
            null, 0, 0, 0, 0, 0,
            null, null, null, null, null, null, null, null, null, null, null, null,
            "TRANSITION",
            [note]);
}
