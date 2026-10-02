using System.Globalization;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Paper replay of every operator-catalog strategy on its published book.
/// One $10,000 book per strategy, across the USD-M universe, with that strategy's position cap.
/// </summary>
internal static class CatalogDailyReplay
{
    private const decimal Capital = 10_000m;
    private static readonly HashSet<string> BtcOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        StrategyTemplateKeys.VolSpikeEmaTrend,
        StrategyTemplateKeys.Bb202Break,
        StrategyTemplateKeys.TsMomentum285,
        StrategyTemplateKeys.BtcDailyMax10
    };

    private static readonly Dictionary<string, (decimal Risk, decimal Stop, decimal Take, decimal Leverage, int Positions)> Books = new(StringComparer.OrdinalIgnoreCase)
    {
        [StrategyTemplateKeys.EmaRsiTrend] = (0.5m, 3m, 9m, 2m, 3),
        [StrategyTemplateKeys.RsiPullback] = (0.5m, 2.5m, 5m, 2m, 3),
        [StrategyTemplateKeys.BollingerReversion] = (0.5m, 2.5m, 2m, 2m, 3),
        [StrategyTemplateKeys.SupertrendEmaTrend] = (0.5m, 3m, 9m, 2m, 3),
        [StrategyTemplateKeys.LiqSweepContinuation] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.VolSqueezeStructure] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.VwapBreakoutVolume] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.MarketStructureTrend] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.VolSpikeEmaTrend] = (0.5m, 2.5m, 5m, 3m, 1),
        [StrategyTemplateKeys.Bb202Break] = (0.5m, 4m, 5m, 3m, 1),
        [StrategyTemplateKeys.BtcEma20Ema50Long] = (0.5m, 1m, 3m, 3m, 1),
        [StrategyTemplateKeys.TsMomentum285] = (2m, 8m, 30m, 1m, 1),
        [StrategyTemplateKeys.BtcDailyMax10] = (2m, 8m, 30m, 1m, 1),
        [StrategyTemplateKeys.FlowZone] = (0.5m, 4m, 15m, 1m, 5),
        [StrategyTemplateKeys.SqueezeWatch] = (0.5m, 4m, 8m, 2m, 3),
        [StrategyTemplateKeys.ImpulseCatch] = (0.5m, 6m, 20m, 2m, 8),
        [StrategyTemplateKeys.FlatRange] = (0.5m, 2m, 4m, 3m, 5),
        [StrategyTemplateKeys.MacContrarian710] = (0.5m, 5m, 5m, 3m, 5),
        [StrategyTemplateKeys.ZigZagFade] = (0.5m, 4m, 8m, 3m, 5),
        [StrategyTemplateKeys.DonchianV2] = (0.5m, 8m, 30m, 1m, 1),
        [StrategyTemplateKeys.BinHv45] = (0.5m, 2.5m, 2.5m, 3m, 5),
        [StrategyTemplateKeys.ClucMay72018] = (0.5m, 5m, 1m, 3m, 5),
        [StrategyTemplateKeys.CombinedBinHCluc] = (0.5m, 5m, 5m, 3m, 5),
        [StrategyTemplateKeys.Hlhb] = (0.5m, 8m, 62m, 1m, 5),
        [StrategyTemplateKeys.FAdxSma] = (0.5m, 5m, 5m, 3m, 5),
        [StrategyTemplateKeys.TripleSupertrend] = (0.5m, 8m, 10m, 1m, 5),
    };

    public static async Task<int> RunAsync(string root, string cacheDir, int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 5, 120);
        var end = DateTimeOffset.UtcNow;
        var start = end.AddDays(-days);
        var universe = LoadUniverse(root);
        var specs = StrategyTemplateKeys.OperatorCatalog
            .Select(Describe)
            .ToArray();
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformCatalogDaily/1.0");
        var futures = new BinanceFuturesHistoryClient(http);
        var futuresRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
        var replay = new BacktestReplay(new StrategyEngine());
        var validator = new StrategyDefinitionValidator();
        var notes = new List<string>();
        var fills = specs.ToDictionary(spec => spec.Key, _ => new List<Fill>(), StringComparer.OrdinalIgnoreCase);

        Console.WriteLine($"Catalog daily replay. Window {start:yyyy-MM-dd} → {end:yyyy-MM-dd} UTC. {specs.Length} strategies. {universe.Length} USD-M coins.");
        Console.WriteLine("One $10,000 book per strategy. Position cap is the strategy's own cap. Fee 0.10% + slippage 0.05%. Funding is not charged. Open trades are marked at the last close.");

        foreach (var frame in specs.GroupBy(spec => spec.Timeframe))
        {
            var frameSpecs = frame.ToArray();
            var symbols = frameSpecs.All(spec => BtcOnly.Contains(spec.Key)) ? ["BTCUSDT"] : universe;
            var needsFutures = frameSpecs.Any(spec => spec.Key is StrategyTemplateKeys.FlowZone or StrategyTemplateKeys.SqueezeWatch);
            Console.WriteLine($"frame {frame.Key}: {symbols.Length} coins, {frameSpecs.Length} strategies");
            var seen = 0;
            foreach (var symbol in symbols)
            {
                cancellationToken.ThrowIfCancellationRequested();
                seen++;
                if (seen % 25 == 0 || seen == symbols.Length)
                {
                    Console.WriteLine($"  {frame.Key} {seen}/{symbols.Length}");
                }

                IReadOnlyList<MarketCandle> series;
                try
                {
                    var (bars, _, _) = await ResearchKlineCache.LoadAsync(
                        http,
                        cacheDir,
                        symbol,
                        frame.Key,
                        FetchFrom(frame.Key, start),
                        end,
                        cancellationToken,
                        requireTaker: frame.Key == "1h");
                    series = bars.Where(bar => bar.IsClosed).OrderBy(bar => bar.OpenTime).ToList();
                }
                catch (Exception ex)
                {
                    notes.Add($"{symbol} {frame.Key} candles: {ex.Message}");
                    continue;
                }

                if (series.Count < 40)
                {
                    continue;
                }

                StrategyFuturesSeries? aligned = null;
                if (needsFutures)
                {
                    try
                    {
                        var fetchFrom = FetchFrom("1h", start);
                        var funding = await FuturesHistoryCache.LoadOrFetchFundingAsync(futures, futuresRoot, symbol, fetchFrom, end, force: false, cancellationToken);
                        var oi = await FuturesHistoryCache.LoadOrFetchOpenInterestAsync(futures, futuresRoot, symbol, "1h", fetchFrom, end, force: false, cancellationToken);
                        aligned = FuturesHistoryCache.Align(series, funding, oi, mark: null, index: null, basis: null);
                    }
                    catch (Exception ex)
                    {
                        notes.Add($"{symbol} futures: {ex.Message}");
                    }
                }

                foreach (var spec in frameSpecs)
                {
                    if (BtcOnly.Contains(spec.Key) && !string.Equals(symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var book = Books[spec.Key];
                    var wantsFutures = spec.Key is StrategyTemplateKeys.FlowZone or StrategyTemplateKeys.SqueezeWatch;
                    if (wantsFutures && (aligned?.OpenInterest is null || (spec.Key == StrategyTemplateKeys.SqueezeWatch && aligned.FundingRate is null)))
                    {
                        continue;
                    }

                    var definition = validator.Parse(StrategyTemplates.Build(spec.Name, 1, Parameters(spec, symbol)));
                    var settings = Settings(start, end, book, spec.Key);
                    var result = replay.Run(definition, series, settings, futures: wantsFutures ? aligned : null);
                    foreach (var trade in result.Trades)
                    {
                        fills[spec.Key].Add(new Fill(symbol, trade.OpenedAt, trade.ClosedAt, trade.PnL, trade.Reason));
                    }
                }
            }
        }

        var rows = new List<Row>();
        foreach (var spec in specs)
        {
            var book = Books[spec.Key];
            var raw = fills[spec.Key];
            var kept = Cap(raw, book.Positions);
            decimal net = 0m;
            decimal marked = 0m;
            var wins = 0;
            var daily = new Dictionary<DateOnly, decimal>();
            foreach (var trade in kept)
            {
                net += trade.PnL;
                if (trade.PnL > 0m)
                {
                    wins++;
                }

                if (trade.Reason == "End of window")
                {
                    marked += trade.PnL;
                }

                var day = DateOnly.FromDateTime(trade.ClosedAt.UtcDateTime);
                daily[day] = daily.GetValueOrDefault(day) + trade.PnL;
            }

            var coins = kept.Select(trade => trade.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            rows.Add(new Row(spec.Name, spec.Key, spec.Timeframe, spec.Side, coins, kept.Count, wins, net, marked, Capital, daily));
            Console.WriteLine($"{spec.Name,-42} {spec.Timeframe,3} raw={raw.Count,5} kept={kept.Count,4} coins={coins,3} net={net,10:0.00} marked={marked,8:0.00}");
        }

        var outDir = Path.Combine(root, "artifacts", "catalog-daily");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "summary.txt");
        var text = Render(start, end, rows, notes);
        await File.WriteAllTextAsync(path, text, cancellationToken);
        Console.WriteLine(text);
        Console.WriteLine($"wrote {path}");
        return 0;
    }

    private static string Render(DateTimeOffset start, DateTimeOffset end, List<Row> rows, List<string> notes)
    {
        var culture = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"window {start:yyyy-MM-dd} -> {end:yyyy-MM-dd} UTC");
        sb.AppendLine("name\ttimeframe\tside\tcoins\ttrades\twins\tnet_usdt\tmarked_usdt\trealized_usdt\tdeployed\treturn_pct");
        foreach (var row in rows.OrderByDescending(r => r.Net))
        {
            var ret = row.Deployed == 0m ? 0m : row.Net / row.Deployed * 100m;
            var realized = row.Net - row.Marked;
            sb.AppendLine(string.Create(culture, $"{row.Name}\t{row.Timeframe}\t{row.Side}\t{row.Coins}\t{row.Trades}\t{row.Wins}\t{row.Net:0.00}\t{row.Marked:0.00}\t{realized:0.00}\t{row.Deployed:0}\t{ret:0.00}"));
        }

        var days = rows.SelectMany(r => r.Daily.Keys).Distinct().OrderBy(d => d).ToList();
        sb.AppendLine("DAILY");
        sb.Append("date");
        foreach (var row in rows)
        {
            sb.Append('\t').Append(row.Name);
        }

        sb.AppendLine("\tALL");
        foreach (var day in days)
        {
            sb.Append(day.ToString("yyyy-MM-dd", culture));
            decimal sum = 0m;
            foreach (var row in rows)
            {
                var pnl = row.Daily.GetValueOrDefault(day);
                sum += pnl;
                sb.Append('\t').Append(pnl.ToString("0.00", culture));
            }

            sb.Append('\t').AppendLine(sum.ToString("0.00", culture));
        }

        if (notes.Count > 0)
        {
            sb.AppendLine("NOTES");
            foreach (var note in notes.Distinct())
            {
                sb.AppendLine(note);
            }
        }

        return sb.ToString();
    }

    private static Spec Describe(string key)
    {
        var ema = key == StrategyTemplateKeys.BtcEma20Ema50Long;
        var daily = key is StrategyTemplateKeys.TsMomentum285 or StrategyTemplateKeys.BtcDailyMax10;
        var flat = key == StrategyTemplateKeys.FlatRange;
        var flow = key == StrategyTemplateKeys.FlowZone;
        var squeeze = key == StrategyTemplateKeys.SqueezeWatch;
        var impulse = key == StrategyTemplateKeys.ImpulseCatch;
        var zigzag = key == StrategyTemplateKeys.ZigZagFade;
        var donchian = key == StrategyTemplateKeys.DonchianV2;
        var binhv = key == StrategyTemplateKeys.BinHv45;
        var hlhb = key == StrategyTemplateKeys.Hlhb;
        var hour = key is StrategyTemplateKeys.FAdxSma or StrategyTemplateKeys.TripleSupertrend;
        var freqtradeLong = binhv || hlhb || key is StrategyTemplateKeys.ClucMay72018 or StrategyTemplateKeys.CombinedBinHCluc;
        var fitted = StrategyTemplateKeys.IsHistoricallyFitted(key);
        var research = key is not (
            StrategyTemplateKeys.EmaRsiTrend
            or StrategyTemplateKeys.RsiPullback
            or StrategyTemplateKeys.BollingerReversion
            or StrategyTemplateKeys.SqueezeWatch
            or StrategyTemplateKeys.FlatRange);
        var quality = !research && !flat && !flow && !squeeze && !impulse && !fitted;
        var side = ema || daily || freqtradeLong || impulse ? "LONG" : "BOTH";
        var timeframe = binhv ? "1m"
            : hlhb ? "4h"
            : hour ? "1h"
            : donchian ? "1d"
            : zigzag ? "30m"
            : daily ? "1d"
            : ema ? "30m"
            : impulse ? "15m"
            : flat || flow || squeeze ? "1h"
            : fitted ? "15m"
            : "5m";
        return new Spec(key, StrategyTemplates.DisplayName(key), timeframe, side, quality);
    }

    private static StrategyTemplateParams Parameters(Spec spec, string symbol)
    {
        var parameters = StrategyTemplates.DefaultsFor(spec.Key, spec.Quality) with
        {
            AllowedSide = spec.Side == "LONG" ? StrategySides.Long : StrategySides.Both,
            Timeframe = spec.Timeframe
        };
        if (spec.Key != StrategyTemplateKeys.ZigZagFade)
        {
            return parameters;
        }

        var (deviation, atr) = symbol switch
        {
            "ETHUSDT" => (6m, 1.5m),
            "SOLUSDT" => (5m, 2.5m),
            _ => (2m, 1.5m)
        };
        return parameters with { PriceChangeThreshold = deviation, AtrStopMultiplier = atr };
    }

    private static ReplaySettings Settings(
        DateTimeOffset start,
        DateTimeOffset end,
        (decimal Risk, decimal Stop, decimal Take, decimal Leverage, int Positions) book,
        string key)
    {
        var imported = StrategyTemplateKeys.IsImported(key);
        var flat = StrategyTemplateKeys.IsFlatRange(key);
        return new ReplaySettings(
            start,
            end,
            Capital,
            book.Risk,
            book.Leverage,
            0.10m,
            0.05m,
            book.Stop,
            book.Take,
            3m,
            4m,
            book.Positions,
            5,
            30,
            1m,
            HonorSuggestedStops: flat || imported,
            MaxHoldBars: flat ? FlatRangeStrategy.MaxHoldHours : 0,
            BookStopsOff: imported);
    }

    private static List<Fill> Cap(List<Fill> trades, int slots)
    {
        var room = Math.Max(1, slots);
        var open = new List<Fill>();
        var kept = new List<Fill>();
        foreach (var trade in trades.OrderBy(row => row.OpenedAt).ThenBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase))
        {
            open.RemoveAll(row => row.ClosedAt <= trade.OpenedAt);
            if (open.Count >= room || open.Any(row => string.Equals(row.Symbol, trade.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            open.Add(trade);
            kept.Add(trade);
        }

        return kept;
    }

    private static string[] LoadUniverse(string root)
    {
        var path = Path.Combine(root, "src", "TradingPlatform.Api", "data", "futures-universe.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("Contracts").EnumerateArray()
            .Where(row =>
                string.Equals(row.GetProperty("Status").GetString(), "TRADING", StringComparison.OrdinalIgnoreCase)
                && string.Equals(row.GetProperty("ContractType").GetString(), "PERPETUAL", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.GetProperty("Symbol").GetString() ?? "")
            .Where(symbol => symbol.EndsWith("USDT", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static DateTimeOffset FetchFrom(string timeframe, DateTimeOffset start) => timeframe switch
    {
        "1d" => new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
        "4h" => start.AddDays(-90),
        "1h" => start.AddDays(-20),
        "30m" => start.AddDays(-10),
        "15m" => start.AddDays(-8),
        "5m" => start.AddDays(-3),
        "1m" => start.AddDays(-1),
        _ => start.AddDays(-10)
    };

    private sealed record Spec(string Key, string Name, string Timeframe, string Side, bool Quality);

    private sealed record Fill(string Symbol, DateTimeOffset OpenedAt, DateTimeOffset ClosedAt, decimal PnL, string Reason);

    private sealed record Row(
        string Name,
        string Key,
        string Timeframe,
        string Side,
        int Coins,
        int Trades,
        int Wins,
        decimal Net,
        decimal Marked,
        decimal Deployed,
        Dictionary<DateOnly, decimal> Daily);
}
