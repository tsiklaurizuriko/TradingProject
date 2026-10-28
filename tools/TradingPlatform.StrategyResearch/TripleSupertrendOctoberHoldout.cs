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
/// Frozen temporal holdout of the catalog Triple Supertrend book on the complete October 2026 15m archive.
/// The rule and risk parameters are taken from the published catalog; this runner does not search parameters.
/// </summary>
internal static class TripleSupertrendOctoberHoldout
{
    private static readonly DateTimeOffset HoldoutStart = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private const decimal Capital = 10_000m;
    private const decimal RiskPercent = 0.5m;
    private const int PortfolioSlots = 5;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string root)
    {
        var historicalDir = Path.Combine(root, "artifacts", "strategy-validation-cache");
        var recentDir = Path.Combine(root, "artifacts", "research", "audit", "market-data-oos-2026-10");
        var fundingDir = Path.Combine(root, "artifacts", "research", "audit", "funding-oos-2026-10");
        var output = Path.Combine(root, "artifacts", "research", "audit", "triple-supertrend-october-holdout-2026-10.md");
        var symbols = Directory.GetFiles(recentDir, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(symbol => File.Exists(Path.Combine(historicalDir, symbol + "_1h.json")))
            .OrderBy(symbol => symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var all = new Dictionary<double, List<(string Symbol, ReplayTrade Trade)>>
        {
            [1.0] = [], [1.5] = [], [2.0] = []
        };
        var barsUsed = 0;
        var noFunding = new List<string>();
        var failed = new List<string>();
        var validator = new StrategyDefinitionValidator();
        var replay = new BacktestReplay(new StrategyEngine());

        Console.WriteLine($"Triple Supertrend October holdout: {symbols.Length} symbols, catalog parameters frozen.");
        foreach (var (symbol, index) in symbols.Select((symbol, index) => (symbol, index + 1)))
        {
            try
            {
                var historical = ReadCandles(Path.Combine(historicalDir, symbol + "_1h.json"));
                var recent = ReadRecentHourly(Path.Combine(recentDir, symbol + ".json"));
                var candles = historical.Concat(recent)
                    .GroupBy(candle => candle.OpenTime)
                    .Select(group => group.Last())
                    .OrderBy(candle => candle.OpenTime)
                    .ToList();
                var from = candles.FindIndex(candle => candle.OpenTime >= HoldoutStart);
                if (from < 500 || candles.Count - from < 24)
                {
                    failed.Add($"{symbol}: insufficient hourly history or October coverage ({candles.Count - Math.Max(from, 0)} holdout bars)");
                    continue;
                }

                var fundingPath = Path.Combine(fundingDir, symbol + ".json");
                if (!File.Exists(fundingPath))
                {
                    noFunding.Add(symbol);
                    continue;
                }
                var funding = ReadFunding(fundingPath);
                if (funding.Count == 0)
                {
                    noFunding.Add(symbol);
                    continue;
                }
                barsUsed += candles.Count - from;
                var parameters = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.TripleSupertrend, qualityOn: false) with
                {
                    TemplateKey = StrategyTemplateKeys.TripleSupertrend,
                    AllowedSide = StrategySides.Both,
                    Timeframe = "1h"
                };
                var definition = validator.Parse(StrategyTemplates.Build(
                    StrategyTemplates.DisplayName(StrategyTemplateKeys.TripleSupertrend), 1, parameters));
                var cache = new CausalIndicatorCache(candles);
                foreach (var multiplier in all.Keys)
                {
                    var settings = new ReplaySettings(
                        HoldoutStart,
                        candles[^1].CloseTime,
                        Capital,
                        RiskPercent,
                        1m,
                        0.10m * (decimal)multiplier,
                        0.05m * (decimal)multiplier,
                        8m,
                        10m,
                        MaxDailyLossPercent: 0m,
                        MaxPortfolioRiskPercent: 4m,
                        MaxSimultaneousPositions: 1,
                        MaxConsecutiveLosses: 5,
                        CooldownMinutes: 30,
                        MinimumLiquidationSafetyBufferPercent: 1m);
                    var result = replay.Run(
                        definition,
                        candles,
                        settings,
                        cache,
                        from,
                        candles.Count,
                        fundingSettlements: funding.Count > 0 ? funding : null,
                        symbol: symbol);
                    all[multiplier].AddRange(result.Trades.Select(trade => (symbol, trade)));
                }

                if (index % 50 == 0 || index == symbols.Length)
                {
                    Console.WriteLine($"  {index}/{symbols.Length} symbols; bars={barsUsed:N0}; raw trades={all[1.0].Count:N0}");
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{symbol}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Triple Supertrend: October temporal holdout");
        sb.AppendLine();
        sb.AppendLine("Frozen catalog `triple_supertrend` rule, 1h, both sides. The candidate was selected before opening this October strategy-specific holdout from the prior short catalog replay; no parameters or coin filters were fitted here. Signals use the canonical `StrategyEngine` and `BacktestReplay`, next-hour-open execution, a 0.5% risk book, 8% stop, 10% take, 1x leverage, one position per symbol, and funding settlements where available.");
        sb.AppendLine();
        sb.AppendLine($"Holdout starts {HoldoutStart:yyyy-MM-dd HH:mm} UTC. Current-listing symbols with a local 1h history: {symbols.Length}. October hourly bars replayed: {barsUsed:N0}. Symbols excluded for missing funding rows: {noFunding.Count}. Missing or insufficient candle data: {failed.Count}.");
        sb.AppendLine();
        sb.AppendLine("Fees and slippage are charged per side (base 0.10% fee + 0.05% slippage; stressed at 1.5x and 2x), with real funding rates applied at settlement. A deterministic 5-slot overlay admits trades by open time, then symbol; only one position per symbol. The overlay is an account-capacity diagnostic; each symbol's fills were generated by its isolated replay book, so reported dollar PnL is a fixed-initial-capital approximation, not compounding across a single shared account.");
        sb.AppendLine();
        sb.AppendLine("| Cost multiplier | Raw trades | Capped trades | Net PnL (USDT) | Mean net / trade | PF | Win rate | Max closed-trade DD | Symbols traded | Positive-symbol share | Top 5 winner share | Funding PnL (USDT) |");
        sb.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var multiplier in all.Keys.OrderBy(value => value))
        {
            var raw = all[multiplier].OrderBy(row => row.Trade.OpenedAt).ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase).ToList();
            var capped = Cap(raw, PortfolioSlots);
            var metrics = Measure(capped);
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "| {0:0.0}x | {1} | {2} | {3:0.00} | {4:P3} | {5:0.000} | {6:P1} | {7:P2} | {8} | {9:P0} | {10:P0} | {11:0.00} |",
                multiplier, raw.Count, capped.Count, metrics.Net, metrics.MeanReturn, metrics.Pf, metrics.WinRate, metrics.MaxDrawdown,
                capped.Select(row => row.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count(), metrics.PositiveSymbolShare, metrics.Top5WinnerShare, metrics.Funding));
        }

        var baseCapped = Cap(all[1.0].OrderBy(row => row.Trade.OpenedAt).ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase).ToList(), PortfolioSlots);
        var baseMetrics = Measure(baseCapped);
        var stressCapped = Cap(all[1.5].OrderBy(row => row.Trade.OpenedAt).ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase).ToList(), PortfolioSlots);
        var stressMetrics = Measure(stressCapped);
        var verdict = baseCapped.Count >= 100
            && baseMetrics.Pf >= 1.15m
            && stressMetrics.Pf >= 1m
            && baseMetrics.Net > 0m
            && baseCapped.Select(row => row.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 8
            && baseMetrics.Top5WinnerShare <= 0.5m
            ? "RESEARCH GATE PASSED — eligible for a longer untouched paper study only."
            : "REJECTED — this frozen October result does not meet the research gate.";
        sb.AppendLine();
        sb.AppendLine("## Verdict");
        sb.AppendLine();
        sb.AppendLine(verdict);
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "Base PF {0:0.000}; 1.5x PF {1:0.000}; capped trades {2}; net {3:0.00} USDT.",
            baseMetrics.Pf, stressMetrics.Pf, baseCapped.Count, baseMetrics.Net));
        sb.AppendLine();
        sb.AppendLine("## Coverage and limitations");
        sb.AppendLine();
        sb.AppendLine($"The cost multiplier 1.0 sample has {baseCapped.Count(row => row.Trade.Side == "Long")} longs and {baseCapped.Count(row => row.Trade.Side == "Short")} shorts. It is current listings only (survivorship risk), a short temporal sample, and has no order-book fill/slippage model. This can reject the frozen rule or justify a longer paper test; it cannot by itself establish deployable profitability.");
        if (failed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Excluded symbols/data problems:");
            foreach (var row in failed.Take(100)) sb.AppendLine("- " + row);
        }

        sb.AppendLine();
        sb.AppendLine("The strategy-level research gate used by this study is at least 100 capped trades, PF >= 1.15 at base costs and PF >= 1.00 at 1.5x costs, at least 8 symbols, positive mean net, and top-5 winners contributing no more than half of gross gains. Passing is only a research candidate; an untouched forward paper record is still required.");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, sb.ToString());
        Console.WriteLine("Wrote " + output);
        return 0;
    }

    private static List<(string Symbol, ReplayTrade Trade)> Cap(IReadOnlyList<(string Symbol, ReplayTrade Trade)> trades, int slots)
    {
        var open = new List<(string Symbol, ReplayTrade Trade)>();
        var kept = new List<(string Symbol, ReplayTrade Trade)>();
        foreach (var row in trades)
        {
            open.RemoveAll(item => item.Trade.ClosedAt <= row.Trade.OpenedAt);
            if (open.Count >= slots || open.Any(item => string.Equals(item.Symbol, row.Symbol, StringComparison.OrdinalIgnoreCase))) continue;
            open.Add(row);
            kept.Add(row);
        }

        return kept;
    }

    private static (decimal Net, decimal MeanReturn, decimal Pf, decimal WinRate, decimal MaxDrawdown, decimal PositiveSymbolShare, decimal Top5WinnerShare, decimal Funding) Measure(IReadOnlyList<(string Symbol, ReplayTrade Trade)> rows)
    {
        if (rows.Count == 0) return (0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
        var trades = rows.Select(row => row.Trade).ToList();
        var net = trades.Sum(trade => trade.PnL);
        var wins = trades.Where(trade => trade.PnL > 0m).Sum(trade => trade.PnL);
        var losses = Math.Abs(trades.Where(trade => trade.PnL < 0m).Sum(trade => trade.PnL));
        var symbolGroups = rows.GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase).ToList();
        var positiveSymbols = symbolGroups.Count(group => group.Sum(row => row.Trade.PnL) > 0m);
        var grossGains = trades.Where(trade => trade.PnL > 0m).Sum(trade => trade.PnL);
        var top5Gains = trades.Where(trade => trade.PnL > 0m).OrderByDescending(trade => trade.PnL).Take(5).Sum(trade => trade.PnL);
        var equity = Capital;
        var peak = equity;
        var drawdown = 0m;
        foreach (var trade in trades.OrderBy(trade => trade.ClosedAt))
        {
            equity += trade.PnL;
            peak = Math.Max(peak, equity);
            if (peak > 0m) drawdown = Math.Max(drawdown, (peak - equity) / peak);
        }

        return (net, net / trades.Count / Capital, losses == 0m ? 99m : wins / losses,
            trades.Count(trade => trade.PnL > 0m) / (decimal)trades.Count, drawdown,
            positiveSymbols / (decimal)symbolGroups.Count, grossGains == 0m ? 0m : top5Gains / grossGains,
            trades.Sum(trade => trade.FundingPnl));
    }

    private static List<MarketCandle> ReadCandles(string path)
    {
        var bars = JsonSerializer.Deserialize<List<CachedBar>>(File.ReadAllText(path), JsonOptions) ?? [];
        return bars.Select(bar => new MarketCandle
        {
            OpenTime = bar.OpenTime,
            CloseTime = bar.CloseTime,
            Open = bar.Open,
            High = bar.High,
            Low = bar.Low,
            Close = bar.Close,
            Volume = bar.Volume,
            TakerBuyVolume = bar.TakerBuyVolume,
            Timeframe = Timeframe.OneHour,
            IsClosed = true,
            ExchangeTimestamp = bar.CloseTime
        }).ToList();
    }

    private static List<MarketCandle> ReadRecentHourly(string path)
    {
        var bars = JsonSerializer.Deserialize<List<RecentBar>>(File.ReadAllText(path), JsonOptions) ?? [];
        var output = new List<MarketCandle>();
        foreach (var group in bars.GroupBy(bar => bar.OpenTime.ToUnixTimeMilliseconds() / 3_600_000 * 3_600_000).OrderBy(group => group.Key))
        {
            var hour = DateTimeOffset.FromUnixTimeMilliseconds(group.Key);
            var rows = group.OrderBy(bar => bar.OpenTime).ToList();
            if (rows.Count != 4 || rows[0].OpenTime != hour || rows[1].OpenTime != hour.AddMinutes(15)
                || rows[2].OpenTime != hour.AddMinutes(30) || rows[3].OpenTime != hour.AddMinutes(45)) continue;
            output.Add(new MarketCandle
            {
                OpenTime = hour,
                CloseTime = hour.AddHours(1).AddMilliseconds(-1),
                Open = rows[0].Open,
                High = rows.Max(row => row.High),
                Low = rows.Min(row => row.Low),
                Close = rows[^1].Close,
                Volume = rows.Sum(row => row.Volume),
                Timeframe = Timeframe.OneHour,
                IsClosed = true,
                ExchangeTimestamp = hour.AddHours(1).AddMilliseconds(-1)
            });
        }

        return output;
    }

    private static List<ReplayFundingSettlement> ReadFunding(string path)
    {
        var rows = JsonSerializer.Deserialize<List<FundingRow>>(File.ReadAllText(path), JsonOptions) ?? [];
        return rows.OrderBy(row => row.FundingTime)
            .Select(row => new ReplayFundingSettlement(DateTimeOffset.FromUnixTimeMilliseconds(row.FundingTime), row.FundingRate))
            .ToList();
    }

    private sealed class CachedBar
    {
        public DateTimeOffset OpenTime { get; set; }
        public DateTimeOffset CloseTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
        public decimal TakerBuyVolume { get; set; }
    }

    private sealed class RecentBar
    {
        public DateTimeOffset OpenTime { get; set; }
        public decimal Open { get; set; }
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal Volume { get; set; }
    }

    private sealed class FundingRow
    {
        public long FundingTime { get; set; }
        public decimal FundingRate { get; set; }
    }
}
