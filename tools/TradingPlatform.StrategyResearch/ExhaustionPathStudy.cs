using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Path replay of the frozen DOWN_EXHAUSTION flag. Research only. No orders.
/// The rule is not refit. Entry is the next hour open. Exit is the open 24 hours later.
/// </summary>
internal static class ExhaustionPathStudy
{
    private const int Hold = 24;
    private const int Warmup = 220;
    private const double Cost1 = 0.0012;
    private const double Cost15 = 0.0018;
    private const double Cost2 = 0.0024;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root, string cacheDir)
    {
        CheckRule();
        Console.WriteLine("DOWN_EXHAUSTION path replay. Research only. No orders.");
        var files = Directory.GetFiles(cacheDir, "*_1h.json");
        var bag = new ConcurrentBag<Trade>();
        var books = 0;
        Parallel.ForEach(files, file =>
        {
            var symbol = Path.GetFileNameWithoutExtension(file);
            var cut = symbol.LastIndexOf("_1h", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
            {
                symbol = symbol[..cut];
            }

            if (Skip(symbol))
            {
                return;
            }

            var coin = Read(symbol, file);
            if (coin.N < 1500)
            {
                return;
            }

            Interlocked.Increment(ref books);
            foreach (var trade in Replay(coin))
            {
                bag.Add(trade);
            }
        });

        var trades = bag.OrderBy(t => t.When).ThenBy(t => t.Symbol, StringComparer.Ordinal).ToList();
        var text = Report(books, trades);
        var path = Path.Combine(root, "docs", "EXHAUSTION_PATH.md");
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    private static void CheckRule()
    {
        if (!Fire(0.21f, 80f, 2f, 0.10f))
        {
            throw new InvalidOperationException("DOWN_EXHAUSTION missed a bar inside the frozen rule.");
        }

        if (Fire(0.20f, 80f, 2f, 0.10f) || Fire(0.21f, 74.9f, 2f, 0.10f) || Fire(0.21f, 80f, 1.49f, 0.10f) || Fire(0.21f, 80f, 2f, 0.40f))
        {
            throw new InvalidOperationException("DOWN_EXHAUSTION accepted a bar outside the frozen rule.");
        }
    }

    private static bool Fire(float ret20, float rsi, float vz, float closePos) =>
        ret20 > 0.20f && rsi >= 75f && vz >= 1.5f && closePos < 0.40f;

    private static IEnumerable<Trade> Replay(Coin coin)
    {
        var next = Warmup;
        for (var i = Warmup; i < coin.N - Hold - 2; i++)
        {
            if (i < next || !Signal(coin, i))
            {
                continue;
            }

            var entry = i + 1;
            var exit = entry + Hold;
            var px = coin.O[entry];
            var outPx = coin.O[exit];
            if (px <= 0 || outPx <= 0)
            {
                continue;
            }

            var mae = 0d;
            var mfe = 0d;
            var label = false;
            var signalClose = coin.C[i];
            for (var k = entry; k < exit; k++)
            {
                mae = Math.Max(mae, coin.H[k] / px - 1d);
                mfe = Math.Max(mfe, 1d - coin.L[k] / px);
                if (signalClose > 0 && coin.L[k] / signalClose - 1d <= -0.15d)
                {
                    label = true;
                }
            }

            var gross = 1d - outPx / px;
            var gap = coin.T[exit] - coin.T[entry] != Hold * 3_600_000L;
            yield return new Trade(coin.Symbol, coin.When(i), gross, mae, mfe, mfe >= 0.15d, label, gap);
            next = i + Hold;
        }
    }

    private static bool Signal(Coin c, int i)
    {
        if (i < 48 || c.C[i - 20] <= 0 || float.IsNaN(c.Rsi[i]) || float.IsNaN(c.Vz[i]))
        {
            return false;
        }

        var ret20 = c.C[i] / c.C[i - 20] - 1f;
        var range = c.H[i] - c.L[i];
        var pos = range > 0 ? (c.C[i] - c.L[i]) / range : 0.5f;
        return Fire(ret20, c.Rsi[i], c.Vz[i], pos);
    }

    private static string Report(int books, List<Trade> trades)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Exhaustion path replay");
        sb.AppendLine();
        sb.AppendLine("Research replay only. No Paper bot, no live order. The rule is the frozen `DOWN_EXHAUSTION` flag. Thresholds were not searched again.");
        sb.AppendLine();
        sb.AppendLine($"Coins: {books}. Clock: closed 1h bars. One short at a time per coin. The next entry waits 24 bars.");
        sb.AppendLine();
        sb.AppendLine("Signal, on the closed hour: 20-hour return > +20%, RSI(14) >= 75, volume z over 48 hours >= 1.5, close in the bottom 40% of that hour. Entry is the next hour's open. Exit is the open 24 hours later. There is no stop and no take-profit in this replay.");
        sb.AppendLine();
        sb.AppendLine("A short's gross return is (entry − exit) / entry. Cost is taker fee 0.04% plus slippage 0.02% per side. Round trip at 1x is 0.12%. Stress uses 1.5x and 2x that cost. Funding is not in the 1h file and is not invented.");
        sb.AppendLine();
        sb.AppendLine("MAE is the worst high against the short, measured from the entry. MFE is the best low in favor of the short. Touched −15% means that low reached 15% under the entry. Label hit means a low reached 15% under the signal close inside the same 24 bars. That label is the old classification, not the trade.");
        sb.AppendLine();
        sb.AppendLine("| Split | Trades | Coins | Top coin share | Clock gaps | Win rate 1x | Mean gross | Mean net 1x | Mean net 1.5x | Mean net 2x | Median net 1x | Median MAE | Median MFE | Touch −15% | Label hit | Mean gross on touch | Mean gross on miss |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var (name, slice) in new[]
        {
            ("IS", trades.Where(t => t.When < IsEnd).ToList()),
            ("VAL", trades.Where(t => t.When >= IsEnd && t.When < ValEnd).ToList()),
            ("OOS", trades.Where(t => t.When >= ValEnd).ToList())
        })
        {
            sb.AppendLine("| " + name + " | " + Row(slice) + " |");
        }

        sb.AppendLine();
        var oos = trades.Where(t => t.When >= ValEnd).ToList();
        var isOk = MeanNet(trades.Where(t => t.When < IsEnd).ToList(), Cost15) > 0;
        var valOk = MeanNet(trades.Where(t => t.When >= IsEnd && t.When < ValEnd).ToList(), Cost15) > 0;
        var oosOk = MeanNet(oos, Cost15) > 0;
        sb.AppendLine(isOk && valOk && oosOk
            ? "IS, VAL, and OOS mean net stay positive at 1.5x cost. This is still a research replay. It is not a Paper approval."
            : "The path does not stay positive at 1.5x cost on every window. This is not a reason to place orders.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static string Row(List<Trade> rows)
    {
        if (rows.Count == 0)
        {
            return "0 | 0 | NA | NA | NA | NA | NA | NA | NA | NA | NA | NA | NA | NA | NA | NA";
        }

        var byCoin = rows.GroupBy(t => t.Symbol).Select(g => g.Count()).ToList();
        var touch = rows.Where(t => t.Touch).ToList();
        var miss = rows.Where(t => !t.Touch).ToList();
        return string.Join(" | ",
            rows.Count.ToString(CultureInfo.InvariantCulture),
            byCoin.Count.ToString(CultureInfo.InvariantCulture),
            Fmt(byCoin.Max() / (double)rows.Count),
            Fmt(rows.Count(t => t.Gap) / (double)rows.Count),
            Fmt(rows.Count(t => t.Gross > Cost1) / (double)rows.Count),
            Fmt(rows.Average(t => t.Gross)),
            Fmt(MeanNet(rows, Cost1)),
            Fmt(MeanNet(rows, Cost15)),
            Fmt(MeanNet(rows, Cost2)),
            Fmt(Median(rows.Select(t => t.Gross - Cost1).ToList())),
            Fmt(Median(rows.Select(t => t.Mae).ToList())),
            Fmt(Median(rows.Select(t => t.Mfe).ToList())),
            Fmt(touch.Count / (double)rows.Count),
            Fmt(rows.Count(t => t.Label) / (double)rows.Count),
            Fmt(touch.Count == 0 ? double.NaN : touch.Average(t => t.Gross)),
            Fmt(miss.Count == 0 ? double.NaN : miss.Average(t => t.Gross)));
    }

    private static double MeanNet(List<Trade> rows, double cost) =>
        rows.Count == 0 ? double.NaN : rows.Average(t => t.Gross) - cost;

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        var arr = values.ToArray();
        Array.Sort(arr);
        var mid = arr.Length / 2;
        return arr.Length % 2 == 1 ? arr[mid] : 0.5 * (arr[mid - 1] + arr[mid]);
    }

    private static bool Skip(string symbol) => symbol is
        "BTCDOMUSDT" or "USDCUSDT" or "FDUSDUSDT" or "TUSDUSDT" or "USDPUSDT" or "DAIUSDT" or "EURUSDT" or "BUSDUSDT";

    private static Coin Read(string symbol, string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var t = new List<long>(20000);
        var o = new List<float>(20000);
        var h = new List<float>(20000);
        var l = new List<float>(20000);
        var c = new List<float>(20000);
        var v = new List<float>(20000);
        long ot = 0;
        float ov = 0, hv = 0, lv = 0, cv = 0, vv = 0;
        var inObj = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                inObj = true;
                ot = 0;
                ov = hv = lv = cv = vv = 0;
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
                    v.Add(vv);
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
                    case "Volume":
                        vv = Num(ref reader);
                        break;
                }
            }
        }

        var coin = new Coin
        {
            Symbol = symbol,
            T = t.ToArray(),
            O = o.ToArray(),
            H = h.ToArray(),
            L = l.ToArray(),
            C = c.ToArray(),
            V = v.ToArray()
        };
        coin.Rsi = Rsi(coin.C, 14);
        coin.Vz = Z(coin.V, 48);
        return coin;
    }

    private static float Num(ref Utf8JsonReader reader) =>
        reader.TokenType == JsonTokenType.Number ? (float)reader.GetDouble() : 0;

    private static float[] Rsi(float[] c, int n)
    {
        var y = new float[c.Length];
        Array.Fill(y, float.NaN);
        double ag = 0, al = 0;
        for (var i = 1; i < c.Length; i++)
        {
            var ch = c[i] - c[i - 1];
            var g = Math.Max(ch, 0);
            var loss = Math.Max(-ch, 0);
            if (i < n)
            {
                ag += g;
                al += loss;
                continue;
            }

            if (i == n)
            {
                ag = (ag + g) / n;
                al = (al + loss) / n;
            }
            else
            {
                ag = (ag * (n - 1) + g) / n;
                al = (al * (n - 1) + loss) / n;
            }

            y[i] = al == 0 ? 100 : (float)(100 - 100 / (1 + ag / al));
        }

        return y;
    }

    private static float[] Z(float[] x, int n)
    {
        var y = new float[x.Length];
        Array.Fill(y, float.NaN);
        for (var i = n - 1; i < x.Length; i++)
        {
            double sum = 0;
            for (var k = i - n + 1; k <= i; k++)
            {
                sum += x[k];
            }

            var mean = sum / n;
            double var = 0;
            for (var k = i - n + 1; k <= i; k++)
            {
                var d = x[k] - mean;
                var += d * d;
            }

            var sd = Math.Sqrt(var / n);
            y[i] = sd < 1e-8 ? 0 : (float)((x[i] - mean) / sd);
        }

        return y;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "NA" : v.ToString("0.####", CultureInfo.InvariantCulture);

    private readonly record struct Trade(string Symbol, DateTimeOffset When, double Gross, double Mae, double Mfe, bool Touch, bool Label, bool Gap);

    private sealed class Coin
    {
        public string Symbol { get; set; } = "";
        public long[] T { get; set; } = [];
        public float[] O { get; set; } = [];
        public float[] H { get; set; } = [];
        public float[] L { get; set; } = [];
        public float[] C { get; set; } = [];
        public float[] V { get; set; } = [];
        public float[] Rsi { get; set; } = [];
        public float[] Vz { get; set; } = [];
        public int N => C.Length;
        public DateTimeOffset When(int i) => DateTimeOffset.FromUnixTimeMilliseconds(T[i]);
    }
}
