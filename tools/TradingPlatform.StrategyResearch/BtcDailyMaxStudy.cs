using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Quantpedia BTC daily MAX. Buy when the close is the high of the last 10 UTC days. Hold the next day. Long only.
/// Horizons 20, 30, 40, and 50 are reported and are not used to pick a rule.
/// </summary>
internal static class BtcDailyMaxStudy
{
    private const int Primary = 10;
    private static readonly int[] Horizons = [10, 20, 30, 40, 50];
    private const double CostPaper = 0.0015;
    private const double CostBook = 0.0012;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root, string cacheDir)
    {
        Console.WriteLine("BTC daily MAX replay. Long only. No orders.");
        var path = Path.Combine(cacheDir, "BTCUSDT_1h.json");
        if (!File.Exists(path))
        {
            Console.WriteLine("BTCUSDT 1h cache is missing.");
            return 1;
        }

        var daily = DailyCloses(path);
        var text = Report(daily);
        var doc = Path.Combine(root, "docs", "BTC_DAILY_MAX.md");
        File.WriteAllText(doc, text);
        Console.WriteLine(text);
        Console.WriteLine("Wrote " + doc);
        return 0;
    }

    private static string Report(List<Day> daily)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# BTC daily MAX");
        sb.AppendLine();
        sb.AppendLine("Research replay. The live switch is a separate operator action and is off until a bot is started. The rule is frozen from Quantpedia SSRN 4955617: buy BTC when the daily price is the maximum of the last 10 days, and hold the next day. Thresholds were not searched on this cache.");
        sb.AppendLine();
        sb.AppendLine($"Coin: BTCUSDT. Clock: one UTC day, the last closed 1h bar of that day. Days in cache: {daily.Count}. From {daily[0].When:yyyy-MM-dd} to {daily[^1].When:yyyy-MM-dd}.");
        sb.AppendLine();
        sb.AppendLine("Signal: today's close is greater than or equal to every close in the previous 9 days, so the 10-day window ending today makes its high today. The book is long the next day and flat otherwise. Long only. No short. The MIN rule is not used.");
        sb.AppendLine();
        sb.AppendLine("Cost is charged on the fraction of the book that changes that day, using the same 12 bp and 15 bp figures as the time-series momentum replay. Funding is not in the 1h file and is not invented. Leverage is 1x.");
        sb.AppendLine();
        sb.AppendLine("## 10-day high");
        sb.AppendLine();
        WriteWindow(sb, Replay(daily, Primary));
        sb.AppendLine();
        sb.AppendLine("Buy and hold on the same days, with no turnover cost inside the window:");
        sb.AppendLine();
        sb.AppendLine("| Split | Growth | Max drawdown |");
        sb.AppendLine("| --- | ---: | ---: |");
        foreach (var (name, slice) in Windows(HoldReplay(daily)))
        {
            var (growth, dd) = PathOf(slice, 0);
            sb.AppendLine($"| {name} | {Fmt(growth)} | {Fmt(dd)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Published horizons, not a search");
        sb.AppendLine();
        sb.AppendLine("10, 20, 30, 40, and 50 are the horizons in the paper. The book uses 10. A higher number on this cache does not replace it.");
        sb.AppendLine();
        sb.AppendLine("| Days | IS growth 12 bp | VAL growth 12 bp | OOS growth 12 bp | IS mean net 12 bp | VAL mean net 12 bp | OOS mean net 12 bp |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var n in Horizons)
        {
            var rows = Replay(daily, n);
            var cells = Windows(rows).Select(pair =>
            {
                var (growth, _) = PathOf(pair.Slice, CostBook);
                return (Growth: growth, Mean: Mean(pair.Slice, CostBook));
            }).ToList();
            sb.AppendLine("| " + n + " | " + string.Join(" | ", cells.Select(c => Fmt(c.Growth))) + " | " + string.Join(" | ", cells.Select(c => Fmt(c.Mean))) + " |");
        }

        sb.AppendLine();
        var primary = Replay(daily, Primary);
        var isOk = Mean(Windows(primary)[0].Slice, CostBook) > 0;
        var valOk = Mean(Windows(primary)[1].Slice, CostBook) > 0;
        var oosOk = Mean(Windows(primary)[2].Slice, CostBook) > 0;
        sb.AppendLine(isOk && valOk && oosOk
            ? "The 10-day rule stays positive at 12 bp on IS, VAL, and OOS. One coin. This is not a profit claim."
            : "The 10-day rule does not stay positive at 12 bp on every window. The operator can still start it. This is not a profit claim.");
        sb.AppendLine();
        sb.AppendLine("## Risk");
        sb.AppendLine();
        sb.AppendLine("One coin. Long only. Isolated. Leverage 1x. The platform stop is a rail, not the paper's exit. The position closes when the latest closed day is no longer a 10-day high.");
        return sb.ToString();
    }

    private static void WriteWindow(StringBuilder sb, List<Day> rows)
    {
        sb.AppendLine("| Split | Days | Days in market | Mean gross | Mean net 12 bp | Mean net 15 bp | Growth 12 bp | Max drawdown 12 bp |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (name, slice) in Windows(rows))
        {
            sb.AppendLine("| " + name + " | " + Row(slice) + " |");
        }
    }

    private static (string Name, List<Day> Slice)[] Windows(List<Day> rows) =>
    [
        ("IS", rows.Where(r => r.When < IsEnd).ToList()),
        ("VAL", rows.Where(r => r.When >= IsEnd && r.When < ValEnd).ToList()),
        ("OOS", rows.Where(r => r.When >= ValEnd).ToList())
    ];

    private static List<Day> Replay(List<Day> daily, int lookback)
    {
        var signal = new bool[daily.Count];
        for (var i = lookback - 1; i < daily.Count; i++)
        {
            var high = daily[i].Close;
            var atHigh = true;
            for (var k = 1; k < lookback; k++)
            {
                if (daily[i - k].Close > high)
                {
                    atHigh = false;
                    break;
                }
            }

            signal[i] = atHigh;
        }

        var rows = new List<Day>();
        var prev = 0d;
        for (var i = 1; i < daily.Count; i++)
        {
            if (daily[i - 1].Close <= 0)
            {
                continue;
            }

            var weight = signal[i - 1] ? 1d : 0d;
            var gross = weight * (daily[i].Close / daily[i - 1].Close - 1d);
            rows.Add(daily[i] with
            {
                Gross = gross,
                Turn = Math.Abs(weight - prev),
                Weight = weight
            });
            prev = weight;
        }

        return rows;
    }

    private static List<Day> HoldReplay(List<Day> daily)
    {
        var rows = new List<Day>();
        for (var i = 1; i < daily.Count; i++)
        {
            if (daily[i - 1].Close <= 0)
            {
                continue;
            }

            rows.Add(daily[i] with
            {
                Gross = daily[i].Close / daily[i - 1].Close - 1d,
                Turn = 0,
                Weight = 1
            });
        }

        return rows;
    }

    private static string Row(List<Day> rows)
    {
        if (rows.Count == 0)
        {
            return "0 | NA | NA | NA | NA | NA | NA";
        }

        var (growth, dd) = PathOf(rows, CostBook);
        var inMkt = rows.Count(r => r.Weight > 0) / (double)rows.Count;
        return string.Join(" | ",
            rows.Count.ToString(CultureInfo.InvariantCulture),
            Fmt(inMkt),
            Fmt(rows.Average(r => r.Gross)),
            Fmt(Mean(rows, CostBook)),
            Fmt(Mean(rows, CostPaper)),
            Fmt(growth),
            Fmt(dd));
    }

    private static (double Growth, double Drawdown) PathOf(List<Day> rows, double cost)
    {
        var eq = 1d;
        var peak = 1d;
        var dd = 0d;
        foreach (var row in rows)
        {
            eq *= 1d + row.Gross - cost * row.Turn;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak <= 0 ? 1 : (peak - eq) / peak);
        }

        return (eq - 1d, dd);
    }

    private static double Mean(List<Day> rows, double cost) =>
        rows.Count == 0 ? double.NaN : rows.Average(r => r.Gross - cost * r.Turn);

    private static List<Day> DailyCloses(string path)
    {
        var hours = Read(path);
        var days = new List<Day>();
        DateTimeOffset? current = null;
        var close = 0d;
        var when = DateTimeOffset.MinValue;
        foreach (var bar in hours)
        {
            var day = new DateTimeOffset(bar.When.UtcDateTime.Date, TimeSpan.Zero);
            if (current is null)
            {
                current = day;
            }

            if (day != current)
            {
                days.Add(new Day(when, close, 0, 0, 0));
                current = day;
            }

            close = bar.Close;
            when = day;
        }

        if (current is not null)
        {
            days.Add(new Day(when, close, 0, 0, 0));
        }

        return days;
    }

    private static List<(DateTimeOffset When, double Close)> Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var rows = new List<(DateTimeOffset, double)>();
        long ot = 0;
        double cv = 0;
        var inObj = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                inObj = true;
                ot = 0;
                cv = 0;
            }
            else if (reader.TokenType == JsonTokenType.EndObject && inObj)
            {
                if (cv > 0 && ot > 0)
                {
                    rows.Add((DateTimeOffset.FromUnixTimeMilliseconds(ot), cv));
                }

                inObj = false;
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "OpenTime":
                        ot = reader.TokenType == JsonTokenType.String
                            ? reader.GetDateTimeOffset().ToUnixTimeMilliseconds()
                            : reader.GetInt64();
                        break;
                    case "Close":
                        cv = reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0;
                        break;
                }
            }
        }

        rows.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return rows;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "NA" : v.ToString("0.####", CultureInfo.InvariantCulture);

    private readonly record struct Day(DateTimeOffset When, double Close, double Gross, double Turn, double Weight);
}
