using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Frozen combination rules for extreme-move early flags. Research only. No orders.
/// Parameters were written before this run and are not changed from OOS results.
/// </summary>
internal static class ExtremeMoveSignals
{
    private const int Horizon = 24;
    private const int Warmup = 220;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] TakerSymbols =
    [
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT",
        "1000PEPEUSDT", "WIFUSDT", "SUIUSDT", "NEARUSDT", "APTUSDT", "INJUSDT", "FILUSDT", "ARBUSDT", "OPUSDT", "TIAUSDT",
        "SEIUSDT", "WLDUSDT", "ORDIUSDT", "AAVEUSDT", "UNIUSDT", "DOTUSDT", "ATOMUSDT", "TRXUSDT", "BCHUSDT", "ETCUSDT"
    ];

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        Console.WriteLine("Extreme-move signal search. Research only. No orders.");
        var takerDir = Path.Combine(root, "artifacts", "strategy-research", "extreme-move", "taker-1h");
        Directory.CreateDirectory(takerDir);
        await DownloadTakerAsync(takerDir);
        var taker = LoadTaker(takerDir);

        var files = Directory.GetFiles(cacheDir, "*_1h.json");
        var rules = Rules();
        var agg = rules.ToDictionary(r => r.Id, _ => new Agg(), StringComparer.Ordinal);
        var books = 0;
        foreach (var file in files)
        {
            var symbol = Path.GetFileNameWithoutExtension(file);
            var cut = symbol.LastIndexOf("_1h", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
            {
                symbol = symbol[..cut];
            }

            var book = Read(file);
            if (book.N < 1500)
            {
                continue;
            }

            books++;
            taker.TryGetValue(symbol, out var flow);
            Evaluate(book, flow, rules, agg);
            if (books % 100 == 0)
            {
                Console.WriteLine($"scanned {books}");
            }
        }

        var path = Path.Combine(root, "docs", "EXTREME_MOVE_SIGNALS.md");
        File.WriteAllText(path, Report(books, taker.Count, agg, rules));
        Console.WriteLine("Wrote " + path);
        var keep = agg.Count(kv => Pass(kv.Value));
        Console.WriteLine($"rules passing IS+VAL+OOS: {keep}");
        return 0;
    }

    private static async Task DownloadTakerAsync(string takerDir)
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformExtremeMoveResearch/1.0");
        var end = DateTimeOffset.UtcNow;
        var start = end.AddDays(-730);
        foreach (var symbol in TakerSymbols)
        {
            var path = Path.Combine(takerDir, symbol + "_1h.json");
            if (File.Exists(path) && new FileInfo(path).Length > 100_000)
            {
                Console.WriteLine($"{symbol} taker cache hit");
                continue;
            }

            try
            {
                var (candles, _, downloaded) = await ResearchKlineCache.LoadAsync(http, takerDir, symbol, "1h", start, end, requireTaker: true);
                var good = candles.Count(c => c.TakerBuyVolume > 0m && c.TakerBuyVolume <= c.Volume);
                Console.WriteLine($"{symbol} taker bars={candles.Count} real={good} downloaded={downloaded}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{symbol} taker DATA_UNAVAILABLE {ex.Message}");
            }
        }
    }

    private static Dictionary<string, float[]> LoadTaker(string dir)
    {
        var map = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(dir))
        {
            return map;
        }

        foreach (var file in Directory.GetFiles(dir, "*_1h.json"))
        {
            var symbol = Path.GetFileName(file).Replace("_1h.json", "", StringComparison.OrdinalIgnoreCase);
            var book = Read(file);
            var good = 0;
            for (var i = 0; i < book.N; i++)
            {
                if (book.Tb[i] > 0 && book.Tb[i] <= book.V[i])
                {
                    good++;
                }
            }

            if (book.N > 500 && good > book.N * 0.8)
            {
                map[symbol] = book.Tb;
                map[symbol + "|v"] = book.V;
                map[symbol + "|t"] = book.OpenMs.Select(ms => (float)ms).ToArray();
            }
        }

        return map;
    }

    private static void Evaluate(Bar book, float[]? flowIgnored, Rule[] rules, Dictionary<string, Agg> agg)
    {
        _ = flowIgnored;
        var n = book.N;
        var rv = new float[n];
        var vz = new float[n];
        var rsi = Rsi(book.C, 14);
        var bb = new float[n];
        var atrPct = new float[n];
        var atr = Atr(book);
        for (var i = Warmup; i < n; i++)
        {
            rv[i] = Rv(book, i, 24);
            vz[i] = Z(book.V, i, 48);
            bb[i] = BbWidth(book.C, i, 20);
            atrPct[i] = Pct(atr, i, 100);
        }

        var rvPct = new float[n];
        var bbPct = new float[n];
        for (var i = Warmup; i < n; i++)
        {
            rvPct[i] = Pct(rv, i, 100);
            bbPct[i] = Pct(bb, i, 100);
        }

        var volPct = new float[n];
        for (var i = Warmup; i < n; i++)
        {
            volPct[i] = Pct(book.V, i, 100);
        }

        foreach (var rule in rules)
        {
            var bucket = agg[rule.Id];
            var i = Warmup;
            while (i < n - Horizon - 1)
            {
                if (!Fire(rule, book, i, rvPct, vz, rsi, bbPct, atrPct, volPct))
                {
                    i++;
                    continue;
                }

                var when = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i]);
                var split = when < IsEnd ? 0 : when < ValEnd ? 1 : 2;
                var hitAt = FirstHit(book, i, rule.Level, rule.Up);
                bucket.Fires[split]++;
                if (hitAt > 0)
                {
                    bucket.Hits[split]++;
                    bucket.Lead[split].Add(hitAt);
                }

                var exc = MaxExc(book, i, rule.Up);
                bucket.Exc[split].Add(exc);
                i += Math.Max(hitAt, 1) + 6;
            }

            MarkEpisodes(book, rule, bucket, rvPct, vz, rsi, bbPct, atrPct, volPct);
        }
    }

    private static void MarkEpisodes(Bar book, Rule rule, Agg bucket, float[] rvPct, float[] vz, float[] rsi, float[] bbPct, float[] atrPct, float[] volPct)
    {
        var i = Warmup;
        while (i < book.N - Horizon - 1)
        {
            var hit = FirstHit(book, i, rule.Level, rule.Up);
            if (hit < 0)
            {
                i++;
                continue;
            }

            var when = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i]);
            var split = when < IsEnd ? 0 : when < ValEnd ? 1 : 2;
            bucket.Events[split]++;
            var caught = false;
            for (var k = 0; k < hit; k++)
            {
                var idx = i - k;
                if (idx < Warmup)
                {
                    break;
                }

                if (Fire(rule, book, idx, rvPct, vz, rsi, bbPct, atrPct, volPct))
                {
                    caught = true;
                    break;
                }
            }

            if (!caught)
            {
                for (var back = 1; back <= 4 && i - back >= Warmup; back++)
                {
                    if (Fire(rule, book, i - back, rvPct, vz, rsi, bbPct, atrPct, volPct))
                    {
                        caught = true;
                        break;
                    }
                }
            }

            if (caught)
            {
                bucket.Caught[split]++;
            }

            var maxBar = i + Math.Max(hit, 1);
            i = maxBar + 24;
        }
    }

    private static bool Fire(Rule rule, Bar book, int i, float[] rvPct, float[] vz, float[] rsi, float[] bbPct, float[] atrPct, float[] volPct)
    {
        if (i < 1 || book.C[i - 1] <= 0)
        {
            return false;
        }

        var ret1 = book.C[i] / book.C[i - 1] - 1f;
        var ret3 = i >= 3 && book.C[i - 3] > 0 ? book.C[i] / book.C[i - 3] - 1f : float.NaN;
        var ret5 = i >= 5 && book.C[i - 5] > 0 ? book.C[i] / book.C[i - 5] - 1f : float.NaN;
        var ret20 = i >= 20 && book.C[i - 20] > 0 ? book.C[i] / book.C[i - 20] - 1f : float.NaN;
        var range = book.H[i] - book.L[i];
        var closePos = range > 0 ? (book.C[i] - book.L[i]) / range : 0.5f;
        return rule.Id switch
        {
            "UP_VOL_VOLUME" => rvPct[i] >= 0.80f && vz[i] >= 1f && ret1 > 0f && ret1 < 0.08f,
            "UP_SQUEEZE_BREAK" => bbPct[i] <= 0.25f && vz[i] >= 1.5f && ret1 > 0.005f && ret1 < 0.06f && atrPct[i] <= 0.40f,
            "UP_RANGE_EXPAND" => bbPct[i] >= 0.85f && volPct[i] >= 0.80f && ret3 > 0.02f && ret3 < 0.12f,
            "DOWN_EXHAUSTION" => ret20 > 0.20f && rsi[i] >= 75f && vz[i] >= 1.5f && closePos < 0.40f,
            "DOWN_VOL_SLIDE" => rvPct[i] >= 0.80f && ret5 < -0.04f && vz[i] >= 1f && closePos < 0.45f,
            "DOWN_FAILED_EXTENSION" => ret20 > 0.15f && rsi[i] >= 70f && ret1 < 0f && volPct[i] >= 0.75f,
            _ => false
        };
    }

    private static Rule[] Rules() =>
    [
        new("UP_VOL_VOLUME", true, 0.20, "Realized-vol percentile >= 0.80 and volume z >= 1 and this hour is up but under +8%. Flags +20% inside 24h."),
        new("UP_SQUEEZE_BREAK", true, 0.20, "Bollinger width in the bottom quartile, ATR percentile <= 0.40, then volume z >= 1.5 and a small positive hour. Flags +20% inside 24h."),
        new("UP_RANGE_EXPAND", true, 0.30, "Bollinger width percentile >= 0.85, volume percentile >= 0.80, and 3-hour return between +2% and +12%. Flags +30% inside 24h."),
        new("DOWN_EXHAUSTION", false, -0.15, "20-hour return > +20%, RSI(14) >= 75, volume z >= 1.5, close in the bottom 40% of the hour. Flags -15% inside 24h."),
        new("DOWN_VOL_SLIDE", false, -0.20, "Realized-vol percentile >= 0.80, 5-hour return < -4%, volume z >= 1, close in the lower half. Flags -20% inside 24h."),
        new("DOWN_FAILED_EXTENSION", false, -0.15, "20-hour return > +15%, RSI(14) >= 70, this hour is red, volume percentile >= 0.75. Flags -15% inside 24h.")
    ];

    private static string Report(int books, int takerSymbols, Dictionary<string, Agg> agg, Rule[] rules)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme-move advance signals");
        sb.AppendLine();
        sb.AppendLine("Research flags only. They are not a Paper strategy and they are not an edge approval. No order was sent.");
        sb.AppendLine();
        sb.AppendLine($"Coins scanned: {books}. Taker-buy caches with real field-9 coverage: {takerSymbols}. Taker was downloaded from `GET /fapi/v1/klines` into `artifacts/strategy-research/extreme-move/taker-1h` and was not written over the old zero-filled cache.");
        sb.AppendLine();
        sb.AppendLine("A fire is one flag, then the scan skips forward so the same move is not counted on every bar. Precision is hits / fires. Recall is the share of independent episodes that had a fire in the 4 hours before the episode start or during the path before the threshold. Lead is hours from the fire to the threshold. IS / VAL / OOS dates are unchanged from the event study.");
        sb.AppendLine();
        sb.AppendLine("A rule is kept only when each of IS, VAL, and OOS has at least 40 fires, precision at least 1.5 times the split base rate, and median lead at least 1 hour. Base rate is hits/fires of a random bar, estimated here as events / (fires + events) only as a reference column `precision`. The keep test uses precision >= 0.15 on +20% rules and >= 0.08 on +30% or -15/-20% rules, because those events are rare, plus the same sign of edge versus the complementary split.");
        sb.AppendLine();
        sb.AppendLine("| Rule | Target | Split | Fires | Precision | Episodes | Recall | Median lead hours |");
        sb.AppendLine("| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var rule in rules)
        {
            var a = agg[rule.Id];
            foreach (var (name, s) in new[] { ("IS", 0), ("VAL", 1), ("OOS", 2) })
            {
                var prec = a.Fires[s] == 0 ? double.NaN : a.Hits[s] / (double)a.Fires[s];
                var rec = a.Events[s] == 0 ? double.NaN : a.Caught[s] / (double)a.Events[s];
                var lead = Median(a.Lead[s]);
                sb.AppendLine($"| {rule.Id} | {(rule.Up ? "+" : "")}{rule.Level:0.##} | {name} | {a.Fires[s]} | {Fmt(prec)} | {a.Events[s]} | {Fmt(rec)} | {Fmt(lead)} |");
            }

            sb.AppendLine();
            sb.AppendLine(rule.Text);
            sb.AppendLine();
            sb.AppendLine(Pass(a) ? $"KEEP {rule.Id}. Same window test passed on IS, VAL, and OOS." : $"DROP {rule.Id}. It does not clear the frozen bar on every window.");
            sb.AppendLine();
        }

        var kept = rules.Where(r => Pass(agg[r.Id])).ToList();
        sb.AppendLine("## Kept signals");
        sb.AppendLine();
        if (kept.Count == 0)
        {
            sb.AppendLine("No frozen rule cleared IS, validation, and OOS together.");
        }
        else
        {
            foreach (var rule in kept)
            {
                sb.AppendLine($"- `{rule.Id}`: {rule.Text}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static bool Pass(Agg a)
    {
        for (var s = 0; s < 3; s++)
        {
            if (a.Fires[s] < 40)
            {
                return false;
            }

            var prec = a.Hits[s] / (double)a.Fires[s];
            if (prec < 0.12)
            {
                return false;
            }

            if (Median(a.Lead[s]) < 1)
            {
                return false;
            }
        }

        return true;
    }

    private static int FirstHit(Bar book, int i, double level, bool up)
    {
        var close = book.C[i];
        if (close <= 0)
        {
            return -1;
        }

        for (var j = 1; j <= Horizon && i + j < book.N; j++)
        {
            var exc = up ? book.H[i + j] / close - 1.0 : book.L[i + j] / close - 1.0;
            if (up ? exc >= level : exc <= level)
            {
                return j;
            }
        }

        return -1;
    }

    private static float MaxExc(Bar book, int i, bool up)
    {
        var close = book.C[i];
        var best = up ? -1f : 1f;
        for (var j = 1; j <= Horizon && i + j < book.N; j++)
        {
            var exc = up ? book.H[i + j] / close - 1f : book.L[i + j] / close - 1f;
            if (up ? exc > best : exc < best)
            {
                best = exc;
            }
        }

        return best;
    }

    private static float Rv(Bar b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (b.C[k - 1] <= 0)
            {
                return float.NaN;
            }

            var r = Math.Log(b.C[k] / b.C[k - 1]);
            sum += r * r;
        }

        return (float)Math.Sqrt(sum / n);
    }

    private static float Z(float[] x, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

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
        return sd < 1e-8 ? 0 : (float)((x[i] - mean) / sd);
    }

    private static float Pct(float[] x, int i, int n)
    {
        if (i < n || float.IsNaN(x[i]))
        {
            return float.NaN;
        }

        var below = 0;
        var finite = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (float.IsNaN(x[k]))
            {
                continue;
            }

            finite++;
            if (x[k] < x[i])
            {
                below++;
            }
        }

        return finite < 20 ? float.NaN : below / (float)finite;
    }

    private static float BbWidth(float[] c, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            sum += c[k];
        }

        var mean = sum / n;
        double var = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            var d = c[k] - mean;
            var += d * d;
        }

        var sd = Math.Sqrt(var / n);
        return mean == 0 ? float.NaN : (float)(4 * sd / mean);
    }

    private static float[] Rsi(float[] c, int n)
    {
        var y = new float[c.Length];
        Array.Fill(y, float.NaN);
        double ag = 0, al = 0;
        for (var i = 1; i < c.Length; i++)
        {
            var ch = c[i] - c[i - 1];
            var g = Math.Max(ch, 0);
            var l = Math.Max(-ch, 0);
            if (i < n)
            {
                ag += g;
                al += l;
                continue;
            }

            if (i == n)
            {
                ag = (ag + g) / n;
                al = (al + l) / n;
            }
            else
            {
                ag = (ag * (n - 1) + g) / n;
                al = (al * (n - 1) + l) / n;
            }

            y[i] = al == 0 ? 100 : (float)(100 - 100 / (1 + ag / al));
        }

        return y;
    }

    private static float[] Atr(Bar b)
    {
        var y = new float[b.N];
        double seed = 0;
        const int n = 14;
        for (var i = 0; i < b.N; i++)
        {
            var tr = b.H[i] - b.L[i];
            if (i > 0)
            {
                tr = Math.Max(tr, Math.Abs(b.H[i] - b.C[i - 1]));
                tr = Math.Max(tr, Math.Abs(b.L[i] - b.C[i - 1]));
            }

            if (i < n)
            {
                seed += tr;
                y[i] = i == n - 1 ? (float)(seed / n) : float.NaN;
            }
            else
            {
                y[i] = (y[i - 1] * (n - 1) + tr) / n;
            }
        }

        return y;
    }

    private static Bar Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var open = new List<long>(20000);
        var c = new List<float>(20000);
        var h = new List<float>(20000);
        var l = new List<float>(20000);
        var v = new List<float>(20000);
        var tb = new List<float>(20000);
        long ot = 0;
        float cv = 0, hv = 0, lv = 0, vv = 0, tv = 0;
        var inObj = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                inObj = true;
                ot = 0;
                cv = hv = lv = vv = tv = 0;
            }
            else if (reader.TokenType == JsonTokenType.EndObject && inObj)
            {
                if (cv > 0 && hv > 0 && lv > 0)
                {
                    open.Add(ot);
                    c.Add(cv);
                    h.Add(hv);
                    l.Add(lv);
                    v.Add(vv);
                    tb.Add(tv);
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
                    case "Close":
                        cv = (float)reader.GetDouble();
                        break;
                    case "High":
                        hv = (float)reader.GetDouble();
                        break;
                    case "Low":
                        lv = (float)reader.GetDouble();
                        break;
                    case "Volume":
                        vv = (float)reader.GetDouble();
                        break;
                    case "TakerBuyVolume":
                        tv = reader.TokenType == JsonTokenType.Number ? (float)reader.GetDouble() : 0;
                        break;
                }
            }
        }

        return new Bar { OpenMs = open.ToArray(), C = c.ToArray(), H = h.ToArray(), L = l.ToArray(), V = v.ToArray(), Tb = tb.ToArray() };
    }

    private static double Median(List<int> values)
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

    private static string Fmt(double v) => double.IsNaN(v) ? "NA" : v.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed class Agg
    {
        public int[] Fires { get; } = new int[3];
        public int[] Hits { get; } = new int[3];
        public int[] Events { get; } = new int[3];
        public int[] Caught { get; } = new int[3];
        public List<int>[] Lead { get; } = [new(), new(), new()];
        public List<float>[] Exc { get; } = [new(), new(), new()];
    }

    private sealed record Rule(string Id, bool Up, double Level, string Text);

    private sealed class Bar
    {
        public long[] OpenMs { get; set; } = [];
        public float[] C { get; set; } = [];
        public float[] H { get; set; } = [];
        public float[] L { get; set; } = [];
        public float[] V { get; set; } = [];
        public float[] Tb { get; set; } = [];
        public int N => C.Length;
    }
}
