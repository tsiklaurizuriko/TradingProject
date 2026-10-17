using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Research replay only. No orders. The long is the current Impulse Catch book from
/// <see cref="ImpulseNowReplay"/>: first +16% cross of the 24h low, green upper-half close,
/// 3M quote, +20% over 30 days, skip 7x volume and a fresh 30-day high. The short is that
/// rule with the sign flipped. Both keeps one position per coin, so an open long blocks a short.
/// Exit matches the long replay: 12% stop, ratchet after +20%, 25% close trail, 4-day cap. Cost 0.3%.
/// </summary>
internal static class ImpulseMirrorReplay
{
    private const int Day = 96;
    private const int Hold = 4 * Day;
    private const double Cost = 0.003;
    private const int Start = Day * 32;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string root, string cacheDir)
    {
        Check();
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine("Impulse Catch mirror. Research only. No orders.");
        var files = Directory.GetFiles(cacheDir, "*_15m.json");
        var bag = new ConcurrentBag<CoinResult>();
        var seen = 0;
        Parallel.ForEach(files, file =>
        {
            var symbol = Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase);
            var book = Read(file);
            if (book.N < Day * 40)
            {
                return;
            }

            var rise = Rise(book);
            var drop = Drop(book);
            var quote = Quote(book);
            var volume = VolumeRatio(book);
            var days = Days(book);
            var longs = new List<Trade>();
            var shorts = new List<Trade>();
            WalkSide(book, rise, drop, quote, volume, days, longs, shorts);
            longs = OneAtATime(longs);
            shorts = OneAtATime(shorts);
            var both = WalkBoth(book, longs, shorts, out var blockedLong, out var blockedShort, out var sameBar);
            if (longs.Count == 0 && shorts.Count == 0)
            {
                return;
            }

            bag.Add(new CoinResult(symbol, longs, shorts, both, blockedLong, blockedShort, sameBar));
            var n = Interlocked.Increment(ref seen);
            if (n % 80 == 0)
            {
                Console.WriteLine(string.Create(ci, $"coins {n}"));
            }
        });

        var rows = bag.OrderBy(row => row.Symbol, StringComparer.Ordinal).ToList();
        var text = Report(files.Length, rows);
        var path = Path.Combine(root, "docs", "IMPULSE_MIRROR.md");
        File.WriteAllText(path, text);
        Console.WriteLine(text);
        Console.WriteLine("Wrote " + path);
        return 0;
    }

    private static void Check()
    {
        var n = 40 * Day;
        var book = new Book
        {
            T = new long[n],
            O = new double[n],
            H = new double[n],
            L = new double[n],
            C = new double[n],
            V = new double[n]
        };
        for (var i = 0; i < n; i++)
        {
            book.T[i] = i * 900_000L;
            book.O[i] = book.H[i] = book.L[i] = book.C[i] = 100;
            book.V[i] = 1000;
        }

        for (var i = 8 * Day; i < 9 * Day; i++)
        {
            book.O[i] = book.H[i] = book.L[i] = book.C[i] = 110;
        }

        var wick = 10 * Day + 10;
        book.L[wick] = 40;
        var signal = 39 * Day + 56;
        book.O[signal] = 100;
        book.H[signal] = 100;
        book.L[signal] = 82;
        book.C[signal] = 84;

        var rise = Rise(book);
        var drop = Drop(book);
        var quote = Quote(book);
        var volume = VolumeRatio(book);
        var days = Days(book);
        var longs = new List<Trade>();
        var shorts = new List<Trade>();
        WalkSide(book, rise, drop, quote, volume, days, longs, shorts);
        if (shorts.Count != 1 || longs.Count != 0 || shorts[0].Side != -1)
        {
            throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"Mirror check failed. longs={longs.Count} shorts={shorts.Count}"));
        }
    }

    private static void WalkSide(
        Book book,
        double[] rise,
        double[] drop,
        double[] quote,
        double[] volume,
        List<DayBar> days,
        List<Trade> longs,
        List<Trade> shorts)
    {
        for (var i = Start; i < book.N - 1; i++)
        {
            if (LongSignal(book, rise, quote, volume, days, i))
            {
                longs.Add(ExitLong(book, i));
            }

            if (ShortSignal(book, drop, quote, volume, days, i))
            {
                shorts.Add(ExitShort(book, i));
            }
        }
    }

    private static List<Trade> OneAtATime(List<Trade> raw)
    {
        var kept = new List<Trade>();
        var until = -1;
        foreach (var trade in raw)
        {
            if (trade.Index <= until)
            {
                continue;
            }

            kept.Add(trade);
            until = trade.ExitIndex;
        }

        return kept;
    }

    private static List<Trade> WalkBoth(
        Book book,
        List<Trade> longs,
        List<Trade> shorts,
        out int blockedLong,
        out int blockedShort,
        out int sameBar)
    {
        blockedLong = 0;
        blockedShort = 0;
        sameBar = 0;
        var signals = new List<(int Index, int Side)>(longs.Count + shorts.Count);
        signals.AddRange(longs.Select(trade => (trade.Index, 1)));
        signals.AddRange(shorts.Select(trade => (trade.Index, -1)));
        signals.Sort((a, b) => a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Side.CompareTo(b.Side));

        var taken = new List<Trade>();
        var cursor = 0;
        while (cursor < signals.Count)
        {
            var (index, side) = signals[cursor];
            if (cursor + 1 < signals.Count && signals[cursor + 1].Index == index && signals[cursor + 1].Side != side)
            {
                sameBar++;
                cursor += 2;
                continue;
            }

            var trade = side > 0 ? ExitLong(book, index) : ExitShort(book, index);
            taken.Add(trade);
            cursor++;
            while (cursor < signals.Count && signals[cursor].Index <= trade.ExitIndex)
            {
                if (signals[cursor].Side > 0)
                {
                    blockedLong++;
                }
                else
                {
                    blockedShort++;
                }

                cursor++;
            }
        }

        return taken;
    }

    private static bool LongSignal(Book b, double[] rise, double[] quote, double[] volume, List<DayBar> days, int i)
    {
        if (rise[i] < 0.16 || quote[i] < 3_000_000 || b.C[i] <= b.O[i] || rise[i - 1] >= 0.16)
        {
            return false;
        }

        var span = b.H[i] - b.L[i];
        if (span > 0 && (b.C[i] - b.L[i]) / span < 0.5)
        {
            return false;
        }

        if (volume[i] > 7 || !Trend(b, days, i, out var day, out var ret) || ret < 0.20 || AbovePriorHigh(b, days, day, i))
        {
            return false;
        }

        return true;
    }

    private static bool ShortSignal(Book b, double[] drop, double[] quote, double[] volume, List<DayBar> days, int i)
    {
        if (drop[i] < 0.16 || quote[i] < 3_000_000 || b.C[i] >= b.O[i] || drop[i - 1] >= 0.16)
        {
            return false;
        }

        var span = b.H[i] - b.L[i];
        if (span > 0 && (b.C[i] - b.L[i]) / span >= 0.5)
        {
            return false;
        }

        if (volume[i] > 7 || !Trend(b, days, i, out var day, out var ret) || ret > -0.20 || BelowPriorLow(b, days, day, i))
        {
            return false;
        }

        return true;
    }

    private static bool Trend(Book b, List<DayBar> days, int i, out int day, out double ret)
    {
        day = -1;
        ret = 0;
        var closeMs = b.T[i] + 15 * 60_000 - 1;
        while (day + 1 < days.Count && days[day + 1].CloseMs <= closeMs)
        {
            day++;
        }

        if (day < 30 || closeMs - days[day].CloseMs > 2L * 86_400_000)
        {
            return false;
        }

        var basis = days[day - 30].Close;
        if (basis <= 0)
        {
            return false;
        }

        ret = b.C[i] / basis - 1;
        return true;
    }

    private static Trade ExitLong(Book b, int i)
    {
        var entry = b.C[i];
        var hard = entry * 0.88;
        var peak = entry;
        for (var k = i + 1; k < b.N; k++)
        {
            var stop = hard;
            if (peak / entry - 1 >= 0.20)
            {
                stop = Math.Max(stop, Math.Max(entry * 1.002, peak * 0.70));
            }

            if (b.L[k] <= stop)
            {
                return new Trade(i, k, 1, b.T[i], b.T[k], Math.Min(b.O[k], stop) / entry - 1 - Cost, 0);
            }

            peak = Math.Max(peak, b.H[k]);
            if (b.C[k] <= peak * 0.75 || k - i >= Hold)
            {
                return new Trade(i, k, 1, b.T[i], b.T[k], b.C[k] / entry - 1 - Cost, 0);
            }
        }

        var last = b.N - 1;
        return new Trade(i, last, 1, b.T[i], b.T[last], b.C[last] / entry - 1 - Cost, -1);
    }

    private static Trade ExitShort(Book b, int i)
    {
        var entry = b.C[i];
        var hard = entry * 1.12;
        var trough = entry;
        for (var k = i + 1; k < b.N; k++)
        {
            var stop = hard;
            if (trough > 0 && entry / trough - 1 >= 0.20)
            {
                stop = Math.Min(stop, Math.Min(entry * 0.998, trough * 1.30));
            }

            if (b.H[k] >= stop)
            {
                var fill = Math.Max(b.O[k], stop);
                return new Trade(i, k, -1, b.T[i], b.T[k], (entry - fill) / entry - Cost, 0);
            }

            trough = Math.Min(trough, b.L[k]);
            if (trough > 0 && (b.C[k] >= trough * 1.25 || k - i >= Hold))
            {
                return new Trade(i, k, -1, b.T[i], b.T[k], (entry - b.C[k]) / entry - Cost, 0);
            }
        }

        var last = b.N - 1;
        return new Trade(i, last, -1, b.T[i], b.T[last], (entry - b.C[last]) / entry - Cost, -1);
    }

    private static bool AbovePriorHigh(Book b, List<DayBar> days, int day, int i)
    {
        var high = 0.0;
        for (var k = day - 29; k <= day; k++)
        {
            high = Math.Max(high, days[k].High);
        }

        for (var k = i - 1; k >= 0 && b.T[k] > days[day].CloseMs; k--)
        {
            high = Math.Max(high, b.H[k]);
        }

        return b.C[i] > high;
    }

    private static bool BelowPriorLow(Book b, List<DayBar> days, int day, int i)
    {
        var low = double.PositiveInfinity;
        for (var k = day - 29; k <= day; k++)
        {
            low = Math.Min(low, days[k].Low);
        }

        for (var k = i - 1; k >= 0 && b.T[k] > days[day].CloseMs; k--)
        {
            low = Math.Min(low, b.L[k]);
        }

        return b.C[i] < low;
    }

    private static double[] Rise(Book b)
    {
        var rise = new double[b.N];
        var dq = new int[b.N];
        var head = 0;
        var tail = 0;
        for (var i = 0; i < b.N; i++)
        {
            while (head < tail && dq[head] <= i - Day)
            {
                head++;
            }

            while (head < tail && b.L[dq[tail - 1]] >= b.L[i])
            {
                tail--;
            }

            dq[tail++] = i;
            var low = b.L[dq[head]];
            rise[i] = low > 0 ? b.C[i] / low - 1 : 0;
        }

        return rise;
    }

    private static double[] Drop(Book b)
    {
        var drop = new double[b.N];
        var dq = new int[b.N];
        var head = 0;
        var tail = 0;
        for (var i = 0; i < b.N; i++)
        {
            while (head < tail && dq[head] <= i - Day)
            {
                head++;
            }

            while (head < tail && b.H[dq[tail - 1]] <= b.H[i])
            {
                tail--;
            }

            dq[tail++] = i;
            var high = b.H[dq[head]];
            drop[i] = high > 0 ? 1 - b.C[i] / high : 0;
        }

        return drop;
    }

    private static double[] Quote(Book b)
    {
        var quote = new double[b.N];
        var sum = 0.0;
        for (var i = 0; i < b.N; i++)
        {
            sum += b.V[i] * b.C[i];
            if (i >= Day)
            {
                sum -= b.V[i - Day] * b.C[i - Day];
            }

            quote[i] = sum;
        }

        return quote;
    }

    private static double[] VolumeRatio(Book b)
    {
        const int week = 672;
        var prefix = new double[b.N + 1];
        for (var i = 0; i < b.N; i++)
        {
            prefix[i + 1] = prefix[i] + b.V[i];
        }

        var ratio = new double[b.N];
        for (var i = week + 4; i < b.N; i++)
        {
            var hour = prefix[i + 1] - prefix[i - 3];
            var baseline = prefix[i - 3] - prefix[i - 3 - week];
            var mean = baseline / week * 4;
            ratio[i] = mean > 0 ? hour / mean : 0;
        }

        return ratio;
    }

    private static List<DayBar> Days(Book b)
    {
        var days = new List<DayBar>();
        const long dayMs = 86_400_000;
        var i = 0;
        while (i < b.N)
        {
            var dayOpen = b.T[i] / dayMs * dayMs;
            var close = b.C[i];
            var high = b.H[i];
            var low = b.L[i];
            var j = i;
            while (j < b.N && b.T[j] / dayMs * dayMs == dayOpen)
            {
                close = b.C[j];
                high = Math.Max(high, b.H[j]);
                low = Math.Min(low, b.L[j]);
                j++;
            }

            days.Add(new DayBar(dayOpen + dayMs - 1, close, high, low));
            i = j;
        }

        return days;
    }

    private static string Report(int files, List<CoinResult> rows)
    {
        var longBook = new BookStats();
        var shortBook = new BookStats();
        var bothBook = new BookStats();
        var blockedLong = 0;
        var blockedShort = 0;
        var sameBar = 0;
        foreach (var row in rows)
        {
            longBook.Add(row.Symbol, OneAtATime(row.Longs));
            shortBook.Add(row.Symbol, OneAtATime(row.Shorts));
            bothBook.Add(row.Symbol, row.Both);
            blockedLong += row.BlockedLong;
            blockedShort += row.BlockedShort;
            sameBar += row.SameBar;
        }

        var sb = new StringBuilder();
        sb.AppendLine("# Impulse Catch mirror");
        sb.AppendLine();
        sb.AppendLine("Research replay only. No Paper bot, no live order. The long is the current Impulse Catch rule. The short is the same rule with the sign flipped. Thresholds were not searched.");
        sb.AppendLine();
        sb.AppendLine("Long: first closed 15m bar at least 16% above the 24h low, green, close in the upper half, 24h quote volume at least 3M USDT, coin already up 20% over 30 days. Hourly volume above 7x the prior 7-day average, and a close above the prior 30-day high, are skips.");
        sb.AppendLine();
        sb.AppendLine("Short: first closed 15m bar at least 16% below the 24h high, red, close in the lower half, 24h quote volume at least 3M USDT, coin already down 20% over 30 days. Hourly volume above 7x, and a close below the prior 30-day low, are skips.");
        sb.AppendLine();
        sb.AppendLine("Exit, both sides: 12% exchange stop, after +20% in favor the stop ratchets to the tighter of breakeven and 30% beyond the extreme, a close 25% back through that extreme exits, and the hold ends after 4 days. One position per coin in the combined book: an open long blocks a later short on that coin, and the reverse. A bar that is both a long and a short is skipped. Cost is 0.3% round trip. Funding is not in the 15m file and is not invented.");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Files {files}. Coins with a trade {rows.Count}."));
        sb.AppendLine();
        sb.AppendLine("## Long only");
        sb.AppendLine();
        longBook.Append(sb);
        sb.AppendLine();
        sb.AppendLine("## Short only");
        sb.AppendLine();
        shortBook.Append(sb);
        sb.AppendLine();
        sb.AppendLine("## Both, one position per coin");
        sb.AppendLine();
        sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Same-bar long and short skipped: {sameBar}. Shorts blocked by an open position: {blockedShort}. Longs blocked by an open position: {blockedLong}."));
        sb.AppendLine();
        bothBook.Append(sb);
        bothBook.AppendSides(sb);
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false. This replay does not change Impulse Catch.");
        return sb.ToString();
    }

    private sealed class BookStats
    {
        private readonly Split _all = new();
        private readonly Split _is = new();
        private readonly Split _val = new();
        private readonly Split _oos = new();
        private readonly Split _long = new();
        private readonly Split _short = new();
        private readonly Dictionary<string, double> _coins = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Trade> _trades = [];

        public void Add(string symbol, List<Trade> trades)
        {
            foreach (var trade in trades)
            {
                if (trade.Age >= 0)
                {
                    _all.Add(trade.Net);
                    SplitOf(trade.At).Add(trade.Net);
                    if (trade.Side > 0)
                    {
                        _long.Add(trade.Net);
                    }
                    else
                    {
                        _short.Add(trade.Net);
                    }

                    _coins[symbol] = _coins.GetValueOrDefault(symbol) + trade.Net;
                }

                _trades.Add(trade);
            }
        }

        public void Append(StringBuilder sb)
        {
            var ci = CultureInfo.InvariantCulture;
            sb.AppendLine("  " + _all.Line("ALL"));
            sb.AppendLine("  " + _is.Line("IS "));
            sb.AppendLine("  " + _val.Line("VAL"));
            sb.AppendLine("  " + _oos.Line("OOS"));
            var closed = _trades.Where(trade => trade.Age >= 0).ToList();
            var eight = Portfolio(closed, 8);
            var wide = Portfolio(closed, 30);
            sb.AppendLine(string.Create(ci, $"  slots8 x{eight.Multiple:0.00} maxDD {eight.MaxDd:P0} losing months {eight.Losing}/{eight.Months} taken {eight.Taken} skipped {eight.Skipped}"));
            sb.AppendLine(string.Create(ci, $"  slots30 x{wide.Multiple:0.00} maxDD {wide.MaxDd:P0} losing months {wide.Losing}/{wide.Months} taken {wide.Taken} skipped {wide.Skipped}"));
            var ordered = _coins.OrderByDescending(pair => pair.Value).Take(5);
            foreach (var pair in ordered)
            {
                sb.AppendLine(string.Create(ci, $"    {pair.Key} {pair.Value:0.00}"));
            }
        }

        public void AppendSides(StringBuilder sb)
        {
            sb.AppendLine("  " + _long.Line("LONG "));
            sb.AppendLine("  " + _short.Line("SHORT"));
        }

        private Split SplitOf(long at)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(at);
            return t < IsEnd ? _is : t < ValEnd ? _val : _oos;
        }

        private static (double Multiple, double MaxDd, int Losing, int Months, int Taken, int Skipped) Portfolio(List<Trade> trades, int slots)
        {
            var equity = 1.0;
            var peak = 1.0;
            var maxDd = 0.0;
            var open = new List<(long Until, double Stake, double Net)>();
            var monthly = new Dictionary<string, double>(StringComparer.Ordinal);
            var taken = 0;
            var skipped = 0;
            foreach (var trade in trades.OrderBy(t => t.At).ThenBy(t => t.Until))
            {
                foreach (var position in open.Where(p => p.Until <= trade.At).OrderBy(p => p.Until).ToList())
                {
                    var pnl = position.Stake * position.Net;
                    equity += pnl;
                    var key = DateTimeOffset.FromUnixTimeMilliseconds(position.Until).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                    monthly[key] = monthly.GetValueOrDefault(key) + pnl;
                    peak = Math.Max(peak, equity);
                    maxDd = Math.Max(maxDd, peak > 0 ? 1 - equity / peak : 0);
                    open.Remove(position);
                }

                if (open.Count >= slots || equity <= 0)
                {
                    skipped++;
                    continue;
                }

                open.Add((trade.Until, equity / slots, trade.Net));
                taken++;
            }

            foreach (var position in open)
            {
                var pnl = position.Stake * position.Net;
                equity += pnl;
                var key = DateTimeOffset.FromUnixTimeMilliseconds(position.Until).ToString("yyyy-MM", CultureInfo.InvariantCulture);
                monthly[key] = monthly.GetValueOrDefault(key) + pnl;
                peak = Math.Max(peak, equity);
                maxDd = Math.Max(maxDd, peak > 0 ? 1 - equity / peak : 0);
            }

            return (equity, maxDd, monthly.Count(m => m.Value < 0), monthly.Count, taken, skipped);
        }
    }

    private sealed class Split
    {
        private int _n;
        private int _win;
        private int _big;
        private double _sum;
        private double _gain;
        private double _loss;

        public void Add(double net)
        {
            _n++;
            _sum += net;
            if (net > 0)
            {
                _win++;
                _gain += net;
            }
            else
            {
                _loss -= net;
            }

            if (net >= 0.20)
            {
                _big++;
            }
        }

        public string Line(string name)
        {
            if (_n == 0)
            {
                return name + " none";
            }

            var pf = _loss > 0 ? _gain / _loss : 99;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{name} n={_n} mean={_sum / _n:P2} pf={pf:0.00} win={_win / (double)_n:P0} ge20={_big / (double)_n:P0}");
        }
    }

    private readonly record struct Trade(int Index, int ExitIndex, int Side, long At, long Until, double Net, int Age);
    private readonly record struct DayBar(long CloseMs, double Close, double High, double Low);
    private readonly record struct CoinResult(
        string Symbol,
        List<Trade> Longs,
        List<Trade> Shorts,
        List<Trade> Both,
        int BlockedLong,
        int BlockedShort,
        int SameBar);

    private sealed class Book
    {
        public long[] T = [];
        public double[] O = [], H = [], L = [], C = [], V = [];
        public int N => C.Length;
    }

    private static Book Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
        var t = new List<long>(40000);
        var o = new List<double>(40000);
        var h = new List<double>(40000);
        var l = new List<double>(40000);
        var c = new List<double>(40000);
        var v = new List<double>(40000);
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

        return new Book { T = t.ToArray(), O = o.ToArray(), H = h.ToArray(), L = l.ToArray(), C = c.ToArray(), V = v.ToArray() };
    }
}
