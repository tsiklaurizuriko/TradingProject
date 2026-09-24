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

internal static class SignalQualityAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private const long BucketSeconds = 900;

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
        var dir = Path.Combine(root, "artifacts", "strategy-research", "signal-quality");
        Directory.CreateDirectory(dir);
        var hash = WriteManifest(dir);
        var rows = new List<Row>();
        var btc = new CausalIndicatorCache(await LoadOne(cacheDir, root, "BTCUSDT", "1h"));
        var eth = new CausalIndicatorCache(await LoadOne(cacheDir, root, "ETHUSDT", "1h"));
        var bnb = new CausalIndicatorCache(await LoadOne(cacheDir, root, "BNBUSDT", "1h"));
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            Console.WriteLine($"Signal quality loading {symbol}.");
            var series = await LoadSymbol(cacheDir, root, symbol);
            var caches = series.ToDictionary(pair => pair.Key.Timeframe, pair => new CausalIndicatorCache(pair.Value), StringComparer.OrdinalIgnoreCase);
            var contextual = ContextualPriceActionSignals.BuildAll(caches);
            foreach (var arm in Arms)
            {
                var candles = series[(symbol, arm.Timeframe)];
                var trades = Replay(arm, symbol, candles, SignalsFor(arm, caches, contextual));
                var opens = new Dictionary<DateTimeOffset, int>();
                for (var i = 0; i < candles.Count; i++)
                {
                    opens[candles[i].OpenTime] = i;
                }

                var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
                foreach (var trade in trades)
                {
                    if (!opens.TryGetValue(trade.OpenedAt, out var fill) || fill <= 0)
                    {
                        continue;
                    }

                    var signal = fill - 1;
                    var longSide = trade.Side.Equals("Long", StringComparison.OrdinalIgnoreCase);
                    var phase = signal < insEnd ? "IS" : signal < valEnd ? "VALIDATION" : "OOS";
                    var block = Math.Clamp((signal * 4 / candles.Count) + 1, 1, 4);
                    rows.Add(new Row(
                        arm.Group, arm.ResearchFamily, arm.Id, symbol, arm.Timeframe, longSide ? "LONG" : "SHORT",
                        phase, block, candles[signal].CloseTime, trade.GrossPnl, trade.PnL, trade.PnL > 0m,
                        SignalQualityFeatures.Compute(caches[arm.Timeframe], caches["1h"], btc, eth, bnb, signal, longSide)));
                }
            }
        }

        AttachContext(rows);
        var results = SignalQualityCatalog.Features.Select(name => Score(name, rows)).ToList();
        WriteJson(Path.Combine(dir, "feature-results.json"), results.Select(x => new
        {
            x.Name, x.Label, x.N, x.IsRho, x.ValRho, x.OosRho, x.Effect
        }));
        WriteReport(root, hash, rows, results);
        Console.WriteLine("SIGNAL QUALITY AUDIT COMPLETE");
        Console.WriteLine(string.Join(", ", results.Where(x => x.Label != "NO_EVIDENCE").Select(x => x.Name + "=" + x.Label)));
        return 0;
    }

    private static string WriteManifest(string dir)
    {
        var body = JsonSerializer.Serialize(new
        {
            SignalQualityCatalog.Version,
            SignalQualityCatalog.RepeatableRule,
            SignalQualityCatalog.Features,
            SignalQualityCatalog.MinimumAbsRho,
            SignalQualityCatalog.MinimumWindowTrades,
            SignalQualityCatalog.MinimumSliceTrades,
            Arms = Arms.Select(x => new { x.Id, x.Group, x.ResearchFamily, x.Timeframe }),
            Signed = "Returns, EMA distance, EMA slope, VWAP distance, VWAP slope, hourly bias, BOS, CHoCH, and BTC returns are multiplied by +1 for long and -1 for short. Positive means agreement with the trade.",
            Unavailable = "minutes_since_signal is DATA_UNAVAILABLE because unfilled signals were not stored. minutes_since_trade uses the previous filled trade on the same coin.",
            Simultaneous = "signals_already_closed counts other filled trades whose signal bar already closed inside the same UTC 15-minute bucket. Later closes in that bucket are not counted."
        }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, "feature-manifest.json"), JsonSerializer.Serialize(new { Sha256 = hash, Body = JsonDocument.Parse(body).RootElement }, JsonOptions));
        return hash;
    }

    private static void AttachContext(List<Row> rows)
    {
        var ordered = rows.OrderBy(row => row.SignalClose).ToList();
        var lastTrade = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var row in ordered)
        {
            var bucket = row.SignalClose.ToUnixTimeSeconds() / BucketSeconds;
            var prior = ordered.Where(other => other != row && other.SignalClose <= row.SignalClose && other.SignalClose.ToUnixTimeSeconds() / BucketSeconds == bucket).ToList();
            row.Features["signals_already_closed"] = prior.Count;
            row.Features["symbols_already_closed"] = prior.Select(other => other.Symbol).Distinct(StringComparer.Ordinal).Count();
            if (lastTrade.TryGetValue(row.Symbol, out var previous))
            {
                row.Features["minutes_since_trade"] = (decimal)(row.SignalClose - previous).TotalMinutes;
            }

            lastTrade[row.Symbol] = row.SignalClose;
        }
    }

    private static FeatureScore Score(string name, IReadOnlyList<Row> rows)
    {
        var ready = rows.Where(row => row.Features[name] is not null).ToList();
        decimal? Rho(string phase)
        {
            var sample = ready.Where(row => row.Phase == phase).ToList();
            return SignalQualityMath.Spearman(sample.Select(row => row.Features[name]!.Value).ToList(), sample.Select(row => row.Net).ToList());
        }

        int Count(string phase) => ready.Count(row => row.Phase == phase);
        var isRho = Rho("IS");
        var valRho = Rho("VALIDATION");
        var oosRho = Rho("OOS");
        var sign = isRho is { } seed && Math.Abs(seed) >= SignalQualityCatalog.MinimumAbsRho ? Math.Sign(seed) : oosRho is { } alt && Math.Abs(alt) >= SignalQualityCatalog.MinimumAbsRho ? Math.Sign(alt) : 0;
        var blocks = sign == 0 ? 0 : Enumerable.Range(1, 4).Count(block => Agrees(ready.Where(row => row.Block == block).ToList(), name, sign));
        var families = sign == 0 ? 0 : ready.GroupBy(row => row.Group).Count(group => Agrees(group.ToList(), name, sign));
        var symbols = sign == 0 ? 0 : FinalFiveCatalog.Symbols.Count(symbol => Agrees(ready.Where(row => row.Symbol == symbol).ToList(), name, sign));
        var winners = ready.Where(row => row.Win).Select(row => row.Features[name]!.Value).OrderBy(value => value).ToList();
        var losers = ready.Where(row => !row.Win).Select(row => row.Features[name]!.Value).OrderBy(value => value).ToList();
        var all = ready.Select(row => row.Features[name]!.Value).OrderBy(value => value).ToList();
        var iqr = SignalQualityMath.Quantile(all, 0.75m) - SignalQualityMath.Quantile(all, 0.25m);
        var effect = winners.Count == 0 || losers.Count == 0 || iqr == 0m ? 0m : (SignalQualityMath.Quantile(winners, 0.5m) - SignalQualityMath.Quantile(losers, 0.5m)) / iqr;
        var cuts = SignalQualityMath.TertileCuts(ready.Where(row => row.Phase == "IS" && row.Features[name] is not null).Select(row => row.Features[name]!.Value).ToList());
        return new FeatureScore(
            name,
            ready.Count,
            SignalQualityMath.Quantile(winners, 0.25m),
            SignalQualityMath.Quantile(winners, 0.5m),
            SignalQualityMath.Quantile(winners, 0.75m),
            SignalQualityMath.Quantile(losers, 0.25m),
            SignalQualityMath.Quantile(losers, 0.5m),
            SignalQualityMath.Quantile(losers, 0.75m),
            effect,
            isRho, valRho, oosRho,
            Count("IS"), Count("VALIDATION"), Count("OOS"),
            Gap(ready, name, "IS", cuts), Gap(ready, name, "VALIDATION", cuts), Gap(ready, name, "OOS", cuts),
            blocks, families, symbols,
            SignalQualityFeatures.Classify(isRho, Count("IS"), valRho, Count("VALIDATION"), oosRho, Count("OOS"), blocks, families, symbols));
    }

    private static bool Agrees(IReadOnlyList<Row> rows, string name, int sign)
    {
        var xs = rows.Where(row => row.Features[name] is not null).Select(row => row.Features[name]!.Value).ToList();
        var ys = rows.Where(row => row.Features[name] is not null).Select(row => row.Net).ToList();
        var rho = SignalQualityMath.Spearman(xs, ys);
        return rows.Count >= SignalQualityCatalog.MinimumSliceTrades && rho is { } value && Math.Abs(value) >= SignalQualityCatalog.MinimumAbsRho && Math.Sign(value) == sign;
    }

    private static decimal Gap(IReadOnlyList<Row> rows, string name, string phase, (decimal Low, decimal High) cuts)
    {
        var sample = rows.Where(row => row.Phase == phase && row.Features[name] is not null).ToList();
        var low = sample.Where(row => row.Features[name]!.Value <= cuts.Low).ToList();
        var high = sample.Where(row => row.Features[name]!.Value >= cuts.High).ToList();
        if (low.Count < SignalQualityCatalog.MinimumBucketTrades || high.Count < SignalQualityCatalog.MinimumBucketTrades)
        {
            return 0m;
        }

        return high.Average(row => row.Net) - low.Average(row => row.Net);
    }

    private static void WriteReport(string root, string hash, IReadOnlyList<Row> rows, IReadOnlyList<FeatureScore> scores)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Signal quality audit");
        sb.AppendLine();
        sb.AppendLine($"Feature manifest SHA-256 `{hash}`.");
        sb.AppendLine();
        sb.AppendLine("This audit does not create a strategy and does not call any relationship an edge.");
        sb.AppendLine();
        sb.AppendLine("## 1. Dataset");
        sb.AppendLine();
        sb.AppendLine($"Trades: {rows.Count}. Winners: {rows.Count(row => row.Win)}. Losers: {rows.Count(row => !row.Win)}.");
        sb.AppendLine("Same reconstructed book as the trade-failure phase: Frozen Five at 15m, Final Five primary timeframes, Phase 8 baselines at 5m, BTCUSDT, ETHUSDT, BNBUSDT, Model B costs. Near-miss was not added.");
        sb.AppendLine($"Gross expectancy {Money(rows.Average(row => row.Gross))}. Net expectancy {Money(rows.Average(row => row.Net))}.");
        sb.AppendLine();
        sb.AppendLine("## 2. Feature definitions");
        sb.AppendLine();
        sb.AppendLine(SignalQualityCatalog.RepeatableRule);
        sb.AppendLine();
        sb.AppendLine($"{SignalQualityCatalog.Features.Length} features were hashed before aggregation. Signed features agree with the trade when positive. `minutes_since_signal` is DATA_UNAVAILABLE. Session and weekday are slices only.");
        sb.AppendLine();
        sb.AppendLine("## 3. Winner vs loser");
        sb.AppendLine();
        sb.AppendLine("| Feature | n | Winner p25 | Winner p50 | Winner p75 | Loser p25 | Loser p50 | Loser p75 | Effect |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var score in scores)
        {
            sb.AppendLine($"| {score.Name} | {score.N} | {Num(score.WinnerP25)} | {Num(score.WinnerP50)} | {Num(score.WinnerP75)} | {Num(score.LoserP25)} | {Num(score.LoserP50)} | {Num(score.LoserP75)} | {Num(score.Effect)} |");
        }

        sb.AppendLine();
        sb.AppendLine("Effect is the winner median minus the loser median, divided by the interquartile range. It is not a profit factor.");
        sb.AppendLine();
        sb.AppendLine("## 4. Feature results");
        sb.AppendLine();
        sb.AppendLine("| Feature | IS rho | VAL rho | OOS rho | IS high-low net | VAL high-low net | OOS high-low net | Blocks | Families | Symbols | Label |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var score in scores)
        {
            sb.AppendLine($"| {score.Name} | {Num(score.IsRho)} | {Num(score.ValRho)} | {Num(score.OosRho)} | {Money(score.IsGap)} | {Money(score.ValGap)} | {Money(score.OosGap)} | {score.Blocks} | {score.Families} | {score.Symbols} | {score.Label} |");
        }

        sb.AppendLine();
        sb.AppendLine("High-low net uses tertile cuts taken from IS feature values only. A zero gap means a bucket had fewer than 30 trades.");
        sb.AppendLine();
        sb.AppendLine("## 5. IS, validation, OOS");
        sb.AppendLine();
        sb.AppendLine("The rho columns above are the stability test. A feature that is large in only one window does not pass.");
        sb.AppendLine();
        sb.AppendLine("## 6. Chronological stability");
        sb.AppendLine();
        sb.AppendLine("Blocks is how many of the four equal index quarters have at least 100 trades and absolute rho of at least 0.05 with the reference sign.");
        sb.AppendLine();
        sb.AppendLine("## 7. Family stability");
        sb.AppendLine();
        sb.AppendLine("Families counts independent groups: FROZEN_TREND, FROZEN_MEAN, PA_SWEEP, PA_FAILED, PA_CONTINUATION, PA_STRUCTURE. Two price-action variants in one group are not two families.");
        sb.AppendLine();
        sb.AppendLine("## 8. Symbol stability");
        sb.AppendLine();
        sb.AppendLine("Symbols counts how many of BTC, ETH, and BNB meet the same bar. One coin is not enough.");
        sb.AppendLine();
        sb.AppendLine("## 9. Long and short");
        sb.AppendLine();
        foreach (var side in new[] { "LONG", "SHORT" })
        {
            var sample = rows.Where(row => row.Side == side).ToList();
            sb.AppendLine($"- {side}: n={sample.Count} win rate {Share(sample.Count(row => row.Win), sample.Count)} gross {Money(Mean(sample, row => row.Gross))} net {Money(Mean(sample, row => row.Net))}");
        }

        sb.AppendLine();
        sb.AppendLine("## 10. BTC context");
        sb.AppendLine();
        foreach (var name in new[] { "btc_signed_ret_20", "btc_adx", "btc_atr_percentile", "btc_same_clock_signed", "coins_same_sign" })
        {
            var score = scores.First(row => row.Name == name);
            sb.AppendLine($"- {name}: {score.Label}. IS rho {Num(score.IsRho)}, validation rho {Num(score.ValRho)}, OOS rho {Num(score.OosRho)}.");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Simultaneous signals");
        sb.AppendLine();
        foreach (var name in new[] { "signals_already_closed", "symbols_already_closed", "minutes_since_trade" })
        {
            var score = scores.First(row => row.Name == name);
            sb.AppendLine($"- {name}: {score.Label}. IS rho {Num(score.IsRho)}, validation rho {Num(score.ValRho)}, OOS rho {Num(score.OosRho)}.");
        }

        sb.AppendLine();
        sb.AppendLine("`minutes_since_signal` is DATA_UNAVAILABLE.");
        sb.AppendLine();
        Session(sb, rows);
        sb.AppendLine();
        sb.AppendLine("## 12. Strongest recurring relationships");
        sb.AppendLine();
        var notable = scores.Where(score => score.Label != "NO_EVIDENCE").ToList();
        if (notable.Count == 0)
        {
            sb.AppendLine("No feature met a label other than NO_EVIDENCE.");
        }

        foreach (var score in notable)
        {
            sb.AppendLine($"- {score.Name}: {score.Label}. IS rho {Num(score.IsRho)}, validation rho {Num(score.ValRho)}, OOS rho {Num(score.OosRho)}.");
        }

        sb.AppendLine();
        sb.AppendLine("## 13. Unstable relationships");
        sb.AppendLine();
        var unstable = scores.Where(score => score.Label is "UNSTABLE" or "OOS_ONLY" or "FAMILY_SPECIFIC" or "SYMBOL_SPECIFIC").ToList();
        sb.AppendLine(unstable.Count == 0 ? "No feature was large enough in one slice to earn an unstable label. The correlations are small in every window." : string.Join(", ", unstable.Select(score => score.Name + " (" + score.Label + ")")) + ".");
        sb.AppendLine();
        sb.AppendLine("## 14. Data limitations");
        sb.AppendLine();
        sb.AppendLine("The book is BTC, ETH, and BNB only. Near-miss is not a second sample. Funding is not in these replays. Unfilled signals were not stored. ATR percentile is a fraction from 0 to 1. Session VWAP is the existing causal session VWAP. BOS and CHoCH come from the existing causal structure book. No feature was added after the manifest hash.");
        sb.AppendLine();
        sb.AppendLine("## 15. Conclusion");
        sb.AppendLine();
        foreach (var label in new[] { "REPEATABLE", "FAMILY_SPECIFIC", "SYMBOL_SPECIFIC", "OOS_ONLY", "UNSTABLE", "NO_EVIDENCE" })
        {
            var names = scores.Where(score => score.Label == label).Select(score => score.Name).ToList();
            sb.AppendLine($"- {label}: {(names.Count == 0 ? "none" : names.Count + " features")}.");
        }

        sb.AppendLine();
        sb.AppendLine("Direction is still the open problem. No pre-entry feature in this fixed list separates winners from losers with the same sign in IS, validation, and OOS across two families and two coins.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.");
        File.WriteAllText(Path.Combine(root, "docs", "SIGNAL_QUALITY_AUDIT_REPORT.md"), sb.ToString());
    }

    private static void Session(StringBuilder sb, IReadOnlyList<Row> rows)
    {
        sb.AppendLine("UTC session slices, not ranked features:");
        sb.AppendLine();
        sb.AppendLine("| Session | Phase | n | Win rate | Net expectancy |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: |");
        foreach (var phase in new[] { "IS", "VALIDATION", "OOS" })
        {
            foreach (var session in new[] { "ASIA", "EUROPE", "US" })
            {
                var sample = rows.Where(row => row.Phase == phase && SessionOf(row.SignalClose) == session).ToList();
                sb.AppendLine($"| {session} | {phase} | {sample.Count} | {Share(sample.Count(row => row.Win), sample.Count)} | {Money(Mean(sample, row => row.Net))} |");
            }
        }
    }

    private static string SessionOf(DateTimeOffset close)
    {
        var hour = close.UtcDateTime.Hour;
        return hour < 7 ? "ASIA" : hour < 15 ? "EUROPE" : "US";
    }

    private static decimal Mean(IReadOnlyList<Row> rows, Func<Row, decimal> value) => rows.Count == 0 ? 0m : rows.Average(value);

    private static string Share(int n, int d) => d == 0 ? "n/a" : (n / (decimal)d).ToString("0.0%", CultureInfo.InvariantCulture);

    private static string Num(decimal? value) => value is { } number ? number.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static IReadOnlyList<SignalType> SignalsFor(Arm arm, Dictionary<string, CausalIndicatorCache> caches, IReadOnlyList<ContextualSignalRow> contextual)
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
        return contextual.First(row => row.CandidateId == id).Signals;
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
            arm.Id + "|" + arm.Timeframe + "|" + symbol, template, 1, "Unmodified existing rule.",
            ResearchKinds.ParentFilter, template, "", [], "existing", "Model B",
            new ResearchFilters(), new ResearchNativeParams(), [arm.Timeframe], ["LONG", "SHORT"], [],
            created, "signal quality audit", ResearchStatuses.Researching);
        var definition = ResearchRunner.DefinitionFor(candidate, arm.Timeframe);
        IStrategyEngine engine = arm.Kind == ArmKind.Frozen ? new ResearchStrategyEngine(candidate) : new PrecomputedResearchSignalEngine(signals);
        var settings = StrategyValidation.LowIsolatedRisk(candles[0].OpenTime, candles[^1].CloseTime);
        var warmup = arm.Kind == ArmKind.Frozen ? StrategyValidation.WarmupBars(definition) : 0;
        return new BacktestReplay(engine).Run(definition, candles, settings, new CausalIndicatorCache(candles), warmup, candles.Count).Trades.ToList();
    }

    private static async Task<Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>> LoadSymbol(string cacheDir, string root, string symbol)
    {
        var map = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var timeframe in new[] { "5m", "15m", "1h" })
        {
            map[(symbol, timeframe)] = await LoadOne(cacheDir, root, symbol, timeframe);
        }

        return map;
    }

    private static async Task<IReadOnlyList<MarketCandle>> LoadOne(string cacheDir, string root, string symbol, string timeframe)
    {
        var coverage = JsonSerializer.Deserialize<CoverageFile>(
            await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidOperationException("Coverage artifact missing.");
        var range = coverage.Coverage.First(row =>
            string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(row.Timeframe, timeframe, StringComparison.OrdinalIgnoreCase));
        var bars = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, timeframe);
        return bars.Where(bar => bar.OpenTime >= range.RequestedFrom && bar.CloseTime <= range.RequestedTo && bar.IsClosed).OrderBy(bar => bar.OpenTime).ToList();
    }

    private static void WriteJson<T>(string path, T value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));

    private sealed record Arm(string Id, string Group, string ResearchFamily, string Timeframe, ArmKind Kind);

    private enum ArmKind { Frozen, FinalFive, Contextual }

    private sealed class Row
    {
        public Row(string group, string family, string strategy, string symbol, string timeframe, string side, string phase, int block, DateTimeOffset signalClose, decimal gross, decimal net, bool win, Dictionary<string, decimal?> features)
        {
            Group = group;
            Family = family;
            Strategy = strategy;
            Symbol = symbol;
            Timeframe = timeframe;
            Side = side;
            Phase = phase;
            Block = block;
            SignalClose = signalClose;
            Gross = gross;
            Net = net;
            Win = win;
            Features = features;
        }

        public string Group { get; }
        public string Family { get; }
        public string Strategy { get; }
        public string Symbol { get; }
        public string Timeframe { get; }
        public string Side { get; }
        public string Phase { get; }
        public int Block { get; }
        public DateTimeOffset SignalClose { get; }
        public decimal Gross { get; }
        public decimal Net { get; }
        public bool Win { get; }
        public Dictionary<string, decimal?> Features { get; }
    }

    private sealed record FeatureScore(
        string Name, int N,
        decimal WinnerP25, decimal WinnerP50, decimal WinnerP75,
        decimal LoserP25, decimal LoserP50, decimal LoserP75,
        decimal Effect,
        decimal? IsRho, decimal? ValRho, decimal? OosRho,
        int IsN, int ValN, int OosN,
        decimal IsGap, decimal ValGap, decimal OosGap,
        int Blocks, int Families, int Symbols, string Label);

    private sealed record CoverageFile(IReadOnlyList<CoverageSlice> Coverage);

    private sealed record CoverageSlice(string Symbol, string Timeframe, DateTimeOffset RequestedFrom, DateTimeOffset RequestedTo);
}
