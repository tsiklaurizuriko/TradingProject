using System.Globalization;
using System.Text.Json;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Current Impulse Catch book on the validation cache. Prints to the console. Does not write a report.
/// Entry: +16% above the 24h low, green close in the upper half, 3M quote, +20% over 30 days.
/// The crossing bar and the next seven closed bars. No volume cap, no 26% cap, no fresh-high skip, no range-shock cap.
/// Exit: 12% exchange stop, ratchet to max(breakeven, 30% under the peak) after +20%, 25% close trail, 4-day cap. Cost 0.3%.
/// </summary>
internal static class ImpulseNowReplay
{
    private const int Day = 96;
    private const int Hold = 4 * Day;
    private const int Window = 8;
    private const double Cost = 0.003;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);

    public static int Run(string cacheDir)
    {
        var ci = CultureInfo.InvariantCulture;
        var files = Directory.GetFiles(cacheDir, "*_15m.json");
        var wide = new BookStats();
        var first = new BookStats();
        var cappedBook = new BookStats();
        var coins = 0;
        foreach (var file in files)
        {
            var symbol = Path.GetFileName(file).Replace("_15m.json", "", StringComparison.OrdinalIgnoreCase);
            var book = Read(file);
            if (book.N < Day * 40)
            {
                continue;
            }

            var rise = Rise(book);
            var quote = Quote(book);
            var volume = VolumeRatio(book);
            var days = Days(book);
            var plain = Walk(book, rise, quote, volume, days, riseMax: 9, skipClimax: false, skipFresh: false);
            var filtered = Walk(book, rise, quote, volume, days, riseMax: 9, skipClimax: true, skipFresh: true);
            var capped = Walk(book, rise, quote, volume, days, riseMax: 0.26, skipClimax: true, skipFresh: true);
            if (plain.Count == 0 && filtered.Count == 0 && capped.Count == 0)
            {
                continue;
            }

            coins++;
            wide.Add(symbol, plain);
            first.Add(symbol, filtered);
            cappedBook.Add(symbol, capped);
            if (coins % 80 == 0)
            {
                Console.WriteLine(string.Create(ci, $"coins {coins}"));
            }
        }

        Console.WriteLine(string.Create(ci, $"files {files.Length} coins {coins}"));
        Console.WriteLine("first bar, no extra filters");
        wide.Print();
        Console.WriteLine("first bar, skip climax and fresh 30d high");
        first.Print();
        first.PrintSince(new DateTimeOffset(2026, 8, 8, 0, 0, 0, TimeSpan.FromHours(4)));
        Console.WriteLine("same, rise also capped at 26%");
        cappedBook.Print();
        return 0;
    }

    private static List<Trade> Walk(
        Book b,
        double[] rise,
        double[] quote,
        double[] volume,
        List<DayBar> days,
        double riseMax,
        bool skipClimax,
        bool skipFresh)
    {
        var trades = new List<Trade>();
        var day = -1;
        for (var i = Day * 32; i < b.N - 1;)
        {
            if (rise[i] < 0.16 || quote[i] < 3_000_000 || b.C[i] <= b.O[i])
            {
                i++;
                continue;
            }

            var span = b.H[i] - b.L[i];
            if (span > 0 && (b.C[i] - b.L[i]) / span < 0.5)
            {
                i++;
                continue;
            }

            if (i == 0 || rise[i - 1] >= 0.16 || rise[i] > riseMax)
            {
                i++;
                continue;
            }

            if (skipClimax && volume[i] > 7)
            {
                i++;
                continue;
            }

            var closeMs = b.T[i] + 15 * 60_000 - 1;
            while (day + 1 < days.Count && days[day + 1].CloseMs <= closeMs)
            {
                day++;
            }

            if (day < 30 || days[day].CloseMs > closeMs || closeMs - days[day].CloseMs > 2L * 86_400_000)
            {
                i++;
                continue;
            }

            var basis = days[day - 30].Close;
            if (basis <= 0 || b.C[i] / basis - 1 < 0.20)
            {
                i++;
                continue;
            }

            if (skipFresh && AbovePriorHigh(b, days, day, i))
            {
                i++;
                continue;
            }

            var entry = b.C[i];
            var hard = entry * 0.88;
            var peak = entry;
            var closed = false;
            for (var k = i + 1; k < b.N; k++)
            {
                var stop = hard;
                if (peak / entry - 1 >= 0.20)
                {
                    stop = Math.Max(stop, Math.Max(entry * 1.002, peak * 0.70));
                }

                if (b.L[k] <= stop)
                {
                    trades.Add(new Trade(b.T[i], b.T[k], Math.Min(b.O[k], stop) / entry - 1 - Cost, 0));
                    i = k + 1;
                    closed = true;
                    break;
                }

                peak = Math.Max(peak, b.H[k]);
                if (b.C[k] <= peak * 0.75 || k - i >= Hold)
                {
                    trades.Add(new Trade(b.T[i], b.T[k], b.C[k] / entry - 1 - Cost, 0));
                    i = k + 1;
                    closed = true;
                    break;
                }
            }

            if (!closed)
            {
                var last = b.N - 1;
                trades.Add(new Trade(b.T[i], b.T[last], b.C[last] / entry - 1 - Cost, -1));
                break;
            }
        }

        return trades;
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
            var j = i;
            while (j < b.N && b.T[j] / dayMs * dayMs == dayOpen)
            {
                close = b.C[j];
                high = Math.Max(high, b.H[j]);
                j++;
            }

            days.Add(new DayBar(dayOpen + dayMs - 1, close, high));
            i = j;
        }

        return days;
    }

    private sealed class BookStats
    {
        private readonly Split _all = new();
        private readonly Split _is = new();
        private readonly Split _val = new();
        private readonly Split _oos = new();
        private readonly Split _late = new();
        private readonly Dictionary<string, double> _coins = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Symbol, Trade Trade)> _trades = [];

        public void Add(string symbol, List<Trade> trades)
        {
            foreach (var trade in trades)
            {
                if (trade.Age >= 0)
                {
                    _all.Add(trade.Net);
                    SplitOf(trade.At).Add(trade.Net);
                    if (trade.Age > 0)
                    {
                        _late.Add(trade.Net);
                    }

                    _coins[symbol] = _coins.GetValueOrDefault(symbol) + trade.Net;
                }

                _trades.Add((symbol, trade));
            }
        }

        public void PrintSince(DateTimeOffset from)
        {
            var ci = CultureInfo.InvariantCulture;
            var fromMs = from.ToUnixTimeMilliseconds();
            var rows = _trades.Where(row => row.Trade.At >= fromMs).Select(row => row.Trade).ToList();
            var closed = rows.Where(trade => trade.Age >= 0).ToList();
            var openRows = rows.Where(trade => trade.Age < 0).ToList();
            var split = new Split();
            foreach (var trade in closed)
            {
                split.Add(trade.Net);
            }

            var marked = new Split();
            foreach (var trade in rows)
            {
                marked.Add(trade.Net);
            }

            Console.WriteLine(string.Create(ci, $"since {from:yyyy-MM-dd}"));
            Console.WriteLine("  closed " + split.Line("CLOSED"));
            Console.WriteLine("  marked " + marked.Line("MARKED"));
            Console.WriteLine(string.Create(ci, $"  still open {openRows.Count}"));
            var byCoin = _trades.Where(row => row.Trade.At >= fromMs)
                .GroupBy(row => row.Symbol, StringComparer.OrdinalIgnoreCase)
                .Select(group => (Symbol: group.Key, Net: group.Sum(row => row.Trade.Net)))
                .OrderByDescending(row => row.Net)
                .ToList();
            var winners = byCoin.Count(row => row.Net > 0);
            Console.WriteLine(string.Create(ci, $"  coins +{winners} / -{byCoin.Count - winners}"));
            foreach (var row in byCoin.Take(5))
            {
                Console.WriteLine(string.Create(ci, $"    {row.Symbol} {row.Net:0.00}"));
            }

            foreach (var row in byCoin.TakeLast(3))
            {
                Console.WriteLine(string.Create(ci, $"    {row.Symbol} {row.Net:0.00}"));
            }

            var book = Portfolio(rows, 30);
            var last = DateTimeOffset.FromUnixTimeMilliseconds(rows.Max(trade => trade.Until));
            Console.WriteLine(string.Create(ci, $"  last bar {last:yyyy-MM-dd HH:mm} utc"));
            Console.WriteLine(string.Create(ci, $"  slots30 x{book.Multiple:0.000} pnl {(book.Multiple - 1):P1} maxDD {book.MaxDd:P0} losing months {book.Losing}/{book.Months} taken {book.Taken} skipped {book.Skipped}"));
            foreach (var month in book.Monthly.OrderBy(pair => pair.Key))
            {
                Console.WriteLine(string.Create(ci, $"    {month.Key} {month.Value:P1}"));
            }
        }

        public void Print()
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine("  " + _all.Line("ALL"));
            Console.WriteLine("  " + _is.Line("IS "));
            Console.WriteLine("  " + _val.Line("VAL"));
            Console.WriteLine("  " + _oos.Line("OOS"));
            Console.WriteLine("  " + _late.Line("2h "));
            var pos = _coins.Values.Count(v => v > 0);
            var neg = _coins.Count - pos;
            var ordered = _coins.OrderByDescending(pair => pair.Value).ToList();
            var net = ordered.Sum(pair => pair.Value);
            var top10 = ordered.Take(10).Sum(pair => pair.Value);
            var cover = 0;
            var running = 0.0;
            foreach (var pair in ordered)
            {
                if (pair.Value <= 0 || running >= net)
                {
                    break;
                }

                running += pair.Value;
                cover++;
            }

            Console.WriteLine(string.Create(ci, $"  coins +{pos} / -{neg} netSum {net:0.00} top10 {top10:0.00} coinsToCoverNet {cover}"));
            foreach (var pair in ordered.Take(5))
            {
                Console.WriteLine(string.Create(ci, $"    {pair.Key} {pair.Value:0.00}"));
            }
            var closedTrades = _trades.Where(row => row.Trade.Age >= 0).Select(row => row.Trade).ToList();
            var (multiple, drawdown, losing, months, taken, skipped, _) = Portfolio(closedTrades, 8);
            var wideBook = Portfolio(closedTrades, 30);
            Console.WriteLine(string.Create(ci, $"  slots8 x{multiple:0.00} maxDD {drawdown:P0} losing months {losing}/{months} taken {taken} skipped {skipped}"));
            Console.WriteLine(string.Create(ci, $"  slots30 x{wideBook.Multiple:0.00} maxDD {wideBook.MaxDd:P0} losing months {wideBook.Losing}/{wideBook.Months} taken {wideBook.Taken} skipped {wideBook.Skipped}"));
        }

        private Split SplitOf(long at)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(at);
            return t < IsEnd ? _is : t < ValEnd ? _val : _oos;
        }

        private static (double Multiple, double MaxDd, int Losing, int Months, int Taken, int Skipped, Dictionary<string, double> Monthly) Portfolio(List<Trade> trades, int slots)
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
                    maxDd = Math.Max(maxDd, 1 - equity / peak);
                    open.Remove(position);
                }

                if (open.Count >= slots)
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
                maxDd = Math.Max(maxDd, 1 - equity / peak);
            }

            return (equity, maxDd, monthly.Count(m => m.Value < 0), monthly.Count, taken, skipped, monthly);
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

    private readonly record struct Trade(long At, long Until, double Net, int Age);
    private readonly record struct DayBar(long CloseMs, double Close, double High);

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
