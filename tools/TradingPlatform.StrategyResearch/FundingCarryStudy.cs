using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Research only. No orders. Long the index and short the mark while settled funding is positive.
/// The rate that triggers entry is not collected. The rate that turns non-positive is paid, then the hedge exits.
/// Spot and futures taker fees plus slippage are charged once per round trip. Thresholds were not searched.
/// </summary>
internal static class FundingCarryStudy
{
    private const double Cost = 0.0036;
    private const long Hour = 3_600_000L;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root)
    {
        Check();
        var dir = Path.Combine(root, "artifacts", "data", "futures-history-v1");
        var symbols = Directory.GetFiles(Path.Combine(dir, "index"), "*_1h.json")
            .Select(path => Path.GetFileName(path).Replace("_1h.json", "", StringComparison.OrdinalIgnoreCase))
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToList();
        var trades = new List<Trade>();
        var always = new List<Trade>();
        var skipped = 0;
        foreach (var symbol in symbols)
        {
            var index = ReadPrices(Path.Combine(dir, "index", symbol + "_1h.json"));
            var mark = ReadPrices(Path.Combine(dir, "mark", symbol + "_1h.json"));
            var funding = ReadFunding(Path.Combine(dir, "funding", symbol + ".json"));
            var events = Align(index, mark, funding, ref skipped);
            trades.AddRange(Conditional(symbol, events));
            var held = Always(symbol, events);
            if (held is not null)
            {
                always.Add(held.Value);
            }
        }

        var text = Report(symbols.Count, skipped, trades, always);
        var path = Path.Combine(root, "docs", "FUNDING_CARRY.md");
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    private static void Check()
    {
        var events = new List<Event>
        {
            new(0, 100, 101, 0.001),
            new(Hour * 8, 100, 101, 0.001),
            new(Hour * 16, 110, 110, -0.0001)
        };
        var trades = Conditional("TEST", events);
        if (trades.Count != 1)
        {
            throw new InvalidOperationException("Carry check did not open one hedge.");
        }

        var trade = trades[0];
        var price = 110d / 100d - 110d / 101d;
        var funding = 0.001 - 0.0001;
        if (Math.Abs(trade.Price - price) > 1e-12 || Math.Abs(trade.Funding - funding) > 1e-12)
        {
            throw new InvalidOperationException("Carry check price or funding does not match the frozen formula.");
        }
    }

    private static List<Trade> Conditional(string symbol, List<Event> events)
    {
        var trades = new List<Trade>();
        double entryIndex = 0, entryMark = 0, funding = 0;
        long entryAt = 0;
        var open = false;
        foreach (var point in events)
        {
            if (open)
            {
                funding += point.Rate;
                if (point.Rate <= 0)
                {
                    trades.Add(Close(symbol, entryAt, entryIndex, entryMark, point, funding));
                    open = false;
                    funding = 0;
                }
            }

            if (!open && point.Rate > 0 && point.Index > 0 && point.Mark > 0)
            {
                open = true;
                entryAt = point.At;
                entryIndex = point.Index;
                entryMark = point.Mark;
                funding = 0;
            }
        }

        if (open && events.Count > 0)
        {
            var last = events[^1];
            trades.Add(Close(symbol, entryAt, entryIndex, entryMark, last, funding) with { Open = true });
        }

        return trades;
    }

    private static Trade? Always(string symbol, List<Event> events)
    {
        if (events.Count < 2)
        {
            return null;
        }

        var first = events[0];
        var last = events[^1];
        var funding = events.Sum(point => point.Rate);
        return Close(symbol, first.At, first.Index, first.Mark, last, funding);
    }

    private static Trade Close(string symbol, long entryAt, double entryIndex, double entryMark, Event exit, double funding)
    {
        var price = exit.Index / entryIndex - exit.Mark / entryMark;
        return new Trade(symbol, entryAt, exit.At, funding, price, funding + price - Cost, false);
    }

    private static List<Event> Align(Dictionary<long, double> index, Dictionary<long, double> mark, List<(long At, double Rate)> funding, ref int skipped)
    {
        var events = new List<Event>();
        foreach (var point in funding)
        {
            var hour = point.At / Hour * Hour;
            if (!index.TryGetValue(hour, out var indexPx) || !mark.TryGetValue(hour, out var markPx) || indexPx <= 0 || markPx <= 0)
            {
                skipped++;
                continue;
            }

            events.Add(new Event(point.At, indexPx, markPx, point.Rate));
        }

        return events;
    }

    private static string Report(int coins, int skipped, List<Trade> trades, List<Trade> always)
    {
        var closed = trades.Where(trade => !trade.Open).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("# Funding carry");
        sb.AppendLine();
        sb.AppendLine("Research replay only. No Paper bot, no live order. Thresholds were not searched.");
        sb.AppendLine();
        sb.AppendLine("Hedge: long the index, short the mark, same notional. Enter after a settled funding rate above zero. That triggering rate is not collected. Later positive settlements are collected. The first settlement at or below zero is paid, then the hedge exits. A position still open at the last bar is marked and excluded from the closed-trade averages.");
        sb.AppendLine();
        sb.AppendLine("Price result is index exit / index entry minus mark exit / mark entry. Funding result is the sum of settled rates while the hedge was on, including the exit settlement. Cost is 0.36% per round trip: spot taker 0.10% and futures taker 0.04% on each side, plus 0.02% slippage on each of the four fills. Cash required is about two units of notional at 1x, so a return on cash is about half the notional return below. The limit-order spread idea is not in this table. Historical top-of-book is not on disk.");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Coins with 1h index and mark: {coins}. Funding hours without both prices, skipped: {skipped}."));
        sb.AppendLine();
        sb.AppendLine("## Enter only while funding is positive");
        sb.AppendLine();
        AppendSplit(sb, "ALL", closed);
        AppendSplit(sb, "IS ", closed.Where(trade => trade.When < IsEnd.ToUnixTimeMilliseconds()).ToList());
        AppendSplit(sb, "VAL", closed.Where(trade => trade.When >= IsEnd.ToUnixTimeMilliseconds() && trade.When < ValEnd.ToUnixTimeMilliseconds()).ToList());
        AppendSplit(sb, "OOS", closed.Where(trade => trade.When >= ValEnd.ToUnixTimeMilliseconds()).ToList());
        var open = trades.Count(trade => trade.Open);
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"  still open {open}"));
        sb.AppendLine();
        sb.AppendLine("## Always hedged, one trade per coin");
        sb.AppendLine();
        AppendSplit(sb, "ALL", always);
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static void AppendSplit(StringBuilder sb, string name, List<Trade> trades)
    {
        if (trades.Count == 0)
        {
            sb.AppendLine("  " + name + " none");
            return;
        }

        var n = trades.Count;
        var funding = trades.Average(trade => trade.Funding);
        var price = trades.Average(trade => trade.Price);
        var net = trades.Average(trade => trade.Net);
        var win = trades.Count(trade => trade.Net > 0) / (double)n;
        var hours = trades.Average(trade => (trade.Until - trade.When) / (double)Hour);
        sb.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  {name} n={n} funding={funding:P3} price={price:P3} net={net:P3} win={win:P0} hold={hours:0.0}h"));
    }

    private readonly record struct Event(long At, double Index, double Mark, double Rate);
    private readonly record struct Trade(string Symbol, long When, long Until, double Funding, double Price, double Net, bool Open);

    private static Dictionary<long, double> ReadPrices(string path)
    {
        var map = new Dictionary<long, double>();
        if (!File.Exists(path))
        {
            return map;
        }

        foreach (var item in JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.EnumerateArray())
        {
            var open = DateTimeOffset.Parse(item.GetProperty("OpenTime").GetString()!, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
            map[open / Hour * Hour] = item.GetProperty("Close").GetDouble();
        }

        return map;
    }

    private static List<(long At, double Rate)> ReadFunding(string path)
    {
        var rows = new List<(long, double)>();
        if (!File.Exists(path))
        {
            return rows;
        }

        foreach (var item in JsonDocument.Parse(File.ReadAllBytes(path)).RootElement.EnumerateArray())
        {
            var at = DateTimeOffset.Parse(item.GetProperty("FundingTime").GetString()!, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
            rows.Add((at, item.GetProperty("FundingRate").GetDouble()));
        }

        rows.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return rows;
    }
}
