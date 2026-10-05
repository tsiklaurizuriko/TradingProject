using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Stopwatch measurements of the replay hot paths on synthetic 15m data. A doubling of bars that roughly doubles
/// time is linear; roughly four times is quadratic.
/// </summary>
internal static class PerfHarness
{
    private static readonly DateTimeOffset Start = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string repoRoot)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.AppendLine("# Replay performance");
        text.AppendLine();
        text.AppendLine($"Measured {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC, {Environment.ProcessorCount} logical cores, synthetic 15m random walk, best of 3.");
        text.AppendLine();
        text.AppendLine("| Path | bars | ms | ms per 1k bars |");
        text.AppendLine("|---|---:|---:|---:|");
        var template = new StrategyDefinitionValidator().Parse(StrategyTemplates.Build(
            "perf", 1, StrategyTemplates.DefaultsFor(StrategyTemplateKeys.EmaRsiTrend, true) with { TemplateKey = StrategyTemplateKeys.EmaRsiTrend, Timeframe = "15m" }));
        var rule = new StrategyDefinition
        {
            Name = "perf-rule",
            Timeframe = "15m",
            Entry = new ConditionGroup { Conditions = [new ConditionNode { Indicator = "RSI", Period = 14, Comparison = ComparisonKind.LessThan, Value = JsonDocument.Parse("30").RootElement }] },
            Exit = new ConditionGroup { Conditions = [new ConditionNode { Indicator = "RSI", Period = 14, Comparison = ComparisonKind.GreaterThan, Value = JsonDocument.Parse("70").RootElement }] }
        };
        foreach (var bars in new[] { 4_000, 8_000, 16_000, 32_000 })
        {
            Row(text, "template ema_rsi_trend (shared cache)", bars, Time(() => Replay(template, Walk(bars))));
        }

        foreach (var bars in new[] { 1_000, 2_000, 4_000 })
        {
            Row(text, "rule RSI<30 / RSI>70, prefix oracle (before)", bars, Time(() => Replay(rule, Walk(bars), new PrefixEngine())));
        }

        foreach (var bars in new[] { 1_000, 2_000, 4_000, 8_000, 16_000, 32_000 })
        {
            Row(text, "rule RSI<30 / RSI>70, indexed (after)", bars, Time(() => Replay(rule, Walk(bars))));
        }

        var htf = Walk(8_760, minutes: 60);
        var signals = Walk(35_040).Select(bar => bar.CloseTime).ToList();
        Row(text, "HTF lookup, backward scan (before)", signals.Count, Time(() => signals.Sum(t => (long)Scan(htf, t))));
        Row(text, "HTF lookup, binary search (after)", signals.Count, Time(() => signals.Sum(t => (long)AlphaIndicatorSeries.LastCompletedHigherTimeframe(htf, t))));
        var path = Path.Combine(repoRoot, "artifacts", "research", "perf.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ToString());
        Console.WriteLine(text);
        return 0;
    }

    private sealed class PrefixEngine : IStrategyEngine
    {
        public SignalType Evaluate(StrategyDefinition definition, StrategyContext context, out string reason) =>
            new StrategyEngine().Evaluate(definition, context, out reason);
    }

    private static void Replay(StrategyDefinition definition, List<MarketCandle> candles, IStrategyEngine? engine = null) =>
        new BacktestReplay(engine ?? new StrategyEngine()).Run(
            definition,
            candles,
            new ReplaySettings(candles[0].OpenTime, candles[^1].CloseTime, 10_000m, 1m, 1m, 0.05m, 0.02m, 2m, 4m));

    private static double Time(Action action)
    {
        var best = double.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var watch = Stopwatch.StartNew();
            action();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    private static void Row(StringBuilder text, string label, int bars, double ms) =>
        text.AppendLine($"| {label} | {bars} | {ms:0} | {ms / bars * 1000:0.0} |");

    private static int Scan(IReadOnlyList<MarketCandle> rows, DateTimeOffset t)
    {
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].IsClosed && rows[i].CloseTime <= t)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<MarketCandle> Walk(int count, int minutes = 15)
    {
        var random = new Random(11);
        var price = 100m;
        var rows = new List<MarketCandle>(count);
        for (var i = 0; i < count; i++)
        {
            var open = price;
            price = Math.Max(1m, price * (1m + (decimal)(random.NextDouble() - 0.5) * 0.01m));
            rows.Add(new MarketCandle
            {
                OpenTime = Start.AddMinutes(i * minutes),
                CloseTime = Start.AddMinutes((i + 1) * minutes),
                Open = open,
                High = Math.Max(open, price) * 1.002m,
                Low = Math.Min(open, price) * 0.998m,
                Close = price,
                Volume = 100m + random.Next(0, 100),
                TakerBuyVolume = 50m,
                IsClosed = true
            });
        }

        return rows;
    }
}
