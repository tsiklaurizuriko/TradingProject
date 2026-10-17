using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.StrategyResearch;

public static class ProductionNewResearch
{
    public static int Run(string root) => ProductionNewUniverse.Run(root);

    public static int RunSingle(string root)
    {
        Console.WriteLine("production-new report");
        Console.WriteLine("LiveEnabled default: false. PaperEnabled default: false.");
        var cache = Path.Combine(root, "artifacts", "strategy-validation-cache");
        Console.WriteLine($"Cache directory: {cache}");
        Console.WriteLine($"Cache present: {Directory.Exists(cache)}");
        var settings = ProductionNewBacktest.Settings();
        Console.WriteLine(
            $"Costs: fee {settings.FeePercent}% per side, slippage {settings.SlippagePercent}% per side, risk {settings.RiskPercent}%, leverage {settings.Leverage}. " +
            "Signals use the closed bar. Entries fill at the next bar open. Funding settlements were not loaded, so funding cashflow is zero and the cost note stays EXCLUDING_FUNDING.");
        Console.WriteLine("Parameters are the documented defaults. They were not fitted on the final 20%.");
        Console.WriteLine("Each line is one coin. A one-coin result is not a portfolio validation, and listing survivorship is not corrected.");

        foreach (var timeframe in new[] { "1h", "15m" })
        {
            var path = Path.Combine(cache, $"BTCUSDT_{timeframe}.json");
            var candles = Load(path);
            if (candles.Count == 0)
            {
                Console.WriteLine($"BTCUSDT {timeframe}: no closed bars at {path}");
                continue;
            }

            Console.WriteLine($"BTCUSDT {timeframe}: {candles.Count} closed bars {candles[0].OpenTime:yyyy-MM-dd} .. {candles[^1].CloseTime:yyyy-MM-dd}");
            foreach (var id in StrategyTemplateKeys.ProductionNew)
            {
                var run = ProductionNewBacktest.Run(id, candles, historicalUntouchedSample: true, symbol: "BTCUSDT", timeframe: timeframe);
                Print(run);
            }

            if (timeframe == "1h")
            {
                WalkForward(candles, timeframe);
            }
        }

        var five = Path.Combine(cache, "BTCUSDT_5m.json");
        Console.WriteLine(File.Exists(five)
            ? $"BTCUSDT 5m cache exists ({new FileInfo(five).Length} bytes) and was not replayed by this command."
            : "BTCUSDT 5m cache is absent.");
        Console.WriteLine("momentum-relative-strength-new has no peer book in this replay, so it stays NO_TRADE.");
        Console.WriteLine("funding-basis-new has no historical funding, mark, or index series here, so it stays NO_TRADE. Missing funding is not treated as zero.");
        Console.WriteLine("market-regime-new is a filter and does not open trades.");
        Console.WriteLine("Synthetic smoke is not a backtest of Binance history and is not a PASS.");
        return 0;
    }

    private static void Print(ProductionNewRun run)
    {
        Console.WriteLine($"{run.StrategyId} | {StrategyTemplates.DisplayName(run.StrategyId)} | series-verdict={run.Verdict} | {run.Notes}");
        if (run.Verdict == ProductionNewAcceptance.Pass)
        {
            Console.WriteLine("  The single-series rule matched. That is not a portfolio validation and it does not authorize live trading.");
        }
        WriteSlice("in-sample", run.InSample);
        WriteSlice("out-of-sample", run.OutOfSample);
        Console.WriteLine($"  walk-forward-windows-counted={run.WalkForwardWindows} (count only on this line; 1h test slices are printed separately)");
    }

    private static void WriteSlice(string label, ReplayResult? result)
    {
        if (result is null)
        {
            Console.WriteLine($"  {label}: not run");
            return;
        }

        var expectancy = result.NumberOfTrades == 0 ? 0m : result.NetProfit / result.NumberOfTrades;
        Console.WriteLine(
            $"  {label}: bars={result.BarsUsed} {result.WindowStart:yyyy-MM-dd}..{result.WindowEnd:yyyy-MM-dd} trades={result.NumberOfTrades} " +
            $"net={result.NetProfit:0.####} gross={result.GrossPnl:0.####} return%={result.ReturnPercent:0.####} maxDD%={result.MaximumDrawdown:0.####} " +
            $"pf={result.ProfitFactor:0.####} win%={result.WinRate:0.####} avgWin={result.AverageWin:0.####} avgLoss={result.AverageLoss:0.####} expectancy={expectancy:0.####} " +
            $"fees={result.FeesPaid:0.####} slippage={result.SlippagePaid:0.####} funding={result.FundingPaid:0.####} " +
            $"long={result.Long.Trades}/{result.Long.NetPnL:0.####} short={result.Short.Trades}/{result.Short.NetPnL:0.####} costs={result.CostNotes}");
    }

    private static void WalkForward(IReadOnlyList<MarketCandle> candles, string timeframe)
    {
        var train = Math.Max(40, candles.Count / 5);
        var test = Math.Max(20, candles.Count / 10);
        var step = Math.Max(20, candles.Count / 10);
        var windows = StrategyValidation.WalkForwardWindows(candles.Count, train, test, step);
        var replay = new BacktestReplay(new StrategyEngine());
        var ids = new[]
        {
            StrategyTemplateKeys.TrendFollowingNew,
            StrategyTemplateKeys.BreakoutVolumeNew,
            StrategyTemplateKeys.MeanReversionRsiBollingerNew
        };
        foreach (var id in ids)
        {
            var definition = StrategyValidation.Definition(id, timeframe);
            definition.Symbol = "BTCUSDT";
            Console.WriteLine($"walk-forward {id} {timeframe} windows={windows.Count} train={train} test={test} step={step}");
            foreach (var (start, length) in windows)
            {
                var testStart = start + train;
                var testEnd = Math.Min(candles.Count, start + length);
                if (testStart >= testEnd)
                {
                    continue;
                }

                var result = replay.Run(
                    definition,
                    candles,
                    ProductionNewBacktest.Settings(),
                    evaluateFromInclusive: testStart,
                    evaluateToExclusive: testEnd);
                Console.WriteLine(
                    $"  {candles[testStart].OpenTime:yyyy-MM-dd}..{candles[testEnd - 1].CloseTime:yyyy-MM-dd} trades={result.NumberOfTrades} net={result.NetProfit:0.####} pf={result.ProfitFactor:0.####} maxDD%={result.MaximumDrawdown:0.####} long={result.Long.Trades} short={result.Short.Trades}");
            }
        }
    }

    private static List<MarketCandle> Load(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        var bars = JsonSerializer.Deserialize<List<CachedBar>>(File.ReadAllText(path));
        return bars?.Select(bar => new MarketCandle
        {
            OpenTime = bar.OpenTime,
            CloseTime = bar.CloseTime,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume,
            IsClosed = true
        }).OrderBy(bar => bar.OpenTime).ToList() ?? [];
    }

    private sealed record CachedBar(
        DateTimeOffset OpenTime,
        DateTimeOffset CloseTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume);
}
