using System.Diagnostics;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Backtesting.Validation;

public sealed record BenchmarkRow(
    string TemplateKey,
    int Candles,
    int Trades,
    decimal NetProfit,
    decimal ProfitFactor,
    decimal MaximumDrawdown,
    long ElapsedMs,
    double CandlesPerSecond);

public sealed record BenchmarkReport(
    int CandleCount,
    long ElapsedMs,
    long PeakWorkingSetBytes,
    int Gen0,
    int Gen1,
    int Gen2,
    IReadOnlyList<BenchmarkRow> Rows,
    string Fingerprint);

public static class ValidationBenchmark
{
    public const int DefaultBars = 8_000;
    public const int Seed = 20260918;

    public static BenchmarkReport Run(int bars = DefaultBars, TextWriter? output = null)
    {
        var writer = output ?? TextWriter.Null;
        var candles = CreateDeterministicSeries(bars);
        writer.WriteLine($"BENCHMARK dataset: {bars} closed synthetic bars, seed {Seed}, no Binance, no database.");
        var cache = new CausalIndicatorCache(candles);

        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var startWs = process.WorkingSet64;
        var sw = Stopwatch.StartNew();
        var rows = new List<BenchmarkRow>();

        foreach (var key in StrategyTemplateKeys.Frozen)
        {
            var definition = StrategyValidation.Definition(key, "1h");
            var one = Stopwatch.StartNew();
            var result = StrategyValidation.Run(definition, candles, cache);
            one.Stop();
            var cps = one.Elapsed.TotalSeconds <= 0 ? 0 : bars / one.Elapsed.TotalSeconds;
            rows.Add(new BenchmarkRow(
                key,
                result.BarsUsed,
                result.NumberOfTrades,
                result.NetProfit,
                result.ProfitFactor,
                result.MaximumDrawdown,
                one.ElapsedMilliseconds,
                Math.Round(cps, 1)));
            writer.WriteLine(
                $"{key}: {one.ElapsedMilliseconds} ms, {result.BarsUsed} bars, {result.NumberOfTrades} trades, net {result.NetProfit:0.00}, PF {result.ProfitFactor:0.00}, {cps:0} bars/s");
        }

        sw.Stop();
        process.Refresh();
        var report = new BenchmarkReport(
            bars,
            sw.ElapsedMilliseconds,
            Math.Max(process.WorkingSet64, startWs),
            GC.CollectionCount(0) - gen0,
            GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2,
            rows,
            Fingerprint(rows));
        writer.WriteLine($"TOTAL {report.ElapsedMs} ms | peak WS {report.PeakWorkingSetBytes / (1024 * 1024)} MB | GC gen0={report.Gen0} gen1={report.Gen1} gen2={report.Gen2}");
        writer.WriteLine($"FINGERPRINT {report.Fingerprint}");
        return report;
    }

    public static IReadOnlyList<MarketCandle> CreateDeterministicSeries(int count)
    {
        var rng = new Random(Seed);
        var candles = new List<MarketCandle>(count);
        decimal price = 100m;
        var t0 = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < count; i++)
        {
            var drift = (decimal)(rng.NextDouble() - 0.48) * 1.4m;
            var open = price;
            var close = Math.Max(1m, price + drift);
            var high = Math.Max(open, close) + (decimal)rng.NextDouble() * 0.4m;
            var low = Math.Max(0.5m, Math.Min(open, close) - (decimal)rng.NextDouble() * 0.4m);
            candles.Add(new MarketCandle
            {
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = 50m + (decimal)rng.NextDouble() * 200m,
                IsClosed = true,
                OpenTime = t0.AddHours(i),
                CloseTime = t0.AddHours(i + 1),
                ExchangeTimestamp = t0.AddHours(i + 1)
            });
            price = close;
        }

        return candles;
    }

    private static string Fingerprint(IReadOnlyList<BenchmarkRow> rows) =>
        string.Join("|", rows.Select(r => $"{r.TemplateKey}:{r.Trades}:{r.NetProfit:0.0000}:{r.ProfitFactor:0.0000}:{r.MaximumDrawdown:0.0000}"));
}
