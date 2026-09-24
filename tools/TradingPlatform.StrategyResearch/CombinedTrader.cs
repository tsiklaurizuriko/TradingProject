using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Three trader books on the 1h cache. Rules are fixed in this file before the run.
/// Entry is the next hour's open. Round trip cost is 12 bp (fee 4 bp + slip 2 bp, each side).
/// A same-bar stop and target counts as the stop.
/// </summary>
internal static class CombinedTrader
{
    private const double Cost = 0.0012;
    private const double Stop = 0.06;
    private const double Target = 0.10;
    private const int Hold = 24;
    private const int Warmup = 220;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root, string cacheDir)
    {
        Console.WriteLine("Combined trader books. Research replay. No orders.");
        var coins = Load(cacheDir);
        var btc = coins.First(c => c.Symbol == "BTCUSDT");
        Console.WriteLine($"coins {coins.Count} btc bars {btc.N}");

        var a = BookA(coins, btc, longOnly: false);
        var aLong = BookA(coins, btc, longOnly: true);
        var b = BookB(coins, stop: Stop, target: Target);
        var bTime = BookB(coins, stop: 0, target: 0);
        var bWide = BookB(coins, stop: 0.15, target: 0.10);
        var c = BookC(coins, btc);
        var text = Report(coins.Count, [a, aLong, b, bTime, bWide, c]);
        var path = Path.Combine(root, "artifacts", "strategy-research", "combined-trader.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        return 0;
    }

    public static int RunLiquid(string root, string cacheDir)
    {
        Console.WriteLine("Liquid reversal. Top 40 by 24h quote volume. Next open. 12bp if replaced.");
        var coins = Load(cacheDir);
        var btc = coins.First(c => c.Symbol == "BTCUSDT");
        Book[] books =
        [
            Liquid(coins, btc, 1, 4, false),
            Liquid(coins, btc, 1, 12, false),
            Liquid(coins, btc, 1, 24, false),
            Liquid(coins, btc, 4, 12, false),
            Liquid(coins, btc, 4, 24, false),
            Liquid(coins, btc, 1, 12, true),
            Liquid(coins, btc, 1, 24, true),
            Hysteresis(coins, btc),
            Universe(coins, btc, 12, longOnly: false),
            Universe(coins, btc, 24, longOnly: false),
            Universe(coins, btc, 12, longOnly: true)
        ];
        var text = Report(coins.Count, books);
        var path = Path.Combine(root, "artifacts", "strategy-research", "liquid-reversal.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        return 0;
    }

    private static Book Liquid(List<Coin> coins, Coin btc, int signalBars, int hold, bool highDisp)
    {
        var book = new Book($"L ret{signalBars}h hold{hold}h" + (highDisp ? " high-disp" : ""));
        var past = new List<double>();
        for (var i = Math.Max(Warmup, 48); i < btc.N - hold - 2; i += hold)
        {
            var rows = LiquidRows(coins, btc.T[i], signalBars);
            if (rows.Count < 40)
            {
                continue;
            }

            var disp = Std(rows.Select(r => (double)r.Ret));
            var gate = true;
            if (highDisp)
            {
                if (past.Count >= 60)
                {
                    var med = Median(past);
                    gate = disp > med;
                }
                else
                {
                    gate = false;
                }
            }

            past.Add(disp);
            if (!gate)
            {
                continue;
            }

            var legL = new List<double>();
            var legS = new List<double>();
            foreach (var row in rows.Take(4))
            {
                if (Forward(row.Coin, row.Index, hold, +1, out var net))
                {
                    legL.Add(net - Cost);
                }
            }

            foreach (var row in rows.TakeLast(4))
            {
                if (Forward(row.Coin, row.Index, hold, -1, out var net))
                {
                    legS.Add(net - Cost);
                }
            }

            if (legL.Count == 0 || legS.Count == 0)
            {
                continue;
            }

            book.Add(btc.When(i), 0.5 * legL.Average() + 0.5 * legS.Average());
        }

        return book;
    }

    private static Book Universe(List<Coin> coins, Coin btc, int hold, bool longOnly)
    {
        var book = new Book(longOnly ? $"U ret1h hold{hold}h long" : $"U ret1h hold{hold}h");
        for (var i = Math.Max(Warmup, 48); i < btc.N - hold - 2; i += hold)
        {
            var rows = Rows(coins, btc.T[i], static (c, k) => Ret(c, k, 1));
            if (rows.Count < 30)
            {
                continue;
            }

            var n = Math.Max(1, rows.Count / 10);
            var legL = new List<double>();
            var legS = new List<double>();
            foreach (var row in rows.Take(n))
            {
                if (Forward(row.Coin, row.Index, hold, +1, out var gross))
                {
                    legL.Add(gross - Cost);
                }
            }

            foreach (var row in rows.TakeLast(n))
            {
                if (Forward(row.Coin, row.Index, hold, -1, out var gross))
                {
                    legS.Add(gross - Cost);
                }
            }

            if (legL.Count == 0 || (!longOnly && legS.Count == 0))
            {
                continue;
            }

            book.Add(btc.When(i), longOnly ? legL.Average() : 0.5 * legL.Average() + 0.5 * legS.Average());
        }

        return book;
    }

    private static Book Hysteresis(List<Coin> coins, Coin btc)
    {
        var book = new Book("H ret1h step4h keep outer 30%");
        const int step = 4;
        var held = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = Math.Max(Warmup, 48); i < btc.N - step - 2; i += step)
        {
            var rows = LiquidRows(coins, btc.T[i], 1);
            if (rows.Count < 40)
            {
                held.Clear();
                continue;
            }

            var rank = new Dictionary<string, int>(rows.Count, StringComparer.Ordinal);
            for (var r = 0; r < rows.Count; r++)
            {
                rank[rows[r].Coin.Symbol] = r;
            }

            var next = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (symbol, side) in held)
            {
                if (!rank.TryGetValue(symbol, out var r))
                {
                    continue;
                }

                var pct = r / (double)(rows.Count - 1);
                if (side > 0 && pct <= 0.30)
                {
                    next[symbol] = side;
                }
                else if (side < 0 && pct >= 0.70)
                {
                    next[symbol] = side;
                }
            }

            foreach (var row in rows)
            {
                if (next.Count(kv => kv.Value > 0) >= 4)
                {
                    break;
                }

                next.TryAdd(row.Coin.Symbol, +1);
            }

            for (var r = rows.Count - 1; r >= 0; r--)
            {
                if (next.Count(kv => kv.Value < 0) >= 4)
                {
                    break;
                }

                var symbol = rows[r].Coin.Symbol;
                if (next.TryGetValue(symbol, out var side) && side > 0)
                {
                    continue;
                }

                next[symbol] = -1;
            }

            var bySymbol = rows.ToDictionary(r => r.Coin.Symbol, r => r, StringComparer.Ordinal);
            var legL = new List<double>();
            var legS = new List<double>();
            foreach (var (symbol, side) in next)
            {
                if (!bySymbol.TryGetValue(symbol, out var row))
                {
                    continue;
                }

                if (!Forward(row.Coin, row.Index, step, side, out var gross))
                {
                    continue;
                }

                var entered = !held.TryGetValue(symbol, out var prev) || prev != side;
                var net = gross - (entered ? Cost * 0.5 : 0);
                if (side > 0)
                {
                    legL.Add(net);
                }
                else
                {
                    legS.Add(net);
                }
            }

            foreach (var (symbol, side) in held)
            {
                if (!next.ContainsKey(symbol))
                {
                    if (side > 0 && legL.Count > 0)
                    {
                        legL[0] -= Cost * 0.5;
                    }
                    else if (side < 0 && legS.Count > 0)
                    {
                        legS[0] -= Cost * 0.5;
                    }
                }
            }

            held.Clear();
            foreach (var kv in next)
            {
                held[kv.Key] = kv.Value;
            }

            if (legL.Count == 0 || legS.Count == 0)
            {
                continue;
            }

            book.Add(btc.When(i), 0.5 * legL.Average() + 0.5 * legS.Average());
        }

        return book;
    }

    private static List<LiqRow> LiquidRows(List<Coin> coins, long ts, int signalBars)
    {
        var rows = new List<LiqRow>(coins.Count);
        foreach (var coin in coins)
        {
            var k = coin.IndexOf(ts);
            if (k < 24)
            {
                continue;
            }

            var ret = Ret(coin, k, signalBars);
            if (float.IsNaN(ret))
            {
                continue;
            }

            double qv = 0;
            for (var j = 0; j < 24; j++)
            {
                qv += coin.V[k - j] * coin.C[k - j];
            }

            rows.Add(new LiqRow(coin, k, ret, qv));
        }

        rows.Sort(static (a, b) => b.Qv.CompareTo(a.Qv));
        var top = rows.Take(40).ToList();
        top.Sort(static (a, b) =>
        {
            var cmp = a.Ret.CompareTo(b.Ret);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Coin.Symbol, b.Coin.Symbol);
        });
        return top;
    }

    private static bool Forward(Coin c, int signal, int hold, int side, out double gross)
    {
        gross = 0;
        var entry = signal + 1;
        var exit = entry + hold;
        if (exit >= c.N || c.O[entry] <= 0 || c.O[exit] <= 0)
        {
            return false;
        }

        gross = side * (c.O[exit] / c.O[entry] - 1d);
        return true;
    }

    private static double Std(IEnumerable<double> values)
    {
        var list = values.ToList();
        var mean = list.Average();
        var var = list.Sum(v => (v - mean) * (v - mean)) / list.Count;
        return Math.Sqrt(var);
    }

    private static double Median(List<double> values)
    {
        var arr = values.ToArray();
        Array.Sort(arr);
        var mid = arr.Length / 2;
        return arr.Length % 2 == 1 ? arr[mid] : 0.5 * (arr[mid - 1] + arr[mid]);
    }

    private readonly record struct LiqRow(Coin Coin, int Index, float Ret, double Qv);

    private static Book BookA(List<Coin> coins, Coin btc, bool longOnly)
    {
        var book = new Book(longOnly ? "A2 long only reversal" : "A slow reversal");
        for (var i = Warmup; i < btc.N - Hold - 2; i++)
        {
            var when = btc.When(i);
            if (when.Hour != 0)
            {
                continue;
            }

            var rows = Rows(coins, btc.T[i], static (c, k) => Ret(c, k, 1));
            if (rows.Count < 30)
            {
                continue;
            }

            var n = Math.Max(1, rows.Count / 10);
            var longs = rows.Take(n).ToList();
            var shorts = rows.TakeLast(n).ToList();
            var leg = new List<double>();
            foreach (var row in longs)
            {
                if (HoldReturn(row.Coin, row.Index, +1, 0, 0, out var net))
                {
                    leg.Add(net);
                }
            }

            var longMean = leg.Count == 0 ? double.NaN : leg.Average();
            leg.Clear();
            foreach (var row in shorts)
            {
                if (HoldReturn(row.Coin, row.Index, -1, 0, 0, out var net))
                {
                    leg.Add(net);
                }
            }

            if (double.IsNaN(longMean) || (!longOnly && leg.Count == 0))
            {
                continue;
            }

            book.Add(when, longOnly ? longMean : 0.5 * longMean + 0.5 * leg.Average());
        }

        return book;
    }

    private static Book BookB(List<Coin> coins, double stop, double target)
    {
        var book = new Book(stop == 0 && target == 0
            ? "B2 exhaustion 24h no stop"
            : stop > 0.10
                ? "B3 exhaustion stop 15% target 10%"
                : "B exhaustion short");
        foreach (var coin in coins)
        {
            var busy = Warmup;
            for (var i = Warmup; i < coin.N - Hold - 2; i++)
            {
                if (i < busy || !Exhaustion(coin, i))
                {
                    continue;
                }

                if (!HoldReturn(coin, i, -1, stop, target, out var net))
                {
                    continue;
                }

                book.Add(coin.When(i), net);
                busy = i + Hold;
            }
        }

        return book;
    }

    private static Book BookC(List<Coin> coins, Coin btc)
    {
        var book = new Book("C combined");
        for (var i = Warmup; i < btc.N - Hold - 2; i++)
        {
            var when = btc.When(i);
            if (when.Hour != 0)
            {
                continue;
            }

            var ret20 = Rows(coins, btc.T[i], static (c, k) => Ret(c, k, 20));
            if (ret20.Count < 30)
            {
                continue;
            }

            var ret1 = Rows(coins, btc.T[i], static (c, k) => Ret(c, k, 1));
            var topCut = ret20[(int)(ret20.Count * 0.80)].Value;
            var n = Math.Max(1, ret1.Count / 10);
            var shorts = ret20
                .Where(r => r.Value >= topCut && Exhaustion(r.Coin, r.Index))
                .OrderByDescending(r => r.Value)
                .Take(5)
                .ToList();
            var shortNames = shorts.Select(r => r.Coin.Symbol).ToHashSet(StringComparer.Ordinal);
            var longs = ret1
                .Take(n)
                .Where(r => !shortNames.Contains(r.Coin.Symbol) && Bounce(r.Coin, r.Index) && Ret(r.Coin, r.Index, 20) > -0.20f)
                .Take(5)
                .ToList();

            var leg = new List<double>();
            foreach (var row in longs)
            {
                if (HoldReturn(row.Coin, row.Index, +1, Stop, Target, out var net))
                {
                    leg.Add(net);
                }
            }

            var longMean = leg.Count == 0 ? 0d : leg.Average();
            var longOn = leg.Count > 0;
            leg.Clear();
            foreach (var row in shorts)
            {
                if (HoldReturn(row.Coin, row.Index, -1, Stop, Target, out var net))
                {
                    leg.Add(net);
                }
            }

            var shortOn = leg.Count > 0;
            if (!longOn && !shortOn)
            {
                continue;
            }

            var port = (longOn ? 0.5 * longMean : 0d) + (shortOn ? 0.5 * leg.Average() : 0d);
            book.Add(when, port);
        }

        return book;
    }

    private static bool Bounce(Coin c, int i)
    {
        var range = c.H[i] - c.L[i];
        var pos = range > 0 ? (c.C[i] - c.L[i]) / range : 0.5f;
        return pos >= 0.50f;
    }

    private static bool Exhaustion(Coin c, int i)
    {
        if (i < 20 || float.IsNaN(c.Rsi[i]) || float.IsNaN(c.Vz[i]) || float.IsNaN(c.VolPct[i]))
        {
            return false;
        }

        var ret1 = Ret(c, i, 1);
        var ret20 = Ret(c, i, 20);
        var range = c.H[i] - c.L[i];
        var pos = range > 0 ? (c.C[i] - c.L[i]) / range : 0.5f;
        var hard = ret20 > 0.20f && c.Rsi[i] >= 75f && c.Vz[i] >= 1.5f && pos < 0.40f;
        var fail = ret20 > 0.15f && c.Rsi[i] >= 70f && ret1 < 0f && c.VolPct[i] >= 0.75f;
        return hard || fail;
    }

    private static bool HoldReturn(Coin c, int signal, int side, double stop, double target, out double net)
    {
        net = 0;
        var entry = signal + 1;
        var exit = entry + Hold;
        if (exit >= c.N || c.O[entry] <= 0)
        {
            return false;
        }

        var px = c.O[entry];
        if (stop > 0 || target > 0)
        {
            for (var k = entry; k < exit; k++)
            {
                var up = c.H[k] / px - 1d;
                var dn = 1d - c.L[k] / px;
                var stopHit = stop > 0 && (side > 0 ? dn >= stop : up >= stop);
                var targetHit = target > 0 && (side > 0 ? up >= target : dn >= target);
                if (stopHit)
                {
                    net = -stop - Cost;
                    return true;
                }

                if (targetHit)
                {
                    net = target - Cost;
                    return true;
                }
            }
        }

        if (c.O[exit] <= 0)
        {
            return false;
        }

        net = side * (c.O[exit] / px - 1d) - Cost;
        return true;
    }

    private static List<Row> Rows(List<Coin> coins, long ts, Func<Coin, int, float> feature)
    {
        var rows = new List<Row>(coins.Count);
        foreach (var coin in coins)
        {
            var k = coin.IndexOf(ts);
            if (k < 20)
            {
                continue;
            }

            var value = feature(coin, k);
            if (float.IsNaN(value))
            {
                continue;
            }

            rows.Add(new Row(coin, k, value));
        }

        rows.Sort(static (a, b) =>
        {
            var cmp = a.Value.CompareTo(b.Value);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.Coin.Symbol, b.Coin.Symbol);
        });
        return rows;
    }

    private static float Ret(Coin c, int i, int bars)
    {
        if (i < bars || c.C[i - bars] <= 0)
        {
            return float.NaN;
        }

        return c.C[i] / c.C[i - bars] - 1f;
    }

    private static List<Coin> Load(string cacheDir)
    {
        var files = Directory.GetFiles(cacheDir, "*_1h.json");
        var bag = new System.Collections.Concurrent.ConcurrentBag<Coin>();
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
            if (coin.N >= 1500)
            {
                bag.Add(coin);
            }
        });

        return bag.OrderBy(c => c.Symbol, StringComparer.Ordinal).ToList();
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
        coin.VolPct = Pct(coin.V, 50);
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

    private static float[] Pct(float[] x, int n)
    {
        var y = new float[x.Length];
        Array.Fill(y, float.NaN);
        for (var i = n - 1; i < x.Length; i++)
        {
            var below = 0;
            for (var k = i - n + 1; k <= i; k++)
            {
                if (x[k] < x[i])
                {
                    below++;
                }
            }

            y[i] = below / (float)n;
        }

        return y;
    }

    private static string Report(int coins, params Book[] books)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"coins {coins}");
        sb.AppendLine("cost 12bp round trip. stop 6%. target 10%. hold 24h. rebalance 00:00 UTC.");
        sb.AppendLine("A: long bottom decile of 1h return, short top decile, hold the open 24h later. No stop.");
        sb.AppendLine("B: short when DOWN_EXHAUSTION or DOWN_FAILED_EXTENSION fires. Path stop/target, else 24h.");
        sb.AppendLine("C: at 00:00 UTC, short up to 5 names that are both exhausted and in the top 20% of 20h return. Long up to 5 names from the bottom decile of 1h return whose hour closed in the upper half and whose 20h return is above -20%. Half capital on each side. Same path exit.");
        foreach (var book in books)
        {
            sb.AppendLine();
            sb.AppendLine(book.Name);
            foreach (var (label, slice) in book.Slices())
            {
                sb.AppendLine($"  {label} n={slice.N} mean={Fmt(slice.Mean)} pf={Fmt(slice.Pf)} win={Fmt(slice.Win)} sum={Fmt(slice.Sum)} dd={Fmt(slice.Dd)}");
            }
        }

        return sb.ToString();
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "NA" : v.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed class Book
    {
        public Book(string name) => Name = name;
        public string Name { get; }
        private readonly List<(DateTimeOffset When, double Net)> _rows = [];
        public void Add(DateTimeOffset when, double net) => _rows.Add((when, net));

        public IEnumerable<(string Label, Slice Slice)> Slices()
        {
            yield return ("ALL", Slice(_rows));
            yield return ("IS", Slice(_rows.Where(r => r.When < IsEnd)));
            yield return ("VAL", Slice(_rows.Where(r => r.When >= IsEnd && r.When < ValEnd)));
            yield return ("OOS", Slice(_rows.Where(r => r.When >= ValEnd)));
        }

        private static Slice Slice(IEnumerable<(DateTimeOffset When, double Net)> rows)
        {
            var list = rows.Select(r => r.Net).ToList();
            if (list.Count == 0)
            {
                return new Slice(0, double.NaN, double.NaN, double.NaN, 0, double.NaN);
            }

            var wins = list.Where(x => x > 0).Sum();
            var losses = list.Where(x => x < 0).Sum();
            var pf = losses == 0 ? double.PositiveInfinity : wins / Math.Abs(losses);
            var eq = 0d;
            var peak = 0d;
            var dd = 0d;
            foreach (var x in list)
            {
                eq += x;
                peak = Math.Max(peak, eq);
                dd = Math.Max(dd, peak - eq);
            }

            return new Slice(list.Count, list.Average(), pf, list.Count(x => x > 0) / (double)list.Count, list.Sum(), dd);
        }
    }

    private readonly record struct Slice(int N, double Mean, double Pf, double Win, double Sum, double Dd);
    private readonly record struct Row(Coin Coin, int Index, float Value);

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
        public float[] VolPct { get; set; } = [];
        public int N => C.Length;
        public DateTimeOffset When(int i) => DateTimeOffset.FromUnixTimeMilliseconds(T[i]);

        public int IndexOf(long ts)
        {
            var k = Array.BinarySearch(T, ts);
            return k >= 0 ? k : -1;
        }
    }
}
