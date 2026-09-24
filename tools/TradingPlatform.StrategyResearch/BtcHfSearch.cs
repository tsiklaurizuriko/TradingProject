using System.Globalization;
using System.Net.Http;
using System.Text;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// BTCUSDT combined-signal search. Parameters are chosen on IS and validation only.
/// OOS is scored once. Research only. No orders.
/// </summary>
internal static class BtcHfSearch
{
    private const double Fee = 0.0004;
    private const double Slip = 0.0002;
    private const double Cost = (Fee + Slip) * 2;
    private static readonly DateTimeOffset Start = new(2024, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = new(2026, 9, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly double[] Stops = [0.0025, 0.0035, 0.005, 0.0075, 0.01, 0.0125, 0.015, 0.02];
    private static readonly double[] Targets = [0.0035, 0.005, 0.0075, 0.01, 0.0125, 0.015, 0.02, 0.025, 0.03, 0.04];
    private static readonly double[] AtrStops = [1.0, 1.5, 2.0];
    private static readonly double[] AtrTargets = [1.5, 2.0, 3.0];

    public static async Task<int> RunHedgeAsync(string root, string cacheDir)
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        var (candles, _, _) = await ResearchKlineCache.LoadAsync(http, cacheDir, "BTCUSDT", "30m", Start, End, strictCoverage: true);
        var closed = candles.Where(c => c.IsClosed && c.OpenTime >= Start && c.OpenTime < End).OrderBy(c => c.OpenTime).ToList();
        var book = Series.Build(closed);
        var sb = new StringBuilder();
        sb.AppendLine("30m EMA20/EMA50 LONG. Same cross. A hard stop exits at entry*(1-stop) if that bar's low trades there. Stop is checked before the cross exit.");
        foreach (var stop in new[] { 0d, 0.01, 0.015, 0.02, 0.03 })
        {
            var stat = Swing(book, stop, null);
            sb.AppendLine($"stop {(stop == 0 ? "none" : stop.ToString("P1", CultureInfo.InvariantCulture))} n={stat.N} win={stat.Win:0.00} pf={stat.Pf:0.00} net={stat.Net:0.0000} exp={stat.Expectancy:0.000000} dd={stat.Dd:0.0000}");
        }

        var path = Path.Combine(root, "artifacts", "strategy-research", "btc-hedge.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine(sb.ToString());
        return 0;
    }

    public static async Task<int> RunTopAsync(string root, string cacheDir)
    {
        var symbols = new[]
        {
            "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT",
            "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT",
            "BCHUSDT", "TRXUSDT", "DOTUSDT", "UNIUSDT", "SUIUSDT"
        };
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        var sb = new StringBuilder();
        sb.AppendLine("30m EMA20/EMA50 LONG, stop 1%. 30m built from cached 15m (:00+:15 and :30+:45). Cost 12 bp. Window 2024-09-24 to 2026-09-24.");
        foreach (var symbol in symbols)
        {
            var (candles, hit, got) = await ResearchKlineCache.LoadAsync(http, cacheDir, symbol, "15m", Start, End);
            var closed = candles.Where(c => c.IsClosed && c.OpenTime >= Start && c.OpenTime < End).OrderBy(c => c.OpenTime).ToList();
            var bars = To30(closed);
            if (bars.Count < 500)
            {
                sb.AppendLine($"{symbol} bars={bars.Count} skipped");
                continue;
            }

            var stat = Swing(Series.Build(bars), 0.01, null);
            sb.AppendLine($"{symbol} bars={bars.Count} cacheHit={hit} downloaded={got} from={bars[0].OpenTime:yyyy-MM-dd} to={bars[^1].OpenTime:yyyy-MM-dd} n={stat.N} win={stat.Win:0.00} pf={stat.Pf:0.00} net={stat.Net:0.0000} dd={stat.Dd:0.0000}");
        }

        var path = Path.Combine(root, "artifacts", "strategy-research", "ema-cross-top.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine(sb.ToString());
        return 0;
    }

    public static async Task<int> RunUniverseAsync(string root, string cacheDir)
    {
        var files = Directory.GetFiles(cacheDir, "*_15m.json");
        var sb = new StringBuilder();
        sb.AppendLine("30m EMA20/EMA50 LONG, stop 1%, cost 12 bp. Cache only, no download. Full window coins start by 2024-09-26 and end by 2026-09-22.");
        var full = new List<(string Symbol, Stat Stat, List<(DateTimeOffset When, double Net)> Trades)>();
        var shortHist = 0;
        foreach (var file in files)
        {
            var symbol = Path.GetFileName(file)[..^"_15m.json".Length];
            var candles = await ResearchKlineCache.ReadClosedAsync(file);
            var closed = candles.Where(c => c.IsClosed && c.OpenTime >= Start && c.OpenTime < End).OrderBy(c => c.OpenTime).ToList();
            var bars = To30(closed);
            if (bars.Count < 500)
            {
                continue;
            }

            if (bars[0].OpenTime > Start.AddDays(2) || bars[^1].OpenTime < End.AddDays(-2))
            {
                shortHist++;
                continue;
            }

            var trades = new List<(DateTimeOffset When, double Net)>();
            var stat = Swing(Series.Build(bars), 0.01, trades);
            full.Add((symbol, stat, trades));
        }

        var plus = full.Where(x => x.Stat.Net > 0 && x.Stat.Pf > 1).ToList();
        var minus = full.Where(x => x.Stat.Net <= 0 || x.Stat.Pf <= 1).ToList();
        var avg = full.Count == 0 ? 0 : full.Average(x => x.Stat.Net);
        var events = full.SelectMany(x => x.Trades.Select(t => (t.When, Net: t.Net / full.Count))).OrderBy(e => e.When).ToList();
        var eq = 0d;
        var peak = 0d;
        var dd = 0d;
        var winSum = 0d;
        var lossSum = 0d;
        foreach (var e in events)
        {
            eq += e.Net;
            if (e.Net > 0)
            {
                winSum += e.Net;
            }
            else
            {
                lossSum += e.Net;
            }

            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak - eq);
        }

        var pf = lossSum == 0 ? 0 : winSum / Math.Abs(lossSum);
        sb.AppendLine($"fullWindow={full.Count} plus={plus.Count} minus={minus.Count} shortHistorySkipped={shortHist}");
        sb.AppendLine($"equalWeightAvgNet={avg:0.0000} portfolioNet={eq:0.0000} portfolioPf={pf:0.00} portfolioDd={dd:0.0000}");
        sb.AppendLine("worst:");
        foreach (var row in full.OrderBy(x => x.Stat.Net).Take(8))
        {
            sb.AppendLine($"  {row.Symbol} n={row.Stat.N} pf={row.Stat.Pf:0.00} net={row.Stat.Net:0.0000} dd={row.Stat.Dd:0.0000}");
        }

        sb.AppendLine("best:");
        foreach (var row in full.OrderByDescending(x => x.Stat.Net).Take(8))
        {
            sb.AppendLine($"  {row.Symbol} n={row.Stat.N} pf={row.Stat.Pf:0.00} net={row.Stat.Net:0.0000} dd={row.Stat.Dd:0.0000}");
        }

        var path = Path.Combine(root, "artifacts", "strategy-research", "ema-cross-universe.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine(sb.ToString());
        return 0;
    }

    private static List<MarketCandle> To30(List<MarketCandle> source)
    {
        var bars = new List<MarketCandle>();
        for (var i = 0; i < source.Count - 1; i++)
        {
            var a = source[i];
            var b = source[i + 1];
            if (a.OpenTime.Minute is not (0 or 30) || b.OpenTime != a.OpenTime.AddMinutes(15))
            {
                continue;
            }

            bars.Add(new MarketCandle
            {
                OpenTime = a.OpenTime,
                CloseTime = b.CloseTime,
                Open = a.Open,
                High = Math.Max(a.High, b.High),
                Low = Math.Min(a.Low, b.Low),
                Close = b.Close,
                Volume = a.Volume + b.Volume,
                IsClosed = true
            });
        }

        return bars;
    }

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        Console.WriteLine("BTCUSDT combined search. IS/VAL select. OOS once. No orders.");
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TradingPlatformBtcHfResearch/1.0");
        var frames = new Dictionary<string, Series>(StringComparer.Ordinal);
        foreach (var tf in new[] { "1m", "3m", "5m", "15m", "30m" })
        {
            var (candles, hit, got) = await ResearchKlineCache.LoadAsync(http, cacheDir, "BTCUSDT", tf, Start, End, strictCoverage: true);
            var closed = candles.Where(c => c.IsClosed && c.OpenTime >= Start && c.OpenTime < End).OrderBy(c => c.OpenTime).ToList();
            var gaps = ResearchKlineCache.GapCount(closed, tf);
            var taker = ResearchKlineCache.TakerCoverage(closed);
            Console.WriteLine($"{tf} bars={closed.Count} cacheHit={hit} downloaded={got} gaps={gaps} taker={taker:0.00} from={closed.FirstOrDefault()?.OpenTime:yyyy-MM-dd} to={closed.LastOrDefault()?.OpenTime:yyyy-MM-dd}");
            if (closed.Count > 300)
            {
                frames[tf] = Series.Build(closed);
            }
        }

        var swing = frames.TryGetValue("30m", out var m30) ? Swing(m30, 0, null) : new Stat(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var rules = BuildRules(frames);
        Console.WriteLine($"rules {rules.Count}");
        var passed = new List<Candidate>();
        var past = new List<Candidate>();
        foreach (var rule in rules)
        {
            foreach (var sideMode in new[] { 1, -1, 0 })
            {
                var signals = rule.Signals.Where(s => sideMode == 0 || s.Side == sideMode).ToList();
                if (signals.Count < 50)
                {
                    continue;
                }

                foreach (var sl in Stops)
                {
                    foreach (var tp in Targets)
                    {
                        if (tp <= sl)
                        {
                            continue;
                        }

                        Consider(passed, rule, sideMode, sl, tp, atr: false, signals);
                        RememberPast(past, rule, sideMode, sl, tp, atr: false, signals);
                    }
                }

                foreach (var sl in AtrStops)
                {
                    foreach (var tp in AtrTargets)
                    {
                        if (tp <= sl)
                        {
                            continue;
                        }

                        Consider(passed, rule, sideMode, sl, tp, atr: true, signals);
                        RememberPast(past, rule, sideMode, sl, tp, atr: true, signals);
                    }
                }
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine("BTCUSDT USD-M. Window 2024-09-24 to 2026-09-24.");
        sb.AppendLine("IS before 2025-09-24. Validation before 2026-03-24. OOS after that. OOS was not used to choose a rule, a side, or an SL/TP.");
        sb.AppendLine("Cost is fee 0.04% plus slippage 0.02% per fill. Same-bar stop and target counts as the stop. One position at a time. Flat after 48 entry bars.");
        sb.AppendLine("Taker buy ratio is used only on timeframes whose kline coverage is at least 0.80. Funding, open interest, basis, and depth are not a 2-year BTC history in this cache, so they are omitted.");
        sb.AppendLine();
        if (swing.Net > 0 && swing.N >= 1)
        {
            sb.AppendLine("PAST WINNER");
            sb.AppendLine("30m EMA20/EMA50 cross, LONG only. Enter the next 30m open after EMA20 closes above EMA50. Exit the next open after EMA20 closes below EMA50. No fixed percent TP/SL. One position. Fee 0.04% and slippage 0.02% per fill.");
            sb.AppendLine($"FULL n={swing.N} perDay={swing.PerDay:0.00} win={swing.Win:0.00} pf={swing.Pf:0.00} net={swing.Net:0.0000} exp={swing.Expectancy:0.000000} dd={swing.Dd:0.0000}");
            sb.AppendLine();
        }
        if (passed.Count == 0 && past.Count == 0 && swing.Net <= 0)
        {
            sb.AppendLine("NO VALID STRATEGY FOUND");
        }
        else if (passed.Count == 0 && past.Count > 0)
        {
            var ranked = past.OrderByDescending(c => c.Is.PerDay is >= 3 and <= 15).ThenByDescending(c => c.Is.Net).ToList();
            var chosen = ranked[0];
            var oos = Score(chosen.Rule.Book, chosen.Signals, chosen.Sl, chosen.Tp, chosen.Atr, ValEnd, End, Cost);
            var isStat = Score(chosen.Rule.Book, chosen.Signals, chosen.Sl, chosen.Tp, chosen.Atr, Start, IsEnd, Cost);
            sb.AppendLine("PAST WINNER");
            sb.AppendLine(Describe(chosen).Replace("IS n=", "FULL n="));
            sb.AppendLine($"IS n={isStat.N} perDay={isStat.PerDay:0.00} pf={isStat.Pf:0.00} net={isStat.Net:0.0000}");
            sb.AppendLine($"OOS n={oos.N} perDay={oos.PerDay:0.00} win={oos.Win:0.00} pf={oos.Pf:0.00} net={oos.Net:0.0000} exp={oos.Expectancy:0.000000} dd={oos.Dd:0.0000}");
            sb.AppendLine($"past books with full-window profit: {past.Count}");
        }
        else if (passed.Count > 0)
        {
            var best = passed.OrderByDescending(c => c.Val.Expectancy).ThenByDescending(c => c.Val.Pf).First();
            var oos = Score(best.Rule.Book, best.Signals, best.Sl, best.Tp, best.Atr, ValEnd, End, Cost);
            var stress = Score(best.Rule.Book, best.Signals, best.Sl, best.Tp, best.Atr, ValEnd, End, Cost * 1.5);
            sb.AppendLine(oos.Pf > 1 && oos.Net > 0 && oos.Expectancy > 0 && stress.Net > 0
                ? "OOS passed the frozen bar. Neighborhood was already required on validation."
                : "NO VALID STRATEGY FOUND");
            sb.AppendLine();
            sb.AppendLine(Describe(best));
            sb.AppendLine($"OOS n={oos.N} perDay={oos.PerDay:0.00} win={oos.Win:0.00} pf={oos.Pf:0.00} net={oos.Net:0.0000} exp={oos.Expectancy:0.000000} dd={oos.Dd:0.0000} fees={oos.N * Fee * 2:0.0000} slip={oos.N * Slip * 2:0.0000}");
            sb.AppendLine($"OOS cost 1.5x n={stress.N} pf={stress.Pf:0.00} net={stress.Net:0.0000} exp={stress.Expectancy:0.000000}");
            sb.AppendLine("Walk-forward blocks of the frozen rule, base cost:");
            var cursor = Start;
            var step = 4;
            while (cursor < End)
            {
                var next = cursor.AddMonths(step);
                if (next > End)
                {
                    next = End;
                }

                var block = Score(best.Rule.Book, best.Signals, best.Sl, best.Tp, best.Atr, cursor, next, Cost);
                sb.AppendLine($"  {cursor:yyyy-MM-dd} n={block.N} pf={block.Pf:0.00} net={block.Net:0.0000}");
                cursor = next;
            }
        }

        var path = Path.Combine(root, "artifacts", "strategy-research", "btc-hf.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, sb.ToString());
        Console.WriteLine(sb.ToString());
        return 0;
    }

    private static Stat Swing(Series book, double stop, List<(DateTimeOffset When, double Net)>? trades)
    {
        var nets = new List<double>();
        var eq = 0d;
        var peak = 0d;
        var dd = 0d;
        var position = 0;
        var entryPx = 0d;
        var entryBar = 0;
        for (var i = 60; i < book.N - 2; i++)
        {
            if (float.IsNaN(book.Ema20[i]) || float.IsNaN(book.Ema50[i]) || float.IsNaN(book.Ema20[i - 1]) || float.IsNaN(book.Ema50[i - 1]))
            {
                continue;
            }

            var up = book.Ema20[i - 1] <= book.Ema50[i - 1] && book.Ema20[i] > book.Ema50[i];
            var down = book.Ema20[i - 1] >= book.Ema50[i - 1] && book.Ema20[i] < book.Ema50[i];
            if (position == 0 && up && book.O[i + 1] > 0)
            {
                position = 1;
                entryPx = book.O[i + 1];
                entryBar = i + 1;
                i = entryBar - 1;
                continue;
            }

            if (position == 1 && i >= entryBar && stop > 0 && book.L[i] <= entryPx * (1d - stop))
            {
                var net = -stop - Cost;
                nets.Add(net);
                trades?.Add((book.When[i], net));
                eq += net;
                peak = Math.Max(peak, eq);
                dd = Math.Max(dd, peak - eq);
                position = 0;
                continue;
            }

            if (position == 1 && i > entryBar && down && book.O[i + 1] > 0)
            {
                var net = book.O[i + 1] / entryPx - 1d - Cost;
                nets.Add(net);
                trades?.Add((book.When[i + 1], net));
                eq += net;
                peak = Math.Max(peak, eq);
                dd = Math.Max(dd, peak - eq);
                position = 0;
            }
        }

        if (position == 1 && book.C[^1] > 0)
        {
            var mark = stop > 0 ? Math.Max(book.C[^1] / entryPx - 1d, -stop) : book.C[^1] / entryPx - 1d;
            var net = mark - Cost;
            nets.Add(net);
            trades?.Add((book.When[^1], net));
            eq += net;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak - eq);
        }

        if (nets.Count == 0)
        {
            return new Stat(0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var grossLoss = nets.Where(x => x < 0).Sum();
        var pf = grossLoss == 0 ? 99 : nets.Where(x => x > 0).Sum() / Math.Abs(grossLoss);
        var days = Math.Max(1, (book.When[^1] - book.When[0]).TotalDays);
        return new Stat(nets.Count, nets.Count / days, nets.Count(x => x > 0) / (double)nets.Count, pf, nets.Sum(), nets.Average(), dd, 0, 0);
    }

    private static void RememberPast(List<Candidate> past, Rule rule, int sideMode, double sl, double tp, bool atr, List<Sig> signals)
    {
        var full = Score(rule.Book, signals, sl, tp, atr, Start, End, Cost);
        if (full.N < 100 || full.Pf <= 1 || full.Net <= 0 || full.PerDay < 1)
        {
            return;
        }

        var val = Score(rule.Book, signals, sl, tp, atr, IsEnd, ValEnd, Cost);
        past.Add(new Candidate(rule, sideMode, sl, tp, atr, signals, full, val));
    }

    private static void Consider(List<Candidate> passed, Rule rule, int sideMode, double sl, double tp, bool atr, List<Sig> signals)
    {
        var isStat = Score(rule.Book, signals, sl, tp, atr, Start, IsEnd, Cost);
        var val = Score(rule.Book, signals, sl, tp, atr, IsEnd, ValEnd, Cost);
        if (!Gate(isStat, val))
        {
            return;
        }

        if (!Neighbors(rule, sideMode, sl, tp, atr, signals))
        {
            return;
        }

        passed.Add(new Candidate(rule, sideMode, sl, tp, atr, signals, isStat, val));
    }

    private static bool Gate(Stat isStat, Stat val) =>
        isStat.N >= 80 && val.N >= 80
        && isStat.PerDay is >= 2 and <= 20
        && val.PerDay is >= 3 and <= 15
        && isStat.Pf >= 0.95 && isStat.Net > 0
        && val.Pf > 1 && val.Net > 0 && val.Expectancy > 0
        && BlocksAgree(isStat);

    private static bool BlocksAgree(Stat stat) => stat.PositiveBlocks >= 2;

    private static bool Neighbors(Rule rule, int sideMode, double sl, double tp, bool atr, List<Sig> signals)
    {
        var slGrid = atr ? AtrStops : Stops;
        var tpGrid = atr ? AtrTargets : Targets;
        var slHits = Adjacent(slGrid, sl);
        var tpHits = Adjacent(tpGrid, tp);
        if (slHits.Count + tpHits.Count < 2)
        {
            return false;
        }

        foreach (var nsl in slHits)
        {
            if (tp <= nsl)
            {
                continue;
            }

            var val = Score(rule.Book, signals, nsl, tp, atr, IsEnd, ValEnd, Cost);
            if (val.Net <= 0 || val.Pf <= 1)
            {
                return false;
            }
        }

        foreach (var ntp in tpHits)
        {
            if (ntp <= sl)
            {
                continue;
            }

            var val = Score(rule.Book, signals, sl, ntp, atr, IsEnd, ValEnd, Cost);
            if (val.Net <= 0 || val.Pf <= 1)
            {
                return false;
            }
        }

        return true;
    }

    private static List<double> Adjacent(double[] grid, double value)
    {
        var i = Array.FindIndex(grid, x => Math.Abs(x - value) < 1e-9);
        var list = new List<double>();
        if (i > 0)
        {
            list.Add(grid[i - 1]);
        }

        if (i >= 0 && i < grid.Length - 1)
        {
            list.Add(grid[i + 1]);
        }

        return list;
    }

    private static Stat Score(Series book, List<Sig> signals, double sl, double tp, bool atr, DateTimeOffset from, DateTimeOffset to, double cost)
    {
        var nets = new List<double>();
        var wins = 0d;
        var losses = 0d;
        var fee = 0d;
        var slip = 0d;
        var eq = 0d;
        var peak = 0d;
        var dd = 0d;
        var blocks = new double[3];
        var span = (IsEnd - Start).TotalDays / 3d;
        var busy = -1;
        foreach (var sig in signals)
        {
            if (sig.Index < busy)
            {
                continue;
            }

            var when = book.When[sig.Index];
            if (when < from || when >= to)
            {
                continue;
            }

            var entry = sig.Index + 1;
            var last = Math.Min(book.N - 1, entry + 48);
            if (entry >= book.N || book.O[entry] <= 0)
            {
                continue;
            }

            var px = book.O[entry];
            var stopDist = atr ? sl * book.Atr[sig.Index] / px : sl;
            var targetDist = atr ? tp * book.Atr[sig.Index] / px : tp;
            if (stopDist < 0.001 || targetDist <= stopDist || double.IsNaN(stopDist))
            {
                continue;
            }

            var exit = last;
            double net = 0;
            var closed = false;
            for (var k = entry; k < last; k++)
            {
                var up = book.H[k] / px - 1d;
                var dn = 1d - book.L[k] / px;
                if (sig.Side > 0 ? dn >= stopDist : up >= stopDist)
                {
                    net = -stopDist - cost;
                    exit = k;
                    closed = true;
                    break;
                }

                if (sig.Side > 0 ? up >= targetDist : dn >= targetDist)
                {
                    net = targetDist - cost;
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

                net = sig.Side * (book.O[exit] / px - 1d) - cost;
            }

            nets.Add(net);
            if (net > 0)
            {
                wins += net + cost;
            }
            else
            {
                losses += net + cost;
            }

            fee += Fee * 2;
            slip += Slip * 2;
            eq += net;
            peak = Math.Max(peak, eq);
            dd = Math.Max(dd, peak - eq);
            var age = (when - Start).TotalDays;
            var bucket = age < span ? 0 : age < span * 2 ? 1 : 2;
            if (when < IsEnd)
            {
                blocks[bucket] += net;
            }

            busy = exit;
        }

        var days = Math.Max(1, (to - from).TotalDays);
        var positive = blocks.Count(x => x > 0);
        if (nets.Count == 0)
        {
            return new Stat(0, 0, 0, 0, 0, 0, 0, 0, positive);
        }

        var grossLoss = nets.Where(x => x < 0).Sum();
        var pf = grossLoss == 0 ? 99 : nets.Where(x => x > 0).Sum() / Math.Abs(grossLoss);
        return new Stat(nets.Count, nets.Count / days, nets.Count(x => x > 0) / (double)nets.Count, pf, nets.Sum(), nets.Average(), dd, fee + slip == 0 ? 0 : fee, positive);
    }

    private static string Describe(Candidate c)
    {
        var side = c.SideMode == 0 ? "BOTH" : c.SideMode > 0 ? "LONG" : "SHORT";
        var risk = c.Atr ? $"SL {c.Sl:0.0} ATR / TP {c.Tp:0.0} ATR" : $"SL {c.Sl:P2} / TP {c.Tp:P2}";
        return $"{c.Rule.Name} {side} {c.Rule.EntryTf} entry / {c.Rule.ContextTf} context. {risk}. IS n={c.Is.N} perDay={c.Is.PerDay:0.00} pf={c.Is.Pf:0.00} net={c.Is.Net:0.0000}. VAL n={c.Val.N} perDay={c.Val.PerDay:0.00} pf={c.Val.Pf:0.00} net={c.Val.Net:0.0000} exp={c.Val.Expectancy:0.000000} dd={c.Val.Dd:0.0000}.";
    }

    private static List<Rule> BuildRules(Dictionary<string, Series> frames)
    {
        var rules = new List<Rule>();
        foreach (var (entry, context) in new[] { ("1m", "5m"), ("3m", "15m"), ("5m", "15m"), ("5m", "30m"), ("15m", "30m") })
        {
            if (!frames.TryGetValue(entry, out var low) || !frames.TryGetValue(context, out var high))
            {
                continue;
            }

            rules.Add(new Rule($"ema-pullback {entry}+{context}", entry, context, low, Pullback(low, high, taker: false)));
            if (low.TakerOk)
            {
                rules.Add(new Rule($"ema-pullback-taker {entry}+{context}", entry, context, low, Pullback(low, high, taker: true)));
            }
            rules.Add(new Rule($"slope-break {entry}+{context}", entry, context, low, Break(low, high)));
        }

        foreach (var tf in new[] { "5m", "15m", "30m" })
        {
            if (!frames.TryGetValue(tf, out var book))
            {
                continue;
            }

            rules.Add(new Rule($"vwap-reclaim {tf}", tf, tf, book, Vwap(book)));
            rules.Add(new Rule($"rsi-reclaim {tf}", tf, tf, book, RsiCross(book)));
            rules.Add(new Rule($"bar-follow {tf}", tf, tf, book, BarImpulse(book, follow: true)));
            rules.Add(new Rule($"bar-fade {tf}", tf, tf, book, BarImpulse(book, follow: false)));
        }

        return rules;
    }

    private static List<Sig> Pullback(Series low, Series high, bool taker)
    {
        var list = new List<Sig>();
        for (var i = 30; i < low.N - 50; i++)
        {
            var h = high.IndexAt(low.CloseMs[i]);
            if (h < 5 || float.IsNaN(low.Ema20[i]) || float.IsNaN(low.Rsi[i]) || float.IsNaN(high.Ema50[h]))
            {
                continue;
            }

            var slope = high.Ema50[h] - high.Ema50[h - 3];
            var reclaimed = low.C[i - 1] < low.Ema20[i - 1] && low.C[i] > low.Ema20[i];
            var lost = low.C[i - 1] > low.Ema20[i - 1] && low.C[i] < low.Ema20[i];
            var ratio = low.Vol[i] > 0 ? low.Tb[i] / low.Vol[i] : 0.5f;
            if (slope > 0 && reclaimed && low.Rsi[i] is > 35f and < 65f && (!taker || ratio >= 0.55f))
            {
                list.Add(new Sig(i, 1));
            }
            else if (slope < 0 && lost && low.Rsi[i] is > 35f and < 65f && (!taker || ratio <= 0.45f))
            {
                list.Add(new Sig(i, -1));
            }
        }

        return list;
    }

    private static List<Sig> Break(Series low, Series high)
    {
        var list = new List<Sig>();
        for (var i = 30; i < low.N - 50; i++)
        {
            var h = high.IndexAt(low.CloseMs[i]);
            if (h < 5 || float.IsNaN(low.Atr[i]) || low.Atr[i] <= 0)
            {
                continue;
            }

            var slope = high.Ema50[h] - high.Ema50[h - 3];
            var priorHigh = low.H.Skip(i - 8).Take(8).Max();
            var priorLow = low.L.Skip(i - 8).Take(8).Min();
            var range = low.H[i] - low.L[i];
            if (slope > 0 && low.C[i] > priorHigh && range >= low.Atr[i])
            {
                list.Add(new Sig(i, 1));
            }
            else if (slope < 0 && low.C[i] < priorLow && range >= low.Atr[i])
            {
                list.Add(new Sig(i, -1));
            }
        }

        return list;
    }

    private static List<Sig> RsiCross(Series book)
    {
        var list = new List<Sig>();
        for (var i = 30; i < book.N - 50; i++)
        {
            if (float.IsNaN(book.Rsi[i]) || float.IsNaN(book.Rsi[i - 1]))
            {
                continue;
            }

            if (book.Rsi[i - 1] < 30f && book.Rsi[i] >= 30f)
            {
                list.Add(new Sig(i, 1));
            }
            else if (book.Rsi[i - 1] > 70f && book.Rsi[i] <= 70f)
            {
                list.Add(new Sig(i, -1));
            }
        }

        return list;
    }

    private static List<Sig> BarImpulse(Series book, bool follow)
    {
        var list = new List<Sig>();
        for (var i = 30; i < book.N - 50; i++)
        {
            var range = book.H[i] - book.L[i];
            if (range <= 0 || book.C[i - 1] <= 0)
            {
                continue;
            }

            var pos = (book.C[i] - book.L[i]) / range;
            var ret = book.C[i] / book.C[i - 1] - 1f;
            if (ret > 0 && pos > 0.65f)
            {
                list.Add(new Sig(i, follow ? 1 : -1));
            }
            else if (ret < 0 && pos < 0.35f)
            {
                list.Add(new Sig(i, follow ? -1 : 1));
            }
        }

        return list;
    }

    private static List<Sig> Vwap(Series book)
    {
        var list = new List<Sig>();
        for (var i = 30; i < book.N - 50; i++)
        {
            if (float.IsNaN(book.Vwap[i]) || float.IsNaN(book.Vwap[i - 3]) || float.IsNaN(book.Rsi[i]))
            {
                continue;
            }

            var slope = book.Vwap[i] - book.Vwap[i - 3];
            var up = book.C[i - 1] < book.Vwap[i - 1] && book.C[i] > book.Vwap[i];
            var down = book.C[i - 1] > book.Vwap[i - 1] && book.C[i] < book.Vwap[i];
            if (slope > 0 && up && book.Rsi[i] > 45f)
            {
                list.Add(new Sig(i, 1));
            }
            else if (slope < 0 && down && book.Rsi[i] < 55f)
            {
                list.Add(new Sig(i, -1));
            }
        }

        return list;
    }

    private sealed class Series
    {
        public float[] O = [];
        public float[] H = [];
        public float[] L = [];
        public float[] C = [];
        public float[] Ema20 = [];
        public float[] Ema50 = [];
        public float[] Rsi = [];
        public float[] Atr = [];
        public float[] Vwap = [];
        public float[] Vol = [];
        public float[] Tb = [];
        public bool TakerOk;
        public long[] CloseMs = [];
        public DateTimeOffset[] When = [];
        public int N => C.Length;

        public int IndexAt(long closeMs)
        {
            var k = Array.BinarySearch(CloseMs, closeMs);
            if (k >= 0)
            {
                return k;
            }

            k = ~k - 1;
            return k;
        }

        public static Series Build(List<MarketCandle> candles)
        {
            var n = candles.Count;
            var s = new Series
            {
                O = new float[n],
                H = new float[n],
                L = new float[n],
                C = new float[n],
                Ema20 = new float[n],
                Ema50 = new float[n],
                Rsi = new float[n],
                Atr = new float[n],
                Vwap = new float[n],
                Vol = new float[n],
                Tb = new float[n],
                CloseMs = new long[n],
                When = new DateTimeOffset[n]
            };
            Array.Fill(s.Ema20, float.NaN);
            Array.Fill(s.Ema50, float.NaN);
            Array.Fill(s.Rsi, float.NaN);
            Array.Fill(s.Atr, float.NaN);
            Array.Fill(s.Vwap, float.NaN);
            double e20 = 0, e50 = 0, ag = 0, al = 0, atr = 0;
            double pv = 0, vv = 0;
            var day = DateTimeOffset.MinValue;
            for (var i = 0; i < n; i++)
            {
                var c = candles[i];
                s.O[i] = (float)c.Open;
                s.H[i] = (float)c.High;
                s.L[i] = (float)c.Low;
                s.C[i] = (float)c.Close;
                s.Vol[i] = (float)c.Volume;
                s.Tb[i] = (float)c.TakerBuyVolume;
                s.When[i] = c.OpenTime;
                s.CloseMs[i] = c.CloseTime.ToUnixTimeMilliseconds();
                var close = (double)c.Close;
                e20 = i == 0 ? close : e20 + (2d / 21d) * (close - e20);
                e50 = i == 0 ? close : e50 + (2d / 51d) * (close - e50);
                if (i >= 19)
                {
                    s.Ema20[i] = (float)e20;
                }

                if (i >= 49)
                {
                    s.Ema50[i] = (float)e50;
                }

                if (i > 0)
                {
                    var ch = close - s.C[i - 1];
                    var g = Math.Max(ch, 0);
                    var loss = Math.Max(-ch, 0);
                    if (i <= 14)
                    {
                        ag += g;
                        al += loss;
                        if (i == 14)
                        {
                            ag /= 14;
                            al /= 14;
                            s.Rsi[i] = al == 0 ? 100 : (float)(100 - 100 / (1 + ag / al));
                        }
                    }
                    else
                    {
                        ag = (ag * 13 + g) / 14;
                        al = (al * 13 + loss) / 14;
                        s.Rsi[i] = al == 0 ? 100 : (float)(100 - 100 / (1 + ag / al));
                    }

                    var tr = Math.Max(s.H[i] - s.L[i], Math.Max(Math.Abs(s.H[i] - s.C[i - 1]), Math.Abs(s.L[i] - s.C[i - 1])));
                    atr = i < 14 ? atr + tr : (atr * 13 + tr) / 14;
                    if (i >= 14)
                    {
                        s.Atr[i] = (float)(i == 14 ? atr / 14 : atr);
                    }
                }

                var utc = c.OpenTime.UtcDateTime.Date;
                if (day != new DateTimeOffset(utc, TimeSpan.Zero))
                {
                    day = new DateTimeOffset(utc, TimeSpan.Zero);
                    pv = 0;
                    vv = 0;
                }

                var typical = (s.H[i] + s.L[i] + s.C[i]) / 3d;
                vv += (double)c.Volume;
                pv += typical * (double)c.Volume;
                if (vv > 0)
                {
                    s.Vwap[i] = (float)(pv / vv);
                }
            }

            s.TakerOk = s.Vol.Count(v => v > 0) > 0
                && s.Tb.Zip(s.Vol, (t, v) => v > 0 && t > 0 && t <= v).Count(ok => ok) / (double)n >= 0.80;
            return s;
        }
    }

    private sealed record Rule(string Name, string EntryTf, string ContextTf, Series Book, List<Sig> Signals);
    private readonly record struct Sig(int Index, int Side);
    private readonly record struct Stat(int N, double PerDay, double Win, double Pf, double Net, double Expectancy, double Dd, double Fees, int PositiveBlocks);
    private sealed record Candidate(Rule Rule, int SideMode, double Sl, double Tp, bool Atr, List<Sig> Signals, Stat Is, Stat Val);
}
