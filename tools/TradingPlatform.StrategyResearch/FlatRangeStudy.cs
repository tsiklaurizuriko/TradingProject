using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Research-only mean reversion inside a causal flat. No orders.
/// Range is the prior 24 closed hours, excluding the signal bar.
/// Rules are frozen and are not adjusted on OOS.
/// </summary>
internal static class FlatRangeStudy
{
    private const int Lookback = 24;
    private const int MaxHold = 24;
    private const double MinWidth = 0.008;
    private const double MaxWidth = 0.06;
    private const double EntryFrac = 0.20;
    private const double ExitFrac = 0.80;
    private const double FeePerSide = 0.0004;
    private const double SlipPerSide = 0.0002;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root, string cacheDir)
    {
        Console.WriteLine("Flat inside-range study. Research only. No orders.");
        var buckets = new[] { new Bucket("IS"), new Bucket("VAL"), new Bucket("OOS") };
        var files = Directory.GetFiles(cacheDir, "*_1h.json");
        var books = 0;
        foreach (var file in files)
        {
            var book = Read(file);
            if (book.N < 400)
            {
                continue;
            }

            books++;
            Simulate(book, buckets);
            if (books % 100 == 0)
            {
                Console.WriteLine($"scanned {books}");
            }
        }

        var path = Path.Combine(root, "docs", "FLAT_RANGE_TRADING.md");
        File.WriteAllText(path, Report(books, buckets));
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    public static async Task<int> ScanNow(string root, string cacheDir)
    {
        var symbols = Directory.GetFiles(cacheDir, "*_1h.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Select(name => name.EndsWith("_1h", StringComparison.Ordinal) ? name[..^3] : name)
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToArray();
        Console.WriteLine($"Flat scan. Closed 1h bars. Coins {symbols.Length}. Research flags only.");
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(20) };
        var gate = new SemaphoreSlim(6);
        var rows = new List<NowRow>();
        var failed = 0;
        var done = 0;
        await Task.WhenAll(symbols.Select(async symbol =>
        {
            await gate.WaitAsync();
            try
            {
                var book = await Fetch(http, symbol);
                if (book is null || book.N < Lookback + 120)
                {
                    Interlocked.Increment(ref failed);
                    return;
                }

                var i = book.N - 1;
                if (!Flat(book, i, out var lo, out var hi))
                {
                    return;
                }

                var close = book.C[i];
                var width = hi - lo;
                var pos = (close - lo) / width;
                var side = pos <= EntryFrac ? "LONG" : pos >= 1 - EntryFrac ? "SHORT" : "INSIDE";
                lock (rows)
                {
                    rows.Add(new NowRow(symbol, side, close, lo, hi, (hi - lo) / book.C[i - 1], pos, book.OpenMs[i]));
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                Interlocked.Increment(ref failed);
            }
            finally
            {
                var n = Interlocked.Increment(ref done);
                if (n % 100 == 0)
                {
                    Console.WriteLine($"scanned {n}");
                }

                gate.Release();
            }
        }));

        rows.Sort(static (a, b) =>
        {
            var rank = Rank(a.Side).CompareTo(Rank(b.Side));
            if (rank != 0)
            {
                return rank;
            }

            var edge = Edge(a).CompareTo(Edge(b));
            return edge != 0 ? edge : string.CompareOrdinal(a.Coin, b.Coin);
        });
        var clock = rows.Count == 0 ? "none" : DateTimeOffset.FromUnixTimeMilliseconds(rows.Max(row => row.OpenMs)).AddHours(1).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
        var path = Path.Combine(root, "artifacts", "strategy-research", "flat-now.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, NowReport(symbols.Length, failed, clock, rows));
        Console.WriteLine($"Wrote {path}. Flat {rows.Count}. Edge {rows.Count(row => row.Side != "INSIDE")}. Failed {failed}. Clock {clock}.");
        return failed == symbols.Length ? 1 : 0;
    }

    public static int RunRisk(string root, string cacheDir)
    {
        Console.WriteLine("Flat bound risk. Stop is the entry bound. Take profit is the opposite bound.");
        var buckets = new[] { new RiskBucket("IS"), new RiskBucket("VAL"), new RiskBucket("OOS") };
        var books = 0;
        foreach (var file in Directory.GetFiles(cacheDir, "*_1h.json"))
        {
            var book = Read(file);
            if (book.N < 400)
            {
                continue;
            }

            books++;
            SimulateRisk(book, buckets);
            if (books % 100 == 0)
            {
                Console.WriteLine($"scanned {books}");
            }
        }

        var path = Path.Combine(root, "docs", "FLAT_RANGE_TRADING.md");
        var prior = File.Exists(path) ? File.ReadAllText(path).TrimEnd() : "# Flat inside-range trading";
        var marker = "\n\n## Bound stop and take profit";
        var cut = prior.IndexOf(marker, StringComparison.Ordinal);
        if (cut >= 0)
        {
            prior = prior[..cut].TrimEnd();
        }

        File.WriteAllText(path, prior + marker + "\n\n" + RiskReport(books, buckets));
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    private static void SimulateRisk(Bar book, RiskBucket[] buckets)
    {
        var i = Lookback + 20;
        while (i < book.N - 2)
        {
            if (!Flat(book, i, out var lo, out var hi))
            {
                i++;
                continue;
            }

            var close = book.C[i];
            var width = hi - lo;
            var pos = (close - lo) / width;
            var side = pos <= EntryFrac ? 1 : pos >= 1 - EntryFrac ? -1 : 0;
            if (side == 0)
            {
                i++;
                continue;
            }

            var stop = side > 0 ? lo : hi;
            var take = side > 0 ? hi : lo;
            var stopPct = Math.Abs(close - stop) / close;
            var takePct = Math.Abs(take - close) / close;
            if (stopPct < 0.002 || takePct <= stopPct)
            {
                i++;
                continue;
            }

            var exit = close;
            var reason = "time";
            var hold = 1;
            var last = Math.Min(book.N - 1, i + MaxHold);
            for (var j = i + 1; j <= last; j++)
            {
                hold = j - i;
                var hitStop = side > 0 ? book.L[j] <= stop : book.H[j] >= stop;
                var hitTake = side > 0 ? book.H[j] >= take : book.L[j] <= take;
                if (hitStop)
                {
                    exit = stop;
                    reason = "stop";
                    break;
                }

                if (hitTake)
                {
                    exit = take;
                    reason = "take";
                    break;
                }

                exit = book.C[j];
            }

            var gross = side * (exit / close - 1.0);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i]);
            var split = when < IsEnd ? 0 : when < ValEnd ? 1 : 2;
            var bucket = buckets[split];
            bucket.Trades++;
            if (reason == "stop")
            {
                bucket.Stops++;
            }
            else if (reason == "take")
            {
                bucket.Takes++;
            }

            bucket.StopPct.Add(stopPct);
            bucket.TakePct.Add(takePct);
            bucket.Hold.Add(hold);
            var net = gross - 2 * (FeePerSide + SlipPerSide);
            bucket.Net.Add(net);
            if (net > 0)
            {
                bucket.Wins++;
            }

            i = i + hold + 1;
        }
    }

    private static string RiskReport(int books, RiskBucket[] buckets)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Coins: {books}. Same flat rule. Stop is the touched bound. Take profit is the opposite bound. A stop closer than 0.20% is skipped. Time exit is 24 hours. Round trip cost is 0.12%.");
        sb.AppendLine();
        sb.AppendLine("| Split | Trades | Stops | Takes | Time exits | Median stop | Median take profit | Median R | Win rate | Mean net |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var bucket in buckets)
        {
            var n = Math.Max(1, bucket.Trades);
            var stop = MedianD(bucket.StopPct);
            var take = MedianD(bucket.TakePct);
            var reward = double.IsNaN(stop) || stop <= 0 ? double.NaN : take / stop;
            sb.AppendLine($"| {bucket.Name} | {bucket.Trades} | {bucket.Stops} | {bucket.Takes} | {bucket.Trades - bucket.Stops - bucket.Takes} | {Fmt(stop)} | {Fmt(take)} | {Fmt(reward)} | {Fmt(bucket.Wins / (double)n)} | {Fmt(Mean(bucket.Net))} |");
        }

        sb.AppendLine();
        sb.AppendLine("Position size uses the account risk percent divided by this stop distance. The stop price and the take-profit price are the locked bounds, not a fixed percent book.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static double MedianD(List<double> values)
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

    private static async Task<Bar?> Fetch(HttpClient http, string symbol)
    {
        using var response = await http.GetAsync($"fapi/v1/klines?symbol={Uri.EscapeDataString(symbol)}&interval=1h&limit=220");
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var open = new List<long>(220);
        var c = new List<float>(220);
        var h = new List<float>(220);
        var l = new List<float>(220);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var bar in doc.RootElement.EnumerateArray())
        {
            var closeMs = bar[6].GetInt64();
            if (closeMs > now)
            {
                continue;
            }

            var cv = float.Parse(bar[4].GetString()!, CultureInfo.InvariantCulture);
            var hv = float.Parse(bar[2].GetString()!, CultureInfo.InvariantCulture);
            var lv = float.Parse(bar[3].GetString()!, CultureInfo.InvariantCulture);
            if (cv <= 0 || hv <= 0 || lv <= 0)
            {
                continue;
            }

            open.Add(bar[0].GetInt64());
            c.Add(cv);
            h.Add(hv);
            l.Add(lv);
        }

        return new Bar { OpenMs = open.ToArray(), C = c.ToArray(), H = h.ToArray(), L = l.ToArray() };
    }

    private static string NowReport(int coins, int failed, string clock, List<NowRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Flat coins now");
        sb.AppendLine();
        sb.AppendLine("Research flags on the last closed 1h bar. No Paper bot, no live order.");
        sb.AppendLine();
        sb.AppendLine($"Coins asked: {coins}. Failed fetches: {failed}. Flat now: {rows.Count}. Edge signals: {rows.Count(row => row.Side != "INSIDE")}. Clock: {clock}.");
        sb.AppendLine();
        sb.AppendLine("The band is the prior 24 closed hours, not including this bar. Width is 0.8% to 6% of price and sits in the lowest 30% of the prior 100 bands. LONG is a close in the bottom 20% of the band. SHORT is a close in the top 20%. INSIDE is flat with no edge touch.");
        sb.AppendLine();
        sb.AppendLine("The frozen replay does not survive 1.5x cost. These rows are a scan, not an order list.");
        sb.AppendLine();
        sb.AppendLine("| Coin | Signal | Close | Band low | Band high | Width | Position |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {row.Coin} | {row.Side} | {Px(row.Close)} | {Px(row.Lo)} | {Px(row.Hi)} | {row.Width.ToString("0.00%", CultureInfo.InvariantCulture)} | {row.Pos.ToString("0.00", CultureInfo.InvariantCulture)} |");
        }

        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static int Rank(string side) => side switch { "LONG" => 0, "SHORT" => 1, _ => 2 };

    private static double Edge(NowRow row) => row.Side == "SHORT" ? 1 - row.Pos : row.Pos;

    private static string Px(float v) => v >= 1000 ? v.ToString("0.##", CultureInfo.InvariantCulture) : v >= 1 ? v.ToString("0.####", CultureInfo.InvariantCulture) : v.ToString("0.######", CultureInfo.InvariantCulture);

    private static void Simulate(Bar book, Bucket[] buckets)
    {
        var i = Lookback + 20;
        while (i < book.N - 2)
        {
            if (!Flat(book, i, out var lo, out var hi))
            {
                i++;
                continue;
            }

            var close = book.C[i];
            var width = hi - lo;
            var pos = (close - lo) / width;
            var side = 0;
            if (pos <= EntryFrac)
            {
                side = 1;
            }
            else if (pos >= 1 - EntryFrac)
            {
                side = -1;
            }

            if (side == 0)
            {
                i++;
                continue;
            }

            var entry = close;
            var exit = entry;
            var reason = "time";
            var hold = 1;
            var last = Math.Min(book.N - 1, i + MaxHold);
            for (var j = i + 1; j <= last; j++)
            {
                hold = j - i;
                exit = book.C[j];
                if (side > 0 && book.L[j] < lo)
                {
                    exit = (float)lo;
                    reason = "break";
                    break;
                }

                if (side < 0 && book.H[j] > hi)
                {
                    exit = (float)hi;
                    reason = "break";
                    break;
                }

                var mark = (book.C[j] - lo) / width;
                if (side > 0 && mark >= ExitFrac)
                {
                    reason = "target";
                    break;
                }

                if (side < 0 && mark <= 1 - ExitFrac)
                {
                    reason = "target";
                    break;
                }
            }

            var gross = side * (exit / entry - 1.0);
            var when = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i]);
            var split = when < IsEnd ? 0 : when < ValEnd ? 1 : 2;
            var bucket = buckets[split];
            bucket.Trades++;
            if (side > 0)
            {
                bucket.Longs++;
            }

            if (reason == "target")
            {
                bucket.Targets++;
            }
            else if (reason == "break")
            {
                bucket.Breaks++;
            }

            bucket.Hold.Add(hold);
            foreach (var stress in new[] { 1.0, 1.5, 2.0 })
            {
                var cost = 2 * (FeePerSide + SlipPerSide) * stress;
                var net = gross - cost;
                var slot = stress switch { 1.0 => 0, 1.5 => 1, _ => 2 };
                bucket.Net[slot].Add(net);
                if (net > 0)
                {
                    bucket.Wins[slot]++;
                }
            }

            i = i + hold + 1;
        }
    }

    private static bool Flat(Bar book, int i, out float lo, out float hi)
    {
        lo = float.MaxValue;
        hi = float.MinValue;
        for (var k = i - Lookback; k < i; k++)
        {
            lo = Math.Min(lo, book.L[k]);
            hi = Math.Max(hi, book.H[k]);
        }

        var mid = book.C[i - 1];
        if (mid <= 0 || hi <= lo)
        {
            return false;
        }

        var width = (hi - lo) / mid;
        if (width < MinWidth || width > MaxWidth)
        {
            return false;
        }

        var below = 0;
        var finite = 0;
        for (var k = i - 100; k < i; k++)
        {
            if (k < Lookback)
            {
                continue;
            }

            var klo = float.MaxValue;
            var khi = float.MinValue;
            for (var t = k - Lookback; t < k; t++)
            {
                klo = Math.Min(klo, book.L[t]);
                khi = Math.Max(khi, book.H[t]);
            }

            if (book.C[k - 1] <= 0 || khi <= klo)
            {
                continue;
            }

            finite++;
            if ((khi - klo) / book.C[k - 1] < width)
            {
                below++;
            }
        }

        return finite >= 40 && below / (float)finite <= 0.30f;
    }

    private static string Report(int books, Bucket[] buckets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Flat inside-range trading");
        sb.AppendLine();
        sb.AppendLine("Research replay only. No Paper bot, no live order.");
        sb.AppendLine();
        sb.AppendLine($"Coins: {books}. Clock: closed 1h bars.");
        sb.AppendLine();
        sb.AppendLine("A flat is the prior 24 hours, not including the signal bar. The band width must be 0.8% to 6% of price, and that width must sit in the lowest 30% of the prior 100 such bands. Long when the close is in the bottom 20% of that band. Short when it is in the top 20%. Exit at the opposite 20% boundary, on a close through the band, or after 24 hours. One position at a time per coin. The next entry waits until the trade is done.");
        sb.AppendLine();
        sb.AppendLine("Cost is taker fee 0.04% plus slippage 0.02% per side. Round trip at 1x is 0.12%. Stress uses 1.5x and 2x that cost. Funding is not in the 1h file and is not invented.");
        sb.AppendLine();
        sb.AppendLine("| Split | Trades | Long share | Target exits | Band breaks | Median hold hours | Win rate 1x | Mean net 1x | Mean net 1.5x | Mean net 2x |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var bucket in buckets)
        {
            var n = Math.Max(1, bucket.Trades);
            sb.AppendLine($"| {bucket.Name} | {bucket.Trades} | {Fmt(bucket.Longs / (double)n)} | {bucket.Targets} | {bucket.Breaks} | {Fmt(Median(bucket.Hold))} | {Fmt(bucket.Wins[0] / (double)n)} | {Fmt(Mean(bucket.Net[0]))} | {Fmt(Mean(bucket.Net[1]))} | {Fmt(Mean(bucket.Net[2]))} |");
        }

        sb.AppendLine();
        var oos = buckets[2];
        var mean = Mean(oos.Net[0]);
        sb.AppendLine(oos.Trades >= 50 && mean > 0
            ? "OOS mean net at 1x cost is positive on this frozen rule."
            : "OOS mean net at 1x cost is not positive, or the sample is under 50 trades. This rule does not justify orders.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false.");
        return sb.ToString();
    }

    private static Bar Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var c = new List<float>(20000);
        var h = new List<float>(20000);
        var l = new List<float>(20000);
        var open = new List<long>(20000);
        long ot = 0;
        float cv = 0, hv = 0, lv = 0;
        var inObj = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                inObj = true;
                ot = 0;
                cv = hv = lv = 0;
            }
            else if (reader.TokenType == JsonTokenType.EndObject && inObj)
            {
                if (cv > 0 && hv > 0 && lv > 0)
                {
                    open.Add(ot);
                    c.Add(cv);
                    h.Add(hv);
                    l.Add(lv);
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
                }
            }
        }

        return new Bar { OpenMs = open.ToArray(), C = c.ToArray(), H = h.ToArray(), L = l.ToArray() };
    }

    private static double Mean(List<double> values) => values.Count == 0 ? double.NaN : values.Average();

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

    private sealed class Bucket(string name)
    {
        public string Name { get; } = name;
        public int Trades { get; set; }
        public int Longs { get; set; }
        public int Targets { get; set; }
        public int Breaks { get; set; }
        public int[] Wins { get; } = new int[3];
        public List<double>[] Net { get; } = [new(), new(), new()];
        public List<int> Hold { get; } = [];
    }

    private sealed class Bar
    {
        public long[] OpenMs { get; set; } = [];
        public float[] C { get; set; } = [];
        public float[] H { get; set; } = [];
        public float[] L { get; set; } = [];
        public int N => C.Length;
    }

    private sealed record NowRow(string Coin, string Side, float Close, float Lo, float Hi, float Width, float Pos, long OpenMs);

    private sealed class RiskBucket(string name)
    {
        public string Name { get; } = name;
        public int Trades { get; set; }
        public int Stops { get; set; }
        public int Takes { get; set; }
        public int Wins { get; set; }
        public List<double> StopPct { get; } = [];
        public List<double> TakePct { get; } = [];
        public List<double> Net { get; } = [];
        public List<int> Hold { get; } = [];
    }
}
