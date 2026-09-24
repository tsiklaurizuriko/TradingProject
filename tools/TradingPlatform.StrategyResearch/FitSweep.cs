using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Fits one frequent book on the cached history. The winner is chosen on that same history.
/// </summary>
internal static class FitSweep
{
    private const double Cost = 0.0012;
    private const int Hold = 36;
    private static readonly string[] Symbols =
    [
        "ADAUSDT", "APTUSDT", "ARBUSDT", "ATOMUSDT", "AVAXUSDT", "BNBUSDT", "BTCUSDT", "DOGEUSDT", "DOTUSDT", "ETHUSDT",
        "FILUSDT", "LINKUSDT", "LTCUSDT", "NEARUSDT", "OPUSDT", "SOLUSDT", "SUIUSDT", "TONUSDT", "UNIUSDT", "XRPUSDT"
    ];

    private static readonly double[] Stops = [0.003, 0.005, 0.008, 0.01, 0.015, 0.02, 0.03];
    private static readonly double[] Targets = [0.004, 0.006, 0.01, 0.015, 0.02, 0.03, 0.05];

    public static int Run(string root, string cacheDir)
    {
        Console.WriteLine("Fit sweep. Same history selects the book. No orders.");
        var hits = new List<Hit>();
        foreach (var tf in new[] { "1m", "3m", "5m", "15m", "30m" })
        {
            Console.WriteLine("loading " + tf);
            var books = Load(cacheDir, tf);
            if (books.Count < 10)
            {
                Console.WriteLine($"skip {tf} coins {books.Count}");
                continue;
            }

            var days = (books.Max(b => b.T[^1]) - books.Min(b => b.T[Warm(b)])) / 86_400_000d;
            foreach (var fade in new[] { true, false })
            {
                foreach (var sl in Stops)
                {
                    foreach (var tp in Targets)
                    {
                        var stat = Sim(books, fade, sl, tp);
                        if (stat.N < 200 || days <= 0)
                        {
                            continue;
                        }

                        var perDay = stat.N / days;
                        if (perDay < 3)
                        {
                            continue;
                        }

                        hits.Add(new Hit(tf, fade, sl, tp, stat.N, perDay, stat.Mean, stat.Pf, stat.Win, stat.Sum, stat.Dd));
                    }
                }
            }

            if (tf is "5m" or "15m" or "30m")
            {
                foreach (var hold in new[] { 24, 72, 144 })
                {
                    foreach (var sl in new[] { 0.008, 0.012, 0.015, 0.02, 0.03 })
                    {
                        foreach (var tp in new[] { 0.02, 0.03, 0.04, 0.06, 0.08 })
                        {
                            var stat = SimList(books, sl, tp, hold, static b => b.Strong);
                            if (stat.N < 200 || days <= 0)
                            {
                                continue;
                            }

                            var perDay = stat.N / days;
                            if (perDay < 3)
                            {
                                continue;
                            }

                            hits.Add(new Hit(tf + " strong-long h" + hold, true, sl, tp, stat.N, perDay, stat.Mean, stat.Pf, stat.Win, stat.Sum, stat.Dd));
                        }
                    }
                }
            }

            Console.WriteLine($"{tf} days {days:0} candidates {hits.Count}");
        }

        var winners = hits.Where(h => h.Pf > 1 && h.Sum > 0).OrderByDescending(h => h.Sum).Take(8).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Fitted on the full cached history of the 20 coins that have 1m bars. 30m is built from 15m. This selection saw the same past it reports.");
        sb.AppendLine("Rule: fade a closed bar that closes in the outer 35% of its range with RSI already stretched. Follow is the opposite bar. Next open. 12bp. One position per coin. Flat after 36 bars if SL/TP has not hit.");
        sb.AppendLine();
        if (winners.Count == 0)
        {
            sb.AppendLine("No book with at least 3 trades per day and profit factor above 1.");
            foreach (var h in hits.OrderByDescending(h => h.Sum).Take(8))
            {
                sb.AppendLine(Line(h));
            }
        }
        else
        {
            sb.AppendLine("Best past book:");
            sb.AppendLine(Line(winners[0]));
            sb.AppendLine();
            sb.AppendLine("Next on the same history:");
            foreach (var h in winners.Skip(1))
            {
                sb.AppendLine(Line(h));
            }
        }

        var path = Path.Combine(root, "artifacts", "strategy-research", "fit-sweep.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine(sb.ToString());
        return 0;
    }

    private static string Line(Hit h) =>
        $"{h.Tf} {(h.Fade ? "fade" : "follow")} SL {Pct(h.Sl)} TP {Pct(h.Tp)} n={h.N} perDay={h.PerDay:0.0} mean={h.Mean:0.0000} pf={h.Pf:0.00} win={h.Win:0.00} sum={h.Sum:0.00} dd={h.Dd:0.00}";

    private static string Pct(double v) => v.ToString("0.0%", CultureInfo.InvariantCulture);

    private static Stat Sim(List<Series> books, bool fade, double sl, double tp)
    {
        var nets = new List<double>(10000);
        foreach (var book in books)
        {
                var sig = fade ? book.Fade : book.Follow;
                var statHold = Hold;
            var busy = Warm(book);
            for (var s = 0; s < sig.Count; s++)
            {
                var i = sig[s];
                if (i < busy)
                {
                    continue;
                }

                var side = book.Side[i];
                if (!fade)
                {
                    side = (sbyte)-side;
                }

                var entry = i + 1;
                var last = Math.Min(book.N - 1, entry + statHold);
                if (entry >= book.N || book.O[entry] <= 0)
                {
                    continue;
                }

                var px = book.O[entry];
                var exit = last;
                double net = 0;
                var closed = false;
                for (var k = entry; k < last; k++)
                {
                    var up = book.H[k] / px - 1d;
                    var dn = 1d - book.L[k] / px;
                    var stop = side > 0 ? dn >= sl : up >= sl;
                    var target = side > 0 ? up >= tp : dn >= tp;
                    if (stop)
                    {
                        net = -sl - Cost;
                        exit = k;
                        closed = true;
                        break;
                    }

                    if (target)
                    {
                        net = tp - Cost;
                        exit = k;
                        closed = true;
                        break;
                    }
                }

                if (!closed)
                {
                    if (book.O[exit] <= 0)
                    {
                        continue;
                    }

                    net = side * (book.O[exit] / px - 1d) - Cost;
                }

                nets.Add(net);
                busy = exit;
            }
        }

        if (nets.Count == 0)
        {
            return new Stat(0, 0, 0, 0, 0, 0);
        }

        var wins = nets.Where(x => x > 0).Sum();
        var losses = nets.Where(x => x < 0).Sum();
        var pf = losses == 0 ? 99 : wins / Math.Abs(losses);
        var eq = 0d;
        var peak = 0d;
        var dd = 0d;
        foreach (var x in nets)
        {
            eq += x;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak - eq);
        }

        return new Stat(nets.Count, nets.Average(), pf, nets.Count(x => x > 0) / (double)nets.Count, nets.Sum(), dd);
    }

    private static Stat SimList(List<Series> books, double sl, double tp, int hold, Func<Series, List<int>> bars)
    {
        var nets = new List<double>(10000);
        foreach (var book in books)
        {
            var sig = bars(book);
            var busy = Warm(book);
            for (var s = 0; s < sig.Count; s++)
            {
                var i = sig[s];
                if (i < busy)
                {
                    continue;
                }

                var entry = i + 1;
                var last = Math.Min(book.N - 1, entry + hold);
                if (entry >= book.N || book.O[entry] <= 0)
                {
                    continue;
                }

                var px = book.O[entry];
                var exit = last;
                double net = 0;
                var closed = false;
                for (var k = entry; k < last; k++)
                {
                    var up = book.H[k] / px - 1d;
                    var dn = 1d - book.L[k] / px;
                    if (dn >= sl)
                    {
                        net = -sl - Cost;
                        exit = k;
                        closed = true;
                        break;
                    }

                    if (up >= tp)
                    {
                        net = tp - Cost;
                        exit = k;
                        closed = true;
                        break;
                    }
                }

                if (!closed)
                {
                    if (book.O[exit] <= 0)
                    {
                        continue;
                    }

                    net = book.O[exit] / px - 1d - Cost;
                }

                nets.Add(net);
                busy = exit;
            }
        }

        if (nets.Count == 0)
        {
            return new Stat(0, 0, 0, 0, 0, 0);
        }

        var wins = nets.Where(x => x > 0).Sum();
        var losses = nets.Where(x => x < 0).Sum();
        var pf = losses == 0 ? 99 : wins / Math.Abs(losses);
        var eq = 0d;
        var peak = 0d;
        var dd = 0d;
        foreach (var x in nets)
        {
            eq += x;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak - eq);
        }

        return new Stat(nets.Count, nets.Average(), pf, nets.Count(x => x > 0) / (double)nets.Count, nets.Sum(), dd);
    }

    private static int Warm(Series b) => Math.Min(50, b.N - 2);

    private static List<Series> Load(string cacheDir, string tf)
    {
        var list = new List<Series>();
        foreach (var symbol in Symbols)
        {
            if (tf == "30m")
            {
                var path = Path.Combine(cacheDir, symbol + "_15m.json");
                if (!File.Exists(path))
                {
                    continue;
                }

                var raw = Read(path);
                var built = To30(raw);
                if (built.N > 500)
                {
                    built.Symbol = symbol;
                    Mark(built);
                    list.Add(built);
                }

                continue;
            }

            var file = Path.Combine(cacheDir, $"{symbol}_{tf}.json");
            if (!File.Exists(file))
            {
                continue;
            }

            var series = Read(file);
            series.Symbol = symbol;
            if (series.N > 500)
            {
                Mark(series);
                list.Add(series);
            }
        }

        return list;
    }

    private static void Mark(Series s)
    {
        var rsi = Rsi(s.C, 14);
        s.Side = new sbyte[s.N];
        for (var i = 20; i < s.N; i++)
        {
            var range = s.H[i] - s.L[i];
            var pos = range > 0 ? (s.C[i] - s.L[i]) / range : 0.5f;
            var ret = s.C[i - 1] > 0 ? s.C[i] / s.C[i - 1] - 1f : 0;
            if (ret < 0 && pos < 0.35f && rsi[i] < 45f)
            {
                s.Side[i] = 1;
                s.Fade.Add(i);
            }
            else if (ret > 0 && pos > 0.65f && rsi[i] > 55f)
            {
                s.Side[i] = -1;
                s.Fade.Add(i);
                s.Follow.Add(i);
            }

            if (s.Side[i] == 1)
            {
                s.Follow.Add(i);
            }

            if (i >= 8 && s.C[i - 8] > 0 && s.C[i] / s.C[i - 8] - 1f > 0 && ret > 0.0015f && pos > 0.70f && rsi[i] > 60f)
            {
                s.Strong.Add(i);
            }
        }
    }

    private static Series To30(Series m)
    {
        var t = new List<long>();
        var o = new List<float>();
        var h = new List<float>();
        var l = new List<float>();
        var c = new List<float>();
        for (var i = 0; i < m.N - 1; i++)
        {
            if (m.T[i + 1] - m.T[i] != 900_000)
            {
                continue;
            }

            var minute = DateTimeOffset.FromUnixTimeMilliseconds(m.T[i]).Minute;
            if (minute is not (0 or 30))
            {
                continue;
            }

            t.Add(m.T[i]);
            o.Add(m.O[i]);
            h.Add(Math.Max(m.H[i], m.H[i + 1]));
            l.Add(Math.Min(m.L[i], m.L[i + 1]));
            c.Add(m.C[i + 1]);
            i++;
        }

        return new Series { T = t.ToArray(), O = o.ToArray(), H = h.ToArray(), L = l.ToArray(), C = c.ToArray() };
    }

    private static Series Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var t = new List<long>(20000);
        var o = new List<float>(20000);
        var h = new List<float>(20000);
        var l = new List<float>(20000);
        var c = new List<float>(20000);
        long ot = 0;
        float ov = 0, hv = 0, lv = 0, cv = 0;
        var inObj = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                inObj = true;
                ot = 0;
                ov = hv = lv = cv = 0;
            }
            else if (reader.TokenType == JsonTokenType.EndObject && inObj)
            {
                if (ov > 0 && hv > 0 && lv > 0 && cv > 0)
                {
                    t.Add(ot);
                    o.Add(ov);
                    h.Add(hv);
                    l.Add(lv);
                    c.Add(cv);
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
                        ot = reader.GetDateTimeOffset().ToUnixTimeMilliseconds();
                        break;
                    case "Open":
                        ov = Num(ref reader);
                        break;
                    case "High":
                        hv = Num(ref reader);
                        break;
                    case "Low":
                        lv = Num(ref reader);
                        break;
                    case "Close":
                        cv = Num(ref reader);
                        break;
                }
            }
        }

        return new Series { T = t.ToArray(), O = o.ToArray(), H = h.ToArray(), L = l.ToArray(), C = c.ToArray() };
    }

    private static float Num(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number ? (float)reader.GetDouble() : 0;

    private static float[] Rsi(float[] c, int n)
    {
        var y = new float[c.Length];
        Array.Fill(y, 50f);
        double ag = 0, al = 0;
        for (var i = 1; i < c.Length; i++)
        {
            var ch = c[i] - c[i - 1];
            var g = Math.Max(ch, 0);
            var loss = Math.Max(-ch, 0);
            if (i <= n)
            {
                ag += g;
                al += loss;
                if (i == n)
                {
                    ag /= n;
                    al /= n;
                    y[i] = al == 0 ? 100 : (float)(100 - 100 / (1 + ag / al));
                }

                continue;
            }

            ag = (ag * (n - 1) + g) / n;
            al = (al * (n - 1) + loss) / n;
            y[i] = al == 0 ? 100 : (float)(100 - 100 / (1 + ag / al));
        }

        return y;
    }

    private readonly record struct Hit(string Tf, bool Fade, double Sl, double Tp, int N, double PerDay, double Mean, double Pf, double Win, double Sum, double Dd);
    private readonly record struct Stat(int N, double Mean, double Pf, double Win, double Sum, double Dd);

    private sealed class Series
    {
        public string Symbol = "";
        public long[] T = [];
        public float[] O = [];
        public float[] H = [];
        public float[] L = [];
        public float[] C = [];
        public sbyte[] Side = [];
        public List<int> Fade = [];
        public List<int> Follow = [];
        public List<int> Strong = [];
        public int N => C.Length;
    }
}
