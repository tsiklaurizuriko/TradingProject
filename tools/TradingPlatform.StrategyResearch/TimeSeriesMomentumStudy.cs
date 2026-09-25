using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Han, Kang, and Ryu (2026): long-only time-series momentum, look-back 28 days, hold 5 days.
/// The market is bought when the 28-day return is in the top third of its own history.
/// Research replay. No short, no orders.
/// </summary>
internal static class TimeSeriesMomentumStudy
{
    private const int Lookback = 28;
    private const int Hold = 5;
    private const double CostPaper = 0.0015;
    private const double CostBook = 0.0012;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root, string cacheDir)
    {
        Console.WriteLine("Time-series momentum replay. BTC only. Long only. No orders.");
        var path = Path.Combine(cacheDir, "BTCUSDT_1h.json");
        if (!File.Exists(path))
        {
            Console.WriteLine("BTCUSDT 1h cache is missing.");
            return 1;
        }

        var daily = DailyCloses(path);
        var rows = Replay(daily);
        var text = Report(daily, rows);
        var doc = Path.Combine(root, "docs", "TIME_SERIES_MOMENTUM.md");
        File.WriteAllText(doc, text);
        Console.WriteLine(text);
        Console.WriteLine("Wrote " + doc);
        return 0;
    }

    private static List<Day> Replay(List<Day> daily)
    {
        var ret28 = new double[daily.Count];
        Array.Fill(ret28, double.NaN);
        var past = new List<double>();
        var signal = new bool[daily.Count];
        for (var i = Lookback; i < daily.Count; i++)
        {
            if (daily[i - Lookback].Close <= 0)
            {
                continue;
            }

            ret28[i] = daily[i].Close / daily[i - Lookback].Close - 1d;
            if (past.Count > 0)
            {
                var below = past.Count(x => x < ret28[i]);
                signal[i] = below / (double)past.Count >= 2d / 3d;
            }

            past.Add(ret28[i]);
        }

        var weight = new double[daily.Count];
        for (var i = 0; i < daily.Count; i++)
        {
            if (!signal[i])
            {
                continue;
            }

            for (var k = 1; k <= Hold && i + k < daily.Count; k++)
            {
                weight[i + k] += 1d / Hold;
            }
        }

        var rows = new List<Day>();
        for (var i = 1; i < daily.Count; i++)
        {
            if (daily[i - 1].Close <= 0)
            {
                continue;
            }

            var gross = weight[i] * (daily[i].Close / daily[i - 1].Close - 1d);
            var turn = Math.Abs(weight[i] - weight[i - 1]);
            rows.Add(daily[i] with
            {
                Gross = gross,
                Turn = turn,
                Weight = weight[i]
            });
        }

        return rows;
    }

    private static string Report(List<Day> daily, List<Day> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Time-series momentum");
        sb.AppendLine();
        sb.AppendLine("Research replay only. No Paper bot, no live order. The rule is frozen from Han, Kang, and Ryu, SSRN 4675565. Thresholds were not searched on this cache.");
        sb.AppendLine();
        sb.AppendLine($"Coin: BTCUSDT. Clock: one UTC day, the last closed 1h bar of that day. Days in cache: {daily.Count}. From {daily[0].When:yyyy-MM-dd} to {daily[^1].When:yyyy-MM-dd}.");
        sb.AppendLine();
        sb.AppendLine("Signal: the 28-day close-to-close return is in the top third of every earlier 28-day return on this series. Direction is long only. A signal funds one fifth of the book for each of the next five days, so five signals can fill the book. A day with no live sleeve is flat. The short book in the paper loses and is not used.");
        sb.AppendLine();
        sb.AppendLine("Cost is charged on the fraction of the book that changes that day. The paper uses 15 bp. This project's book uses 12 bp round trip. Funding is not in the 1h file and is not invented. Leverage is 1x.");
        sb.AppendLine();
        sb.AppendLine("| Split | Days | Days in market | Mean gross | Mean net 12 bp | Mean net 15 bp | Mean log net 12 bp | Growth 12 bp | Max drawdown 12 bp |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (name, slice) in new[]
        {
            ("IS", rows.Where(r => r.When < IsEnd).ToList()),
            ("VAL", rows.Where(r => r.When >= IsEnd && r.When < ValEnd).ToList()),
            ("OOS", rows.Where(r => r.When >= ValEnd).ToList())
        })
        {
            sb.AppendLine("| " + name + " | " + Row(slice) + " |");
        }

        sb.AppendLine();
        var isOk = Mean(rows.Where(r => r.When < IsEnd).ToList(), CostBook) > 0;
        var valOk = Mean(rows.Where(r => r.When >= IsEnd && r.When < ValEnd).ToList(), CostBook) > 0;
        var oosOk = Mean(rows.Where(r => r.When >= ValEnd).ToList(), CostBook) > 0;
        sb.AppendLine(isOk && valOk && oosOk
            ? "IS, VAL, and OOS mean net stay positive at 12 bp. This is still a research replay of one coin. It is not a Paper approval."
            : "The path does not stay positive at 12 bp on every window. This is not a reason to place orders.");
        sb.AppendLine();
        sb.AppendLine("## What the folder does not put in the book");
        sb.AppendLine();
        sb.AppendLine("- Cross-sectional momentum. The same paper liquidates most of those accounts.");
        sb.AppendLine("- Eight-to-ten-week reversal (SSRN 6703978). It is a different horizon from the 15-minute book already measured here, and this pass does not promote it.");
        sb.AppendLine("- Pairs (SSRN 6188418). The reported window is seven months, funding is not in the result, and the first live run lost about a third of a 53 dollar book on a stop bug.");
        sb.AppendLine("- Buying the 10-day low (SSRN 4955617). The out-of-sample section says that mean-reversion leg weakened. The 10-day high is the same family as this long-only trend book and is not a second signal.");
        sb.AppendLine("- Candles, inside bars, supply-demand zones, and Smart Money from the two handbooks. Those are the same price-action family already marked unstable on this cache.");
        sb.AppendLine();
        sb.AppendLine("## Risk");
        sb.AppendLine();
        sb.AppendLine("One coin. Long only. Isolated. Leverage 1x. No short. A sleeve is one fifth of the book. Five sleeves are the whole book. There is no stop in the paper, so none is added here. The account is flat when the 28-day return leaves the top third and the open sleeves expire.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static string Row(List<Day> rows)
    {
        if (rows.Count == 0)
        {
            return "0 | NA | NA | NA | NA | NA | NA | NA";
        }

        var inMkt = rows.Count(r => r.Weight > 0) / (double)rows.Count;
        var eq = 1d;
        var peak = 1d;
        var dd = 0d;
        var log = 0d;
        foreach (var row in rows)
        {
            var net = row.Gross - CostBook * row.Turn;
            eq *= 1d + net;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak <= 0 ? 1 : (peak - eq) / peak);
            if (1d + net > 0)
            {
                log += Math.Log(1d + net);
            }
        }

        return string.Join(" | ",
            rows.Count.ToString(CultureInfo.InvariantCulture),
            Fmt(inMkt),
            Fmt(rows.Average(r => r.Gross)),
            Fmt(Mean(rows, CostBook)),
            Fmt(Mean(rows, CostPaper)),
            Fmt(log / rows.Count),
            Fmt(eq - 1d),
            Fmt(dd));
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
