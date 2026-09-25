using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

internal static class ImportedRuleMeasure
{
    public static async Task<int> RunAsync(string cacheDir)
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformImportedRules/1.0");
        var end = DateTimeOffset.UtcNow;
        var intradayFrom = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var dailyFrom = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var symbols = new[] { "BTCUSDT", "ETHUSDT", "SOLUSDT" };
        var replay = new BacktestReplay(new StrategyEngine());

        Console.WriteLine("Imported rules. BookStopsOff. Fee 0.04% + slippage 0.02%. Risk 1% / 5x / $10,000. No funding.");
        Console.WriteLine("MAC 5m and ZigZag 30m from 2024-01-01. Donchian 1d from 2020-01-01.");

        foreach (var symbol in symbols)
        {
            await RunOne(http, cacheDir, replay, symbol, "5m", intradayFrom, end, Mac(symbol));
            await RunOne(http, cacheDir, replay, symbol, "30m", intradayFrom, end, ZigZag(symbol));
            await RunOne(http, cacheDir, replay, symbol, "1d", dailyFrom, end, Donchian());
        }

        return 0;
    }

    private static StrategyTemplateParams Mac(string symbol) =>
        StrategyTemplates.DefaultsFor(StrategyTemplateKeys.MacContrarian710, false);

    private static StrategyTemplateParams ZigZag(string symbol)
    {
        var (deviation, atrMultiple) = symbol switch
        {
            "ETHUSDT" => (6m, 1.5m),
            "SOLUSDT" => (5m, 2.5m),
            _ => (2m, 1.5m)
        };
        return StrategyTemplates.DefaultsFor(StrategyTemplateKeys.ZigZagFade, false) with
        {
            PriceChangeThreshold = deviation,
            AtrStopMultiplier = atrMultiple
        };
    }

    private static StrategyTemplateParams Donchian() =>
        StrategyTemplates.DefaultsFor(StrategyTemplateKeys.DonchianV2, false);

    private static async Task RunOne(
        HttpClient http,
        string cacheDir,
        BacktestReplay replay,
        string symbol,
        string timeframe,
        DateTimeOffset from,
        DateTimeOffset to,
        StrategyTemplateParams parameters)
    {
        Console.WriteLine($"loading {symbol} {timeframe}...");
        var (candles, _, _) = await ResearchKlineCache.LoadAsync(http, cacheDir, symbol, timeframe, from, to);
        var closed = candles.Where(c => c.IsClosed && c.OpenTime >= from && c.CloseTime <= to).OrderBy(c => c.OpenTime).ToList();
        if (closed.Count < 100)
        {
            Console.WriteLine($"{parameters.TemplateKey} {symbol} {timeframe} insufficient bars {closed.Count}");
            return;
        }

        var definition = new StrategyDefinitionValidator().Parse(StrategyTemplates.Build(parameters.TemplateKey, 1, parameters));
        var settings = StrategyValidation.FrozenRisk(closed[0].OpenTime, closed[^1].CloseTime) with
        {
            HonorSuggestedStops = true,
            BookStopsOff = true,
            CooldownMinutes = 0,
            MaxConsecutiveLosses = 100_000,
            MaxDailyLossPercent = 100m
        };
        var result = replay.Run(definition, closed, settings, new CausalIndicatorCache(closed));
        var first = closed[0].Close;
        var last = closed[^1].Close;
        var buyHold = first > 0m ? (last / first - 1m) * 100m : 0m;
        var sharpe = result.SharpeRatio?.ToString("0.00") ?? "n/a";
        var reasons = string.Join(", ", result.Trades.GroupBy(t => t.Reason).Select(g => $"{g.Key}:{g.Count()}"));
        Console.WriteLine(
            $"{parameters.TemplateKey,-22} {symbol,-8} {timeframe,-3} bars={closed.Count,6} trades={result.NumberOfTrades,4} L={result.Long.Trades,4} S={result.Short.Trades,4} win={result.WinRate,6:0.0}% PF={result.ProfitFactor,6:0.00} sharpe={sharpe,6} net={result.ReturnPercent,7:0.00}% DD={result.MaximumDrawdown,6:0.0}% BH={buyHold,7:0.0}% band={parameters.PriceChangeThreshold} atr={parameters.AtrStopMultiplier} [{reasons}]");
    }
}
