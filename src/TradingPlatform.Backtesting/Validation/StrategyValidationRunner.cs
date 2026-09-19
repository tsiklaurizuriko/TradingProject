using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Backtesting.Validation;

public static class StrategyValidationRunner
{
    public static IReadOnlyList<TemplateValidationResult> Evaluate(
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        bool implementationOk)
    {
        var caches = new Dictionary<(string Symbol, string Timeframe), (List<MarketCandle> Closed, CausalIndicatorCache Cache)>();
        var rows = new List<TemplateValidationResult>();
        foreach (var key in StrategyTemplateKeys.All)
        {
            rows.Add(EvaluateTemplate(key, series, implementationOk, caches));
        }

        return rows;
    }

    public static TemplateValidationResult MergeTemplates(TemplateValidationResult left, TemplateValidationResult right)
    {
        var combined = left.Combined is null ? right.Combined : right.Combined is null ? left.Combined : Merge(left.Combined, right.Combined);
        var insample = left.InSample is null ? right.InSample : right.InSample is null ? left.InSample : Merge(left.InSample, right.InSample);
        var validation = left.Validation is null ? right.Validation : right.Validation is null ? left.Validation : Merge(left.Validation, right.Validation);
        var oos = left.OutOfSample is null ? right.OutOfSample : right.OutOfSample is null ? left.OutOfSample : Merge(left.OutOfSample, right.OutOfSample);
        var walk = left.WalkForward.Concat(right.WalkForward).ToList();
        var symbols = left.Symbols.Concat(right.Symbols).Where(s => !string.Equals(s, "none", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s).ToArray();
        var timeframes = left.Timeframes.Concat(right.Timeframes).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToArray();
        var notes = left.Notes.Concat(right.Notes).ToList();
        var regimes = left.Regimes.Concat(right.Regimes)
            .GroupBy(r => r.Regime, StringComparer.OrdinalIgnoreCase)
            .Select(g => new RegimeRow(
                g.Key,
                g.Sum(x => x.Windows),
                Median(g.Select(x => x.MedianPf).ToList()),
                g.Sum(x => x.Trades)))
            .OrderBy(r => r.Regime)
            .ToList();
        var periodStart = Min(left.PeriodStart, right.PeriodStart);
        var periodEnd = Max(left.PeriodEnd, right.PeriodEnd);
        var barCount = Math.Max(left.Combined?.BarsUsed ?? 0, right.Combined?.BarsUsed ?? 0);
        var sensitivity = left.Sensitivity.Count > 0 ? left.Sensitivity : right.Sensitivity;
        var status = StrategyValidation.AssignStatus(
            true,
            barCount,
            oos,
            insample,
            combined,
            sensitivity,
            combined?.Long,
            combined?.Short);
        return left with
        {
            Status = status,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Timeframes = timeframes.Length == 0 ? left.Timeframes : timeframes,
            Symbols = symbols.Length == 0 ? left.Symbols : symbols,
            Long = combined?.Long,
            Short = combined?.Short,
            Combined = combined,
            WalkForward = walk,
            InSample = insample,
            Validation = validation,
            OutOfSample = oos,
            Sensitivity = sensitivity,
            Regimes = regimes,
            Notes = notes,
            CostNotes = combined?.CostNotes ?? left.CostNotes
        };
    }

    private static TemplateValidationResult EvaluateTemplate(
        string templateKey,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        bool implementationOk,
        Dictionary<(string Symbol, string Timeframe), (List<MarketCandle> Closed, CausalIndicatorCache Cache)> caches)
    {
        var notes = new List<string>
        {
            "Model B: causal full-history indicators with independent execution windows. Model A slice-reseeded results are stale and must be rerun."
        };
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var timeframes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var walk = new List<ValidationSlice>();
        var regimes = new Dictionary<string, List<ReplayResult>>(StringComparer.OrdinalIgnoreCase);
        ReplayResult? combined = null;
        ReplayResult? insample = null;
        ReplayResult? validation = null;
        ReplayResult? oos = null;
        var sensitivity = new List<SensitivityRow>();
        DateTimeOffset? periodStart = null;
        DateTimeOffset? periodEnd = null;
        var barCount = 0;

        foreach (var (slot, candles) in series.OrderBy(s => s.Key.Symbol).ThenBy(s => s.Key.Timeframe))
        {
            if (!caches.TryGetValue(slot, out var prepared))
            {
                var listed = candles.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                prepared = (listed, new CausalIndicatorCache(listed));
                caches[slot] = prepared;
            }

            var closed = prepared.Closed;
            var definition = StrategyValidation.Definition(templateKey, slot.Timeframe);
            var warmup = StrategyValidation.WarmupBars(definition);
            if (closed.Count < warmup + StrategyValidation.MinimumEvaluatedBars)
            {
                notes.Add($"{slot.Symbol} {slot.Timeframe}: INSUFFICIENT_DATA ({closed.Count} bars, warmup {warmup}).");
                continue;
            }

            symbols.Add(slot.Symbol);
            timeframes.Add(slot.Timeframe);
            barCount = Math.Max(barCount, closed.Count);
            periodStart = periodStart is null ? closed[0].OpenTime : Min(periodStart.Value, closed[0].OpenTime);
            periodEnd = periodEnd is null ? closed[^1].CloseTime : Max(periodEnd.Value, closed[^1].CloseTime);

            var full = StrategyValidation.Run(definition, closed, prepared.Cache, 0, closed.Count);
            combined = Strip(Merge(combined, full));

            var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(closed.Count);
            if (insEnd - warmup >= 80)
            {
                insample = Strip(Merge(insample, StrategyValidation.Run(definition, closed, prepared.Cache, 0, insEnd)));
            }
            else
            {
                notes.Add($"{slot.Symbol} {slot.Timeframe}: INSUFFICIENT_DATA for IS after warmup ({Math.Max(0, insEnd - warmup)} bars).");
            }

            if (valEnd - Math.Max(insEnd, warmup) >= 40)
            {
                validation = Strip(Merge(validation, StrategyValidation.Run(definition, closed, prepared.Cache, insEnd, valEnd)));
            }
            else
            {
                notes.Add($"{slot.Symbol} {slot.Timeframe}: INSUFFICIENT_DATA for Validation ({Math.Max(0, valEnd - Math.Max(insEnd, warmup))} bars).");
            }

            if (closed.Count - Math.Max(valEnd, warmup) >= 40)
            {
                oos = Strip(Merge(oos, StrategyValidation.Run(definition, closed, prepared.Cache, valEnd, closed.Count)));
            }
            else
            {
                notes.Add($"{slot.Symbol} {slot.Timeframe}: INSUFFICIENT_DATA for OOS ({Math.Max(0, closed.Count - Math.Max(valEnd, warmup))} bars).");
            }

            var train = Math.Min(400, Math.Max(warmup, Math.Max(80, closed.Count / 3)));
            var test = Math.Min(80, Math.Max(20, closed.Count / 10));
            var span = train + test;
            var available = Math.Max(0, closed.Count - span);
            var step = available <= 0 ? test : Math.Max(test, available / 7);
            foreach (var window in StrategyValidation.WalkForwardWindows(closed.Count, train, test, step).Take(8))
            {
                var testStart = window.Start + train;
                var testEnd = window.Start + window.Length;
                if (testStart < warmup || testEnd > closed.Count || testEnd - testStart < 1)
                {
                    continue;
                }

                var result = StrategyValidation.Run(definition, closed, prepared.Cache, testStart, testEnd);
                var label = $"{slot.Symbol} {slot.Timeframe} {closed[testStart].OpenTime:yyyy-MM-dd}";
                walk.Add(new ValidationSlice(label, closed[testStart].OpenTime, closed[testEnd - 1].CloseTime, testEnd - testStart, Strip(result)));
                var testView = new SliceList<MarketCandle>(closed, testStart, testEnd - testStart);
                var regime = StrategyValidation.ClassifyRegime(testView);
                if (!regimes.TryGetValue(regime, out var bucket))
                {
                    bucket = [];
                    regimes[regime] = bucket;
                }

                bucket.Add(Strip(result));
            }

            if (sensitivity.Count == 0)
            {
                sensitivity.AddRange(Sensitivity(templateKey, slot.Timeframe, closed, prepared.Cache, full));
            }
        }

        var longSide = combined?.Long;
        var shortSide = combined?.Short;
        var status = StrategyValidation.AssignStatus(
            implementationOk,
            barCount,
            oos,
            insample,
            combined,
            sensitivity,
            longSide,
            shortSide);
        var failure = FailureModes(templateKey, combined, longSide, shortSide, status);
        var next = status switch
        {
            StrategyValidationStatuses.ValidatedForPaper => "Eligible for PAPER only. Do not enable LIVE from this report.",
            StrategyValidationStatuses.InsufficientData => "Fetch a longer closed-candle history and re-run the harness. Do not invent bars.",
            StrategyValidationStatuses.Unstable => "Keep the template. Do not pick a lucky parameter. Re-test a robust region later.",
            StrategyValidationStatuses.OosDegradation => "Keep the template disabled from any LIVE path. Frozen defaults did not hold on the holdout.",
            StrategyValidationStatuses.ImplementationError => "Disable the row without deleting it. Fix tests first.",
            _ => "Leave VALIDATION_PENDING. No LIVE. User decides whether to paper-test."
        };

        return new TemplateValidationResult(
            templateKey,
            StrategyTemplates.DisplayName(templateKey),
            status,
            Implementation(templateKey),
            LongLogic(templateKey),
            ShortLogic(templateKey),
            "No future bars. Donchian excludes the current high/low. Bollinger includes the current close. Evaluation windows use causal full-history indicators and start flat.",
            "Closed-bar prefix signals stay stable when later bars are appended (unit tests).",
            periodStart,
            periodEnd,
            timeframes.Count == 0 ? StrategyValidation.Timeframes : timeframes.OrderBy(t => t).ToArray(),
            symbols.Count == 0 ? ["none"] : symbols.OrderBy(s => s).ToArray(),
            longSide,
            shortSide,
            combined,
            walk,
            insample,
            validation,
            oos,
            sensitivity,
            regimes.Select(kv => new RegimeRow(
                kv.Key,
                kv.Value.Count,
                Median(kv.Value.Select(v => v.ProfitFactor).ToList()),
                kv.Value.Sum(v => v.NumberOfTrades))).OrderBy(r => r.Regime).ToList(),
            failure,
            next,
            combined?.CostNotes ?? "EXCLUDING_FUNDING",
            notes);
    }

    private static IReadOnlyList<SensitivityRow> Sensitivity(
        string templateKey,
        string timeframe,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        ReplayResult baseline)
    {
        var rows = new List<SensitivityRow>();
        var defaults = StrategyTemplates.DefaultsFor(templateKey, true) with { AllowedSide = StrategySides.Both, Timeframe = timeframe };
        if (templateKey == StrategyTemplateKeys.DonchianBreakout)
        {
            var changed = StrategyValidation.Run(
                StrategyValidation.Definition(templateKey, timeframe, defaults with { DonchianLength = defaults.DonchianLength + 1 }),
                candles,
                cache,
                0,
                candles.Count);
            rows.Add(StrategyValidation.Compare("DonchianLength+1", baseline, changed));
        }
        else if (templateKey is StrategyTemplateKeys.EmaRsiTrend or StrategyTemplateKeys.RsiPullback)
        {
            var changed = StrategyValidation.Run(
                StrategyValidation.Definition(templateKey, timeframe, defaults with { RsiPeriod = defaults.RsiPeriod + 1 }),
                candles,
                cache,
                0,
                candles.Count);
            rows.Add(StrategyValidation.Compare("RsiPeriod+1", baseline, changed));
        }
        else if (templateKey == StrategyTemplateKeys.BollingerReversion)
        {
            var changed = StrategyValidation.Run(
                StrategyValidation.Definition(templateKey, timeframe, defaults with { BbStdDev = defaults.BbStdDev + 0.2m }),
                candles,
                cache,
                0,
                candles.Count);
            rows.Add(StrategyValidation.Compare("BbStdDev+0.2", baseline, changed));
        }
        else
        {
            var changed = StrategyValidation.Run(
                StrategyValidation.Definition(templateKey, timeframe, defaults with { MacdSignal = defaults.MacdSignal + 1 }),
                candles,
                cache,
                0,
                candles.Count);
            rows.Add(StrategyValidation.Compare("MacdSignal+1", baseline, changed));
        }

        return rows;
    }

    private static ReplayResult Merge(ReplayResult? left, ReplayResult right)
    {
        if (left is null)
        {
            return right;
        }

        if (left.Trades.Count > 0 && right.Trades.Count > 0)
        {
            var trades = left.Trades.Concat(right.Trades).OrderBy(t => t.OpenedAt).ToList();
            var fees = left.FeesPaid + right.FeesPaid;
            var net = left.NetProfit + right.NetProfit;
            var wins = trades.Where(t => t.PnL > 0m).ToList();
            var losses = trades.Where(t => t.PnL < 0m).ToList();
            var grossWin = wins.Sum(t => t.PnL);
            var grossLoss = Math.Abs(losses.Sum(t => t.PnL));
            var capital = left.InitialBalance;
            return left with
            {
                FinalBalance = capital + net,
                NetProfit = net,
                ReturnPercent = capital == 0m ? 0m : Math.Round(net / capital * 100m, 8, MidpointRounding.AwayFromZero),
                NumberOfTrades = trades.Count,
                WinRate = trades.Count == 0 ? 0m : Math.Round((decimal)wins.Count / trades.Count * 100m, 8, MidpointRounding.AwayFromZero),
                ProfitFactor = grossLoss == 0m ? (grossWin > 0m ? 99m : 0m) : Math.Round(grossWin / grossLoss, 8, MidpointRounding.AwayFromZero),
                AverageWin = wins.Count == 0 ? 0m : Math.Round(wins.Average(t => t.PnL), 8, MidpointRounding.AwayFromZero),
                AverageLoss = losses.Count == 0 ? 0m : Math.Round(losses.Average(t => t.PnL), 8, MidpointRounding.AwayFromZero),
                MaximumDrawdown = Math.Max(left.MaximumDrawdown, right.MaximumDrawdown),
                FeesPaid = fees,
                LargestWinningTrade = trades.Count == 0 ? 0m : trades.Max(t => t.PnL),
                LargestLosingTrade = trades.Count == 0 ? 0m : trades.Min(t => t.PnL),
                BarsUsed = left.BarsUsed + right.BarsUsed,
                WindowStart = left.WindowStart < right.WindowStart ? left.WindowStart : right.WindowStart,
                WindowEnd = left.WindowEnd > right.WindowEnd ? left.WindowEnd : right.WindowEnd,
                Trades = trades,
                Long = ReplayMetrics.ForSide(trades, "Long"),
                Short = ReplayMetrics.ForSide(trades, "Short")
            };
        }

        var leftWins = WinCount(left);
        var rightWins = WinCount(right);
        var winsCount = leftWins + rightWins;
        var tradeCount = left.NumberOfTrades + right.NumberOfTrades;
        var lossCount = Math.Max(0, tradeCount - winsCount);
        var summaryGrossWin = left.AverageWin * leftWins + right.AverageWin * rightWins;
        var summaryGrossLoss = Math.Abs(left.AverageLoss) * (left.NumberOfTrades - leftWins) + Math.Abs(right.AverageLoss) * (right.NumberOfTrades - rightWins);
        var summaryNet = left.NetProfit + right.NetProfit;
        var summaryCapital = left.InitialBalance;
        return left with
        {
            FinalBalance = summaryCapital + summaryNet,
            NetProfit = summaryNet,
            ReturnPercent = summaryCapital == 0m ? 0m : Math.Round(summaryNet / summaryCapital * 100m, 8, MidpointRounding.AwayFromZero),
            NumberOfTrades = tradeCount,
            WinRate = tradeCount == 0 ? 0m : Math.Round((decimal)winsCount / tradeCount * 100m, 8, MidpointRounding.AwayFromZero),
            ProfitFactor = summaryGrossLoss == 0m ? (summaryGrossWin > 0m ? 99m : 0m) : Math.Round(summaryGrossWin / summaryGrossLoss, 8, MidpointRounding.AwayFromZero),
            AverageWin = winsCount == 0 ? 0m : Math.Round(summaryGrossWin / winsCount, 8, MidpointRounding.AwayFromZero),
            AverageLoss = lossCount == 0 ? 0m : Math.Round(-(summaryGrossLoss / lossCount), 8, MidpointRounding.AwayFromZero),
            MaximumDrawdown = Math.Max(left.MaximumDrawdown, right.MaximumDrawdown),
            FeesPaid = left.FeesPaid + right.FeesPaid,
            LargestWinningTrade = Math.Max(left.LargestWinningTrade, right.LargestWinningTrade),
            LargestLosingTrade = Math.Min(left.LargestLosingTrade, right.LargestLosingTrade),
            BarsUsed = left.BarsUsed + right.BarsUsed,
            WindowStart = left.WindowStart < right.WindowStart ? left.WindowStart : right.WindowStart,
            WindowEnd = left.WindowEnd > right.WindowEnd ? left.WindowEnd : right.WindowEnd,
            Trades = [],
            Equity = [],
            Long = MergeSides(left.Long, right.Long),
            Short = MergeSides(left.Short, right.Short)
        };
    }

    private static int WinCount(ReplayResult result) =>
        result.NumberOfTrades <= 0 ? 0 : (int)Math.Round(result.NumberOfTrades * result.WinRate / 100m, MidpointRounding.AwayFromZero);

    private static ReplaySideMetrics MergeSides(ReplaySideMetrics left, ReplaySideMetrics right)
    {
        var trades = left.Trades + right.Trades;
        var net = left.NetPnL + right.NetPnL;
        var winRate = trades == 0 ? 0m : Math.Round((left.WinRate * left.Trades + right.WinRate * right.Trades) / trades, 8, MidpointRounding.AwayFromZero);
        var pf = trades == 0 ? 0m : Math.Round((left.ProfitFactor * left.Trades + right.ProfitFactor * right.Trades) / trades, 8, MidpointRounding.AwayFromZero);
        return new ReplaySideMetrics(
            trades,
            net,
            winRate,
            pf,
            trades == 0 ? 0m : Math.Round(net / trades, 8, MidpointRounding.AwayFromZero),
            Math.Max(left.MaximumDrawdown, right.MaximumDrawdown));
    }

    private static ReplayResult Strip(ReplayResult result) =>
        result with { Trades = [], Equity = [] };

    private static decimal Median(IReadOnlyList<decimal> values)
    {
        if (values.Count == 0)
        {
            return 0m;
        }

        var ordered = values.OrderBy(v => v).ToList();
        return ordered[ordered.Count / 2];
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    private static DateTimeOffset? Min(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a.Value <= b.Value ? a : b;

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : a.Value >= b.Value ? a : b;

    private static string Implementation(string key) => key switch
    {
        StrategyTemplateKeys.DonchianBreakout => "Break event on prior-N channel (current high/low excluded). Closed candles only.",
        StrategyTemplateKeys.BollingerReversion => "Close returns inside the current bands with a slow-EMA side filter. Closed candles only.",
        StrategyTemplateKeys.MacdTrend => "MACD/signal crossover plus histogram sign and slow EMA. Closed candles only.",
        StrategyTemplateKeys.RsiPullback => "RSI oversold/overbought cross with local slow-EMA trend filter. Closed candles only.",
        _ => "EMA cross plus RSI band. Closed candles only. Not RSI>50 as the entry."
    };

    private static string LongLogic(string key) => key switch
    {
        StrategyTemplateKeys.DonchianBreakout => "Previous bar inside the prior-N high, this close breaks above it.",
        StrategyTemplateKeys.BollingerReversion => "Previous close below the lower band, this close back inside, still above slow EMA.",
        StrategyTemplateKeys.MacdTrend => "MACD crosses above signal, histogram > 0, close above slow EMA.",
        StrategyTemplateKeys.RsiPullback => "Close above slow EMA, RSI crosses up through oversold.",
        _ => "Fast EMA crosses above slow, close above slow, RSI in the long band."
    };

    private static string ShortLogic(string key) => key switch
    {
        StrategyTemplateKeys.DonchianBreakout => "Previous bar inside the prior-N low, this close breaks below it.",
        StrategyTemplateKeys.BollingerReversion => "Previous close above the upper band, this close back inside, still below slow EMA.",
        StrategyTemplateKeys.MacdTrend => "MACD crosses below signal, histogram < 0, close below slow EMA.",
        StrategyTemplateKeys.RsiPullback => "Close below slow EMA, RSI crosses down through overbought.",
        _ => "Fast EMA crosses below slow, close below slow, mirrored RSI band."
    };

    private static string FailureModes(
        string templateKey,
        ReplayResult? combined,
        ReplaySideMetrics? longSide,
        ReplaySideMetrics? shortSide,
        string status)
    {
        var parts = new List<string>();
        if (combined is null || combined.NumberOfTrades == 0)
        {
            parts.Add("No Isolated round-trips in the fetched window after fees/slippage/quality filters.");
        }

        if (longSide is { Trades: 0 })
        {
            parts.Add("LONG produced no trades in this sample.");
        }

        if (shortSide is { Trades: 0 })
        {
            parts.Add("SHORT produced no trades in this sample.");
        }

        parts.Add(templateKey switch
        {
            StrategyTemplateKeys.BollingerReversion => "Expected to struggle in strong trends; the slow-EMA filter is not ADX.",
            StrategyTemplateKeys.DonchianBreakout => "Expected to struggle in ranges; SL can flatten a valid breakout then wait for a new arm.",
            StrategyTemplateKeys.EmaRsiTrend => "Crosses are infrequent; RSI band can skip the actual cross.",
            StrategyTemplateKeys.MacdTrend => "Late crosses in extended moves; histogram filter can skip the cross.",
            _ => "Pullback may never print if RSI does not recross the threshold in trend."
        });
        parts.Add($"Status {status} is historical only.");
        return string.Join(" ", parts);
    }
}
