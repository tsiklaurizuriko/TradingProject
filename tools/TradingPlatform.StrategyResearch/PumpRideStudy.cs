using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Large up-move continuation on closed 15m bars. Research only, no orders.
/// Entry is the close of the trigger bar. The hard stop is intrabar (an exchange stop); the trail is checked on closed bars,
/// which is what the bot can do. Parameters are chosen on IS only; VAL and OOS are reported once.
/// </summary>
internal static class PumpRideStudy
{
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);
    private const int Day = 96;
    private const int Week = 672;
    private const int Warmup = Week + 8;
    private const double CostRoundTrip = 0.003;

    private static readonly double[] RiseLevels = [0.08, 0.12, 0.16, 0.20];
    private static readonly double[] VolumeRatios = [3, 5, 8];
    private static readonly int[] BreakoutBars = [0, 288, 672];
    private static readonly double[] Extension = [0.10, 9];
    private static readonly double[] Stops = [0.06, 0.10, 0.15];
    private static readonly double[] Trails = [0.10, 0.15, 0.20, 0.30];
    private const int MaxHoldBars = 4 * Day;
    private const double MinQuoteVolume24h = 3_000_000;

    public static int Run(string root, string cacheDir)
    {
        Console.WriteLine("Pump ride study. Research only. No orders.");
        var files = Directory.GetFiles(cacheDir, "*_15m.json");
        var combos = new List<Combo>();
        foreach (var rise in RiseLevels)
        foreach (var vol in VolumeRatios)
        foreach (var brk in BreakoutBars)
        foreach (var ext in Extension)
        foreach (var stop in Stops)
        foreach (var trail in Trails)
        {
            combos.Add(new Combo(rise, vol, brk, ext, stop, trail));
        }

        var results = combos.ToDictionary(c => c, _ => new Split[] { new(), new(), new() });
        var events = new EventStats();
        var coins = 0;
        foreach (var file in files)
        {
            var symbol = Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase);
            var book = Read(file);
            if (book.N < Warmup + Day * 7)
            {
                continue;
            }

            coins++;
            var f = Features.Build(book);
            Describe(book, f, events);
            foreach (var combo in combos)
            {
                Simulate(symbol, book, f, combo, results[combo]);
            }

            if (coins % 50 == 0)
            {
                Console.WriteLine($"coins {coins}");
            }
        }

        var report = Report(coins, combos, results, events);
        var path = Path.Combine(root, "docs", "PUMP_RIDE_STUDY.md");
        File.WriteAllText(path, report);
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    /// <summary>
    /// One baseline trigger and exit; every trade is tagged with context features so each bucket can be compared on IS, VAL and OOS.
    /// </summary>
    public static int RunFeatures(string root, string cacheDir)
    {
        Console.WriteLine("Pump ride feature buckets. Research only. No orders.");
        var btc = Read(Path.Combine(cacheDir, "BTCUSDT_15m.json"));
        var btcAt = new Dictionary<long, int>(btc.N);
        for (var i = 0; i < btc.N; i++)
        {
            btcAt[btc.OpenMs[i]] = i;
        }

        var baseline = new Combo(0.16, 3, 0, 0.10, 0.15, 0.30);
        var rows = new List<Tagged>();
        foreach (var file in Directory.GetFiles(cacheDir, "*_15m.json"))
        {
            var symbol = Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase);
            var book = Read(file);
            if (book.N < Month + Day * 7)
            {
                continue;
            }

            var f = Features.Build(book);
            var low30 = Features.Sliding(book.L, Month, (a, b) => a <= b);
            var high30 = Features.Sliding(book.H, Month, (a, b) => a >= b);
            var i = Month + 8;
            while (i < book.N - 1)
            {
                if (!Trigger(book, f, baseline, i))
                {
                    i++;
                    continue;
                }

                var (net, k, peak) = Exit(book, i, baseline);
                if (k >= book.N)
                {
                    break;
                }

                var tag = new Dictionary<string, double>();
                var priorRise = 0.0;
                for (var j = i - Week; j < i - Day; j++)
                {
                    priorRise = Math.Max(priorRise, f.Rise[j]);
                }

                tag["prior7dMaxRise"] = priorRise;
                tag["base30Range"] = high30[i - Day] / low30[i - Day] - 1;
                tag["above30dHigh"] = book.C[i] > high30[i - 1] ? 1 : 0;
                tag["ret30d"] = book.C[i] / book.C[i - Month] - 1;
                tag["volRatio"] = f.VolRatio[i];
                tag["quote24M"] = f.Quote24[i] / 1e6;
                var lowAt = i;
                for (var j = i; j > i - Day; j--)
                {
                    if (book.L[j] < book.L[lowAt])
                    {
                        lowAt = j;
                    }
                }

                tag["hoursFromLow"] = (i - lowAt) / 4.0;
                tag["hourUtc"] = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i]).Hour;
                if (btcAt.TryGetValue(book.OpenMs[i], out var bi) && bi >= Week)
                {
                    tag["btc24h"] = btc.C[bi] / btc.C[bi - Day] - 1;
                    var mean = 0.0;
                    for (var j = bi - Week + 1; j <= bi; j++)
                    {
                        mean += btc.C[j];
                    }

                    tag["btcVs7dMean"] = btc.C[bi] / (mean / Week) - 1;
                }

                rows.Add(new Tagged(symbol, book.OpenMs[i], SplitOf(book.OpenMs[i]), net, peak, tag));
                i = k + 4;
            }
        }

        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("# Pump ride feature buckets");
        sb.AppendLine();
        sb.AppendLine($"Baseline: rise ≥16% from the 24h low (first cross, ≤26%), hourly volume ≥3x the 7-day mean, green bar closing in its upper half, 24h quote volume ≥$3M. Exchange stop 15%, trail 30% below the peak high on closed bars, 4-day cap. Cost {CostRoundTrip:P1}.");
        sb.AppendLine("Buckets are terciles fixed on IS. Mean net per trade per split; n in brackets.");
        sb.AppendLine();
        foreach (var name in rows.SelectMany(r => r.Tags.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal))
        {
            var isValues = rows.Where(r => r.Split == 0 && r.Tags.ContainsKey(name)).Select(r => r.Tags[name]).OrderBy(v => v).ToArray();
            if (isValues.Length < 30)
            {
                continue;
            }

            var cuts = name is "above30dHigh" ? [0.5] : new[] { isValues[isValues.Length / 3], isValues[isValues.Length * 2 / 3] };
            sb.AppendLine($"## {name}");
            sb.AppendLine();
            sb.AppendLine("| Bucket | IS | VAL | OOS | OOS ≥+20% |");
            sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
            for (var bucket = 0; bucket <= cuts.Length; bucket++)
            {
                var lo = bucket == 0 ? double.NegativeInfinity : cuts[bucket - 1];
                var hi = bucket == cuts.Length ? double.PositiveInfinity : cuts[bucket];
                var label = string.Create(ci, $"{(double.IsNegativeInfinity(lo) ? "−∞" : lo.ToString("0.###", ci))} .. {(double.IsPositiveInfinity(hi) ? "∞" : hi.ToString("0.###", ci))}");
                var cells = Enumerable.Range(0, 3).Select(split =>
                {
                    var g = rows.Where(r => r.Split == split && r.Tags.TryGetValue(name, out var v) && v >= lo && v < hi).ToList();
                    return g.Count == 0 ? "—" : string.Create(ci, $"{g.Average(r => r.Net):P2} ({g.Count})");
                }).ToList();
                var oos = rows.Where(r => r.Split == 2 && r.Tags.TryGetValue(name, out var v) && v >= lo && v < hi).ToList();
                var big = oos.Count == 0 ? "—" : (oos.Count(r => r.Net >= 0.2) / (double)oos.Count).ToString("P0", ci);
                sb.AppendLine($"| {label} | {cells[0]} | {cells[1]} | {cells[2]} | {big} |");
            }

            sb.AppendLine();
        }

        var path = Path.Combine(root, "docs", "PUMP_RIDE_FEATURES.md");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    private sealed record Variant(string Name, double MinRet30, double MaxVol, bool BlockFresh30dHigh);

    /// <summary>Filter variants chosen from the feature buckets, each with a few exits, plus an 8-slot portfolio replay.</summary>
    public static int RunVariants(string root, string cacheDir)
    {
        Console.WriteLine("Pump ride variants. Research only. No orders.");
        Variant[] variants =
        [
            new("baseline", double.NegativeInfinity, double.PositiveInfinity, false),
            new("trend30", 0.20, double.PositiveInfinity, false),
            new("noClimax", double.NegativeInfinity, 7, false),
            new("trend30+noClimax", 0.20, 7, false),
            new("trend30+noClimax+notFresh", 0.20, 7, true),
            new("trend10+noClimax", 0.10, 7, false)
        ];
        (double Stop, double Trail)[] exits = [(0.15, 0.30), (0.15, 0.20), (0.12, 0.25), (0.10, 0.15), (0.20, 0.30)];
        var books = new List<(string Symbol, Book Book, Features F, double[] High30)>();
        foreach (var file in Directory.GetFiles(cacheDir, "*_15m.json"))
        {
            var book = Read(file);
            if (book.N < Month + Day * 7)
            {
                continue;
            }

            books.Add((Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase), book, Features.Build(book), Features.Sliding(book.H, Month, (a, b) => a >= b)));
        }

        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Pump ride variants");
        sb.AppendLine();
        sb.AppendLine($"Coins {books.Count}. Trigger: rise ≥16% from the 24h low (first cross, ≤26%), hourly volume ≥3x the 7-day mean, green bar closing in its upper half, 24h quote volume ≥$3M. Cost {CostRoundTrip:P1}. 4-day cap.");
        sb.AppendLine("trend30 = coin up ≥20% over 30 days. noClimax = hourly volume ≤7x. notFresh = close not above the prior 30-day high. These filters were picked after looking at all three splits, so VAL and OOS are not clean out-of-sample here.");
        sb.AppendLine("Portfolio: at most 8 open at once, each 1/8 of equity at entry, later signals skipped while full. Return is compounded over the whole period.");
        sb.AppendLine();
        sb.AppendLine("| Variant | Stop | Trail | IS n / mean / PF | VAL n / mean / PF | OOS n / mean / PF | ≥+20% | Portfolio x | Max DD | Losing months |");
        sb.AppendLine("| --- | ---: | ---: | --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var v in variants)
        foreach (var (stop, trail) in exits)
        {
            var combo = new Combo(0.16, 3, 0, 0.10, stop, trail);
            var trades = new List<(long At, long Until, int Split, double Net, string Symbol)>();
            foreach (var (symbol, book, f, high30) in books)
            {
                var i = Month + 8;
                while (i < book.N - 1)
                {
                    if (!Trigger(book, f, combo, i)
                        || book.C[i] / book.C[i - Month] - 1 < v.MinRet30
                        || f.VolRatio[i] > v.MaxVol
                        || (v.BlockFresh30dHigh && book.C[i] > high30[i - 1]))
                    {
                        i++;
                        continue;
                    }

                    var (net, k, _) = Exit(book, i, combo);
                    if (k >= book.N)
                    {
                        break;
                    }

                    trades.Add((book.OpenMs[i], book.OpenMs[k], SplitOf(book.OpenMs[i]), net, symbol));
                    i = k + 4;
                }
            }

            string Cell(int split)
            {
                var g = trades.Where(t => t.Split == split).Select(t => t.Net).ToList();
                if (g.Count == 0)
                {
                    return "—";
                }

                var gain = g.Where(x => x > 0).Sum();
                var loss = -g.Where(x => x < 0).Sum();
                return string.Create(ci, $"{g.Count} / {g.Average():P2} / {(loss > 0 ? gain / loss : 99):0.00}");
            }

            var (multiple, drawdown, losingMonths, months) = Portfolio(trades.Select(t => (t.At, t.Until, t.Net)).ToList());
            var big = trades.Count == 0 ? 0 : trades.Count(t => t.Net >= 0.2) / (double)trades.Count;
            sb.AppendLine(string.Create(ci, $"| {v.Name} | {stop:P0} | {trail:P0} | {Cell(0)} | {Cell(1)} | {Cell(2)} | {big:P0} | {multiple:0.00} | {drawdown:P0} | {losingMonths}/{months} |"));
        }

        var path = Path.Combine(root, "docs", "PUMP_RIDE_VARIANTS.md");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    /// <summary>
    /// Historical measurement of the older book (26% rise cap, volume 3–7×, no fresh 30-day high).
    /// The live entry no longer uses those filters. Reported under several costs.
    /// </summary>
    public static int RunLive(string root, string cacheDir)
    {
        Console.WriteLine("Pump ride live definition. Research only. No orders.");
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("# Pump ride, live definition");
        sb.AppendLine();
        sb.AppendLine("Live entry is the first closed 15m bar at least 16% above the 24h low, including a bar that jumps past that level. The bar is green and closes in its upper half, 24h turnover is at least 3M USDT, and the coin is already up 20% over 30 days. A volume climax and a new 30-day high are entries. Exchange stop 12%, trail 25% below the peak high on closed bars, 4-day cap.");
        sb.AppendLine();
        sb.AppendLine("The tables below measured the older book (trend30 + noClimax + notFresh, rise capped at 26%). They do not describe the live entry.");
        sb.AppendLine();
        var combo = new Combo(0.16, 3, 0, 0.10, 0.12, 0.25);
        var books = new List<(string Symbol, Book Book, Features F, List<DayBar> Days)>();
        foreach (var file in Directory.GetFiles(cacheDir, "*_15m.json"))
        {
            var book = Read(file);
            if (book.N >= Month + Day * 7)
            {
                books.Add((Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase), book, Features.Build(book), DailyBars(book)));
            }
        }

        sb.AppendLine("## Exchange stop ratchet");
        sb.AppendLine();
        sb.AppendLine("After the peak is `arm` above entry, the exchange stop moves to max(breakeven, peak high × (1 − ratchet)). It fills intrabar. The close-based 25% trail stays. 0.3% cost.");
        sb.AppendLine();
        sb.AppendLine("| Arm | Ratchet | IS mean / PF | VAL mean / PF | OOS mean / PF | Win |");
        sb.AppendLine("| ---: | ---: | --- | --- | --- | ---: |");
        foreach (var (arm, ratchet) in new[] { (9.0, 0.0), (0.10, 0.08), (0.10, 0.15), (0.20, 0.20), (0.30, 0.25), (0.50, 0.25), (0.20, 0.30) })
        {
            var rows = new List<(int Split, double Net)>();
            foreach (var (_, book, f, days) in books)
            {
                var i = Month + Day * 2;
                while (i < book.N - 1)
                {
                    if (!Trigger(book, f, combo, i) || !LiveFilters(book, f, days, i))
                    {
                        i++;
                        continue;
                    }

                    var (net, k) = RatchetExit(book, i, arm, ratchet);
                    if (k >= book.N)
                    {
                        break;
                    }

                    rows.Add((SplitOf(book.OpenMs[i]), net));
                    i = k + 4;
                }
            }

            string Cell(int split)
            {
                var g = rows.Where(r => r.Split == split).Select(r => r.Net).ToList();
                var gain = g.Where(x => x > 0).Sum();
                var loss = -g.Where(x => x < 0).Sum();
                return g.Count == 0 ? "—" : string.Create(ci, $"{g.Average():P2} / {(loss > 0 ? gain / loss : 99):0.00}");
            }

            sb.AppendLine(string.Create(ci, $"| {(arm > 1 ? "off" : arm.ToString("P0", ci))} | {ratchet:P0} | {Cell(0)} | {Cell(1)} | {Cell(2)} | {rows.Count(r => r.Net > 0) / (double)Math.Max(1, rows.Count):P0} |"));
        }

        sb.AppendLine();
        var trades = new List<(long At, long Until, int Split, double Gross, string Symbol)>();
        foreach (var file in Directory.GetFiles(cacheDir, "*_15m.json"))
        {
            var symbol = Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase);
            var book = Read(file);
            if (book.N < Month + Day * 7)
            {
                continue;
            }

            var f = Features.Build(book);
            var days = DailyBars(book);
            var i = Month + Day * 2;
            while (i < book.N - 1)
            {
                if (!Trigger(book, f, combo, i) || !LiveFilters(book, f, days, i))
                {
                    i++;
                    continue;
                }

                var (net, k, _) = Exit(book, i, combo);
                if (k >= book.N)
                {
                    break;
                }

                trades.Add((book.OpenMs[i], book.OpenMs[k], SplitOf(book.OpenMs[i]), net + CostRoundTrip, symbol));
                i = k + 4;
            }
        }

        sb.AppendLine("| Cost round trip | IS n / mean / PF | VAL n / mean / PF | OOS n / mean / PF | Win | ≥+20% | Portfolio x | Max DD | Losing months |");
        sb.AppendLine("| ---: | --- | --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var cost in new[] { 0.003, 0.006, 0.010 })
        {
            string Cell(int split)
            {
                var g = trades.Where(t => t.Split == split).Select(t => t.Gross - cost).ToList();
                if (g.Count == 0)
                {
                    return "—";
                }

                var gain = g.Where(x => x > 0).Sum();
                var loss = -g.Where(x => x < 0).Sum();
                return string.Create(ci, $"{g.Count} / {g.Average():P2} / {(loss > 0 ? gain / loss : 99):0.00}");
            }

            var (multiple, drawdown, losing, months) = Portfolio(trades.Select(t => (t.At, t.Until, t.Gross - cost)).ToList());
            var win = trades.Count(t => t.Gross - cost > 0) / (double)Math.Max(1, trades.Count);
            var big = trades.Count(t => t.Gross - cost >= 0.2) / (double)Math.Max(1, trades.Count);
            sb.AppendLine(string.Create(ci, $"| {cost:P1} | {Cell(0)} | {Cell(1)} | {Cell(2)} | {win:P0} | {big:P0} | {multiple:0.00} | {drawdown:P0} | {losing}/{months} |"));
        }

        sb.AppendLine();
        sb.AppendLine("Trades per month, mean net at 0.3%:");
        sb.AppendLine();
        foreach (var g in trades.GroupBy(t => DateTimeOffset.FromUnixTimeMilliseconds(t.At).ToString("yyyy-MM", ci)).OrderBy(g => g.Key))
        {
            sb.AppendLine(string.Create(ci, $"- {g.Key}: n {g.Count()}, mean {g.Average(t => t.Gross - 0.003):P2}, best {g.Max(t => t.Gross - 0.003):P0}"));
        }

        sb.AppendLine();
        sb.AppendLine("Largest trades:");
        sb.AppendLine();
        foreach (var t in trades.OrderByDescending(t => t.Gross).Take(12))
        {
            sb.AppendLine(string.Create(ci, $"- {t.Symbol} {DateTimeOffset.FromUnixTimeMilliseconds(t.At):yyyy-MM-dd HH:mm} net {t.Gross - 0.003:P0}"));
        }

        var path = Path.Combine(root, "docs", "PUMP_RIDE_LIVE.md");
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    private static (double Net, int Exit) RatchetExit(Book b, int i, double arm, double ratchet)
    {
        var entry = b.C[i];
        var hard = entry * 0.88;
        var peak = entry;
        for (var k = i + 1; k < b.N; k++)
        {
            var stop = hard;
            if (peak / entry - 1 >= arm)
            {
                stop = Math.Max(stop, Math.Max(entry * 1.002, peak * (1 - ratchet)));
            }

            if (b.L[k] <= stop)
            {
                return (Math.Min(b.O[k], stop) / entry - 1 - CostRoundTrip, k);
            }

            peak = Math.Max(peak, b.H[k]);
            if (b.C[k] <= peak * 0.75 || k - i >= MaxHoldBars)
            {
                return (b.C[k] / entry - 1 - CostRoundTrip, k);
            }
        }

        return (0, b.N);
    }

    private sealed record DayBar(long OpenMs, long CloseMs, double Close, double High);

    private static List<DayBar> DailyBars(Book b)
    {
        var days = new List<DayBar>();
        const long dayMs = 86_400_000;
        var i = 0;
        while (i < b.N)
        {
            var dayOpen = b.OpenMs[i] / dayMs * dayMs;
            var high = b.H[i];
            var close = b.C[i];
            var j = i;
            while (j < b.N && b.OpenMs[j] / dayMs * dayMs == dayOpen)
            {
                high = Math.Max(high, b.H[j]);
                close = b.C[j];
                j++;
            }

            days.Add(new DayBar(dayOpen, dayOpen + dayMs - 1, close, high));
            i = j;
        }

        return days;
    }

    private static bool LiveFilters(Book b, Features f, List<DayBar> days, int i)
    {
        if (f.VolRatio[i] > 7)
        {
            return false;
        }

        var closeMs = b.OpenMs[i] + 15 * 60_000 - 1;
        var d = days.FindLastIndex(day => day.CloseMs <= closeMs);
        if (d < 30)
        {
            return false;
        }

        if (b.C[i] / days[d - 30].Close - 1 < 0.20)
        {
            return false;
        }

        var high = 0.0;
        for (var k = d - 29; k <= d; k++)
        {
            high = Math.Max(high, days[k].High);
        }

        for (var k = i - 1; k >= 0 && b.OpenMs[k] > days[d].CloseMs; k--)
        {
            high = Math.Max(high, b.H[k]);
        }

        return b.C[i] <= high;
    }

    private static (double Multiple, double MaxDrawdown, int LosingMonths, int Months) Portfolio(List<(long At, long Until, double Net)> trades)
    {
        const int slots = 8;
        var equity = 1.0;
        var peak = 1.0;
        var maxDd = 0.0;
        var open = new List<(long Until, double Stake, double Net)>();
        var monthly = new SortedDictionary<string, double>(StringComparer.Ordinal);
        void Close(long upTo)
        {
            foreach (var p in open.Where(p => p.Until <= upTo).OrderBy(p => p.Until).ToList())
            {
                var pnl = p.Stake * p.Net;
                equity += pnl;
                var key = DateTimeOffset.FromUnixTimeMilliseconds(p.Until).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                monthly[key] = monthly.GetValueOrDefault(key) + pnl;
                peak = Math.Max(peak, equity);
                maxDd = Math.Max(maxDd, 1 - equity / peak);
                open.Remove(p);
            }
        }

        foreach (var t in trades.OrderBy(t => t.At))
        {
            Close(t.At);
            if (open.Count >= slots)
            {
                continue;
            }

            open.Add((t.Until, equity / slots, t.Net));
        }

        Close(long.MaxValue);
        return (equity, maxDd, monthly.Count(m => m.Value < 0), monthly.Count);
    }

    private const int Month = Day * 30;

    private sealed record Tagged(string Symbol, long At, int Split, double Net, double Peak, Dictionary<string, double> Tags);

    private static (double Net, int Exit, double Peak) Exit(Book b, int i, Combo c)
    {
        var entry = b.C[i];
        var hard = entry * (1 - c.Stop);
        var peak = entry;
        for (var k = i + 1; k < b.N; k++)
        {
            if (b.L[k] <= hard)
            {
                return (Math.Min(b.O[k], hard) / entry - 1 - CostRoundTrip, k, peak / entry - 1);
            }

            peak = Math.Max(peak, b.H[k]);
            if (b.C[k] <= peak * (1 - c.Trail) || k - i >= MaxHoldBars)
            {
                return (b.C[k] / entry - 1 - CostRoundTrip, k, peak / entry - 1);
            }
        }

        return (0, b.N, 0);
    }

    private static void Simulate(string symbol, Book b, Features f, Combo c, Split[] splits)
    {
        var i = Warmup;
        while (i < b.N - 1)
        {
            if (!Trigger(b, f, c, i))
            {
                i++;
                continue;
            }

            var entry = b.C[i];
            var hard = entry * (1 - c.Stop);
            var peak = entry;
            var exit = 0.0;
            var k = i + 1;
            for (; k < b.N; k++)
            {
                if (b.L[k] <= hard)
                {
                    exit = Math.Min(b.O[k], hard);
                    break;
                }

                peak = Math.Max(peak, b.H[k]);
                if (b.C[k] <= peak * (1 - c.Trail) || k - i >= MaxHoldBars)
                {
                    exit = b.C[k];
                    break;
                }
            }

            if (k >= b.N)
            {
                break;
            }

            var net = exit / entry - 1 - CostRoundTrip;
            var split = SplitOf(b.OpenMs[i]);
            splits[split].Add(net, k - i, symbol, b.OpenMs[i], peak / entry - 1);
            i = k + 4;
        }
    }

    private static bool Trigger(Book b, Features f, Combo c, int i) =>
        f.Rise[i] >= c.Rise
        && f.Rise[i - 1] < c.Rise
        && f.Rise[i] <= c.Rise + c.Extension
        && f.VolRatio[i] >= c.Volume
        && f.Quote24[i] >= MinQuoteVolume24h
        && b.C[i] > b.O[i]
        && (b.H[i] - b.L[i] <= 0 || (b.C[i] - b.L[i]) / (b.H[i] - b.L[i]) >= 0.5)
        && (c.Breakout == 0 || b.C[i] > (c.Breakout == 288 ? f.High3d[i] : f.High7d[i]));

    /// <summary>What happened after the first +12% (from the 24h low) bar with 5x hourly volume, before any exit rule.</summary>
    private static void Describe(Book b, Features f, EventStats stats)
    {
        var i = Warmup;
        while (i < b.N - Day * 2)
        {
            if (!(f.Rise[i] >= 0.12 && f.Rise[i - 1] < 0.12 && f.VolRatio[i] >= 5 && f.Quote24[i] >= MinQuoteVolume24h))
            {
                i++;
                continue;
            }

            var entry = b.C[i];
            double mfe = 0, maeBeforePeak = 0, low = entry;
            var peakAt = i;
            for (var k = i + 1; k <= i + Day * 2; k++)
            {
                low = Math.Min(low, b.L[k]);
                if (b.H[k] / entry - 1 > mfe)
                {
                    mfe = b.H[k] / entry - 1;
                    peakAt = k;
                    maeBeforePeak = Math.Min(maeBeforePeak, low / entry - 1);
                }
            }

            stats.Add(mfe, maeBeforePeak, peakAt - i, b.C[i + Day * 2] / entry - 1, f.VolRatio[i], f.Rise[i]);
            i += Day;
        }
    }

    private static int SplitOf(long ms)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return t < IsEnd ? 0 : t < ValEnd ? 1 : 2;
    }

    private static string Report(int coins, List<Combo> combos, Dictionary<Combo, Split[]> results, EventStats events)
    {
        var sb = new StringBuilder();
        var ci = CultureInfo.InvariantCulture;
        sb.AppendLine("# Pump ride study");
        sb.AppendLine();
        sb.AppendLine("Research only. No order was sent. 15m closed bars from `artifacts/strategy-validation-cache`.");
        sb.AppendLine($"Coins: {coins}. IS before {IsEnd:yyyy-MM-dd}, VAL before {ValEnd:yyyy-MM-dd}, OOS after. Cost {CostRoundTrip:P1} round trip per trade.");
        sb.AppendLine("Survivorship: the cache holds coins listed today. Pumped coins that were delisted are missing, which flatters a long-only result.");
        sb.AppendLine();
        sb.AppendLine("## What follows a first +12% bar with 5x hourly volume (48h window, no exit rule)");
        sb.AppendLine();
        sb.AppendLine(events.Render());
        sb.AppendLine();
        sb.AppendLine("## Grid, ranked on IS only (at least 150 IS trades)");
        sb.AppendLine();
        sb.AppendLine("Rise = close vs 24h low. Vol = last hour vs 7-day hourly mean. Brk = close above prior N-bar high (0 = off). Ext = max rise beyond the level. Stop = exchange stop. Trail = close below peak high.");
        sb.AppendLine();
        sb.AppendLine("| Rise | Vol | Brk | Ext | Stop | Trail | IS n | IS mean | IS PF | VAL n | VAL mean | VAL PF | OOS n | OOS mean | OOS PF | OOS win | OOS ≥+20% | OOS top10 share |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        var ranked = combos
            .Where(c => results[c][0].N >= 150)
            .OrderByDescending(c => results[c][0].Mean)
            .Take(40)
            .ToList();
        foreach (var c in ranked)
        {
            var s = results[c];
            sb.AppendLine(string.Create(ci,
                $"| {c.Rise:P0} | {c.Volume} | {c.Breakout} | {(c.Extension > 1 ? "off" : c.Extension.ToString("P0", ci))} | {c.Stop:P0} | {c.Trail:P0} | {s[0].N} | {s[0].Mean:P2} | {s[0].Pf:0.00} | {s[1].N} | {s[1].Mean:P2} | {s[1].Pf:0.00} | {s[2].N} | {s[2].Mean:P2} | {s[2].Pf:0.00} | {s[2].Win:P0} | {s[2].Big:P0} | {s[2].Top10:P0} |"));
        }

        sb.AppendLine();
        sb.AppendLine("## Marginal effect of each parameter (mean of IS means across the rest of the grid)");
        sb.AppendLine();
        Marginal(sb, "Rise", combos, results, c => c.Rise.ToString("P0", ci));
        Marginal(sb, "Vol", combos, results, c => c.Volume.ToString(ci));
        Marginal(sb, "Brk", combos, results, c => c.Breakout.ToString(ci));
        Marginal(sb, "Ext", combos, results, c => c.Extension > 1 ? "off" : c.Extension.ToString("P0", ci));
        Marginal(sb, "Stop", combos, results, c => c.Stop.ToString("P0", ci));
        Marginal(sb, "Trail", combos, results, c => c.Trail.ToString("P0", ci));

        if (ranked.Count > 0)
        {
            var best = results[ranked[0]];
            sb.AppendLine("## Best IS row: largest OOS trades");
            sb.AppendLine();
            foreach (var t in best[2].Trades.OrderByDescending(t => t.Net).Take(15))
            {
                sb.AppendLine(string.Create(ci, $"- {t.Symbol} {DateTimeOffset.FromUnixTimeMilliseconds(t.At):yyyy-MM-dd HH:mm} net {t.Net:P1}, peak {t.Peak:P1}, held {t.Bars * 15 / 60.0:0.#}h"));
            }

            sb.AppendLine();
            sb.AppendLine("## Best IS row: OOS by month");
            sb.AppendLine();
            foreach (var g in best[2].Trades.GroupBy(t => DateTimeOffset.FromUnixTimeMilliseconds(t.At).ToString("yyyy-MM", ci)).OrderBy(g => g.Key))
            {
                sb.AppendLine(string.Create(ci, $"- {g.Key}: n {g.Count()}, sum {g.Sum(t => t.Net):P0}, mean {g.Average(t => t.Net):P2}"));
            }
        }

        return sb.ToString();
    }

    private static void Marginal(StringBuilder sb, string name, List<Combo> combos, Dictionary<Combo, Split[]> results, Func<Combo, string> key)
    {
        sb.Append($"- {name}: ");
        sb.AppendLine(string.Join(", ", combos
            .Where(c => results[c][0].N >= 50)
            .GroupBy(key)
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{g.Key} IS {g.Average(c => results[c][0].Mean):P2} / VAL {g.Average(c => results[c][1].Mean):P2} / OOS {g.Average(c => results[c][2].Mean):P2}"))));
    }

    private sealed record Combo(double Rise, double Volume, int Breakout, double Extension, double Stop, double Trail);

    private sealed record Trade(string Symbol, long At, double Net, int Bars, double Peak);

    private sealed class Split
    {
        public List<Trade> Trades { get; } = [];
        public int N => Trades.Count;
        public double Mean => N == 0 ? 0 : Trades.Average(t => t.Net);
        public double Win => N == 0 ? 0 : Trades.Count(t => t.Net > 0) / (double)N;
        public double Big => N == 0 ? 0 : Trades.Count(t => t.Net >= 0.20) / (double)N;

        public double Pf
        {
            get
            {
                var gain = Trades.Where(t => t.Net > 0).Sum(t => t.Net);
                var loss = -Trades.Where(t => t.Net < 0).Sum(t => t.Net);
                return loss <= 0 ? 99 : gain / loss;
            }
        }

        public double Top10
        {
            get
            {
                var total = Trades.Sum(t => t.Net);
                return total <= 0 ? 0 : Trades.OrderByDescending(t => t.Net).Take(10).Sum(t => t.Net) / total;
            }
        }

        public void Add(double net, int bars, string symbol, long at, double peak) => Trades.Add(new Trade(symbol, at, net, bars, peak));
    }

    private sealed class EventStats
    {
        private readonly List<(double Mfe, double Mae, int PeakBars, double Ret48, double Vol, double Rise)> _rows = [];

        public void Add(double mfe, double mae, int peakBars, double ret48, double vol, double rise) => _rows.Add((mfe, mae, peakBars, ret48, vol, rise));

        public string Render()
        {
            if (_rows.Count == 0)
            {
                return "No events.";
            }

            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine(string.Create(ci, $"Events: {_rows.Count}."));
            foreach (var level in new[] { 0.10, 0.20, 0.50, 1.00 })
            {
                sb.AppendLine(string.Create(ci, $"- went another +{level:P0} within 48h: {_rows.Count(r => r.Mfe >= level) / (double)_rows.Count:P1}"));
            }

            sb.AppendLine(string.Create(ci, $"- median further gain {Median(_rows.Select(r => r.Mfe)):P1}, median drawdown before that peak {Median(_rows.Select(r => r.Mae)):P1}, median hours to peak {Median(_rows.Select(r => r.PeakBars / 4.0)):0.#}"));
            sb.AppendLine(string.Create(ci, $"- close 48h later vs entry: median {Median(_rows.Select(r => r.Ret48)):P1}, mean {_rows.Average(r => r.Ret48):P1}"));
            foreach (var (lo, hi) in new[] { (5.0, 10.0), (10.0, 20.0), (20.0, 1e9) })
            {
                var g = _rows.Where(r => r.Vol >= lo && r.Vol < hi).ToList();
                if (g.Count > 0)
                {
                    sb.AppendLine(string.Create(ci, $"- volume {lo}x–{(hi > 1e8 ? "∞" : hi.ToString(ci))}x: n {g.Count}, another +20% {g.Count(r => r.Mfe >= 0.2) / (double)g.Count:P1}, median 48h {Median(g.Select(r => r.Ret48)):P1}"));
                }
            }

            return sb.ToString();
        }

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
        }
    }

    private sealed class Features
    {
        public double[] Rise = [];
        public double[] VolRatio = [];
        public double[] Quote24 = [];
        public double[] High3d = [];
        public double[] High7d = [];

        public static Features Build(Book b)
        {
            var n = b.N;
            var f = new Features
            {
                Rise = new double[n],
                VolRatio = new double[n],
                Quote24 = new double[n],
                High3d = new double[n],
                High7d = new double[n]
            };
            var vPrefix = new double[n + 1];
            var qPrefix = new double[n + 1];
            for (var i = 0; i < n; i++)
            {
                vPrefix[i + 1] = vPrefix[i] + b.V[i];
                qPrefix[i + 1] = qPrefix[i] + b.V[i] * b.C[i];
            }

            var low24 = SlidingMin(b.L, Day);
            var high3 = SlidingMax(b.H, 288);
            var high7 = SlidingMax(b.H, Week);
            for (var i = Warmup; i < n; i++)
            {
                f.Rise[i] = low24[i] > 0 ? b.C[i] / low24[i] - 1 : 0;
                var hour = vPrefix[i + 1] - vPrefix[i - 3];
                var baseMean = (vPrefix[i - 3] - vPrefix[i - 3 - Week]) / Week * 4;
                f.VolRatio[i] = baseMean > 0 ? hour / baseMean : 0;
                f.Quote24[i] = qPrefix[i + 1] - qPrefix[i + 1 - Day];
                f.High3d[i] = high3[i - 1];
                f.High7d[i] = high7[i - 1];
            }

            return f;
        }

        private static double[] SlidingMin(double[] x, int w) => Sliding(x, w, (a, b) => a <= b);

        private static double[] SlidingMax(double[] x, int w) => Sliding(x, w, (a, b) => a >= b);

        public static double[] Sliding(double[] x, int w, Func<double, double, bool> keep)
        {
            var result = new double[x.Length];
            var dq = new LinkedList<int>();
            for (var i = 0; i < x.Length; i++)
            {
                while (dq.Count > 0 && dq.First!.Value <= i - w)
                {
                    dq.RemoveFirst();
                }

                while (dq.Count > 0 && keep(x[i], x[dq.Last!.Value]))
                {
                    dq.RemoveLast();
                }

                dq.AddLast(i);
                result[i] = x[dq.First!.Value];
            }

            return result;
        }
    }

    private sealed class Book
    {
        public long[] OpenMs = [];
        public double[] O = [], H = [], L = [], C = [], V = [];
        public int N => C.Length;
    }

    private static Book Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var t = new List<long>(80000);
        var o = new List<double>(80000);
        var h = new List<double>(80000);
        var l = new List<double>(80000);
        var c = new List<double>(80000);
        var v = new List<double>(80000);
        long ot = 0;
        double ov = 0, hv = 0, lv = 0, cv = 0, vv = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                ot = 0;
                ov = hv = lv = cv = vv = 0;
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                if (cv > 0 && hv > 0 && lv > 0 && ov > 0 && (t.Count == 0 || ot > t[^1]))
                {
                    t.Add(ot);
                    o.Add(ov);
                    h.Add(hv);
                    l.Add(lv);
                    c.Add(cv);
                    v.Add(vv);
                }
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                switch (name)
                {
                    case "OpenTime":
                        ot = DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
                        break;
                    case "Open":
                        ov = reader.GetDouble();
                        break;
                    case "High":
                        hv = reader.GetDouble();
                        break;
                    case "Low":
                        lv = reader.GetDouble();
                        break;
                    case "Close":
                        cv = reader.GetDouble();
                        break;
                    case "Volume":
                        vv = reader.GetDouble();
                        break;
                }
            }
        }

        return new Book { OpenMs = t.ToArray(), O = o.ToArray(), H = h.ToArray(), L = l.ToArray(), C = c.ToArray(), V = v.ToArray() };
    }
}
