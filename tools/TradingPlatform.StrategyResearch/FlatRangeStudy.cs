using System.Globalization;
using System.Collections.Concurrent;
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

    public static async Task<int> RunOctoberHoldout(string root, string cacheDir, string candles15mDir, string fundingDir, string outputPath)
    {
        Console.WriteLine("Flat range locked-rule October holdout. Research only. No orders.");
        var trades = new List<(string Symbol, long At, double Gross)>();
        var productionAlignedTrades = new List<HoldoutTrade>();
        var books = 0;
        var hourlyBars = 0;
        var holdoutStart = new DateTimeOffset(2026, 10, 6, 6, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        foreach (var file in Directory.GetFiles(cacheDir, "*_1h.json"))
        {
            var symbol = Path.GetFileName(file).Replace("_1h.json", "", StringComparison.OrdinalIgnoreCase);
            var recentPath = Path.Combine(candles15mDir, symbol + ".json");
            if (!File.Exists(recentPath)) continue;

            var history = Read(file);
            var recent = ReadHourlyFrom15m(recentPath);
            if (recent.N == 0) continue;
            hourlyBars += recent.N;
            var book = Merge(history, recent);
            if (book.N < Lookback + 120) continue;
            books++;

            var start = Array.BinarySearch(book.OpenMs, holdoutStart);
            if (start < 0) start = ~start;
            start = Math.Max(start, Lookback + 100);
            trades.AddRange(SimulateOctober(book, symbol, start, boundStops: false).Select(trade => (trade.Symbol, trade.At, trade.Gross)));
            productionAlignedTrades.AddRange(SimulateOctober(book, symbol, start, boundStops: true));
        }

        var startFunding = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var endFunding = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var funding = await FetchFundingAsync(
            productionAlignedTrades.Select(trade => trade.Symbol).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            fundingDir, startFunding, endFunding, CancellationToken.None);
        var productionWithFunding = productionAlignedTrades.Select(trade =>
        {
            var fundingReturn = 0.0;
            if (funding.Rates.TryGetValue(trade.Symbol, out var settlements))
            {
                foreach (var settlement in settlements)
                {
                    if (settlement.FundingTime < trade.EntryTimeMs || settlement.FundingTime > trade.ExitTimeMs) continue;
                    var priceRatio = settlement.MarkPrice > 0 ? settlement.MarkPrice / trade.EntryPrice : 1.0;
                    fundingReturn -= trade.Side * settlement.FundingRate * priceRatio;
                }
            }

            return trade with { Gross = trade.Gross + fundingReturn };
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Flat range: locked-rule October holdout");
        sb.AppendLine();
        sb.AppendLine("This is a prospective temporal holdout of the existing `FlatRangeStudy` rule. No parameters were changed. It starts 2026-10-06 06:00 UTC so all 24-hour bands and prior-100-band width ranks are formed entirely from October bars; earlier 1h cache has a two-hour boundary gap before October. Only entries with a complete 24-hour exit window are included.");
        sb.AppendLine();
        sb.AppendLine($"Current-cache symbols with an hourly overlay: {books}. Aggregated October hourly bars: {hourlyBars}. The universe contains current cached contracts only, not delisted symbols.");
        sb.AppendLine();
        sb.AppendLine("Both replays use the unchanged prior-24h band and lowest-30%-of-prior-100-bands entry rule. Fees and slippage are 0.12% round trip at 1x, stressed at 1.5x and 2x.");
        sb.AppendLine();
        sb.AppendLine($"The close-based research exit produced {trades.Count} trades: exit at opposite 20%, a close through the band, or 24 hours.");
        sb.AppendLine();
        sb.AppendLine("| Round-trip cost | Trades | Mean net | Net sum | PF | Win | Top 5 winners share of gross gains | Mean after removing top 5 winners |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        AppendOctoberMetrics(sb, trades);

        sb.AppendLine();
        sb.AppendLine($"The production-aligned risk rule produced {productionAlignedTrades.Count} trades: exchange stop at the entry-side band bound, take profit at the opposite bound, stop-first when both touch in one hourly candle, 24-hour time exit, skip stop distances below 0.20%, and skip take distance not greater than stop distance. This matches the deployed `FlatRangeStrategy` stop/take definition more closely than the close-based research exit.");
        sb.AppendLine();
        sb.AppendLine("| Round-trip cost | Trades | Mean net | Net sum | PF | Win | Top 5 winners share of gross gains | Mean after removing top 5 winners |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        AppendOctoberMetrics(sb, productionAlignedTrades.Select(trade => (trade.Symbol, trade.At, trade.Gross)).ToList());

        sb.AppendLine();
        sb.AppendLine($"Funding data fetched for {funding.SuccessfulSymbols}/{funding.RequestedSymbols} symbols, covering {funding.SettlementRows} settlements; failures: {funding.Failures.Length}. Funding cash flow is applied at published settlement timestamps using the returned mark price and trade direction. This is separate from fees/slippage.");
        sb.AppendLine();
        sb.AppendLine("| Round-trip cost with funding | Trades | Mean net | Net sum | PF | Win | Top 5 winners share of gross gains | Mean after removing top 5 winners |");
        sb.AppendLine("| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        AppendOctoberMetrics(sb, productionWithFunding.Select(trade => (trade.Symbol, trade.At, trade.Gross)).ToList());
        if (funding.Failures.Length > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Funding-adjusted results are incomplete because some symbols failed to fetch funding data:");
            foreach (var failure in funding.Failures.Take(20)) sb.AppendLine("- " + failure);
        }

        var productionNet15 = productionWithFunding.Select(trade => (Trade: trade, Net: trade.Gross - 2 * (FeePerSide + SlipPerSide) * 1.5)).ToList();
        var bySymbol = productionNet15.GroupBy(row => row.Trade.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Symbol = group.Key, Count = group.Count(), Mean = group.Average(row => row.Net), Sum = group.Sum(row => row.Net) })
            .OrderBy(row => row.Mean).ToList();
        var positiveGainSum = productionNet15.Where(row => row.Net > 0).Sum(row => row.Net);
        var top10SymbolGain = productionNet15.GroupBy(row => row.Trade.Symbol, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Sum(row => Math.Max(0, row.Net))).OrderByDescending(value => value).Take(10).Sum();
        var positionEvents = productionWithFunding.SelectMany(trade => new[] { (Time: trade.EntryTimeMs, Delta: 1), (Time: trade.ExitTimeMs, Delta: -1) })
            .OrderBy(item => item.Time).ThenBy(item => item.Delta).ToArray();
        var openPositions = 0;
        var maxOpenPositions = 0;
        foreach (var item in positionEvents)
        {
            openPositions += item.Delta;
            maxOpenPositions = Math.Max(maxOpenPositions, openPositions);
        }
        var meansBySymbol = bySymbol.Select(row => row.Mean).OrderBy(value => value).ToArray();
        var p10 = meansBySymbol.Length == 0 ? double.NaN : meansBySymbol[(int)Math.Floor((meansBySymbol.Length - 1) * 0.10)];
        var median = meansBySymbol.Length == 0 ? double.NaN : meansBySymbol[meansBySymbol.Length / 2];
        sb.AppendLine();
        sb.AppendLine("## Cross-symbol and capacity checks at 1.5x cost, including funding");
        sb.AppendLine();
        sb.AppendLine($"Symbols with trades: {bySymbol.Count}. Symbols with positive per-trade mean: {bySymbol.Count(row => row.Mean > 0)} ({(bySymbol.Count == 0 ? 0 : bySymbol.Count(row => row.Mean > 0) / (double)bySymbol.Count):P0}). Median symbol mean: {Fmt(median):P2}; 10th percentile: {Fmt(p10):P2}. Symbols with at least 5 trades: {bySymbol.Count(row => row.Count >= 5)}. Top 10 symbols' share of positive per-trade gains: {(positiveGainSum == 0 ? 0 : top10SymbolGain / positiveGainSum):P0}.");
        sb.AppendLine();
        sb.AppendLine($"Peak simultaneous positions if every signal is traded at once: {maxOpenPositions}. The 5,886-trade aggregate assumes a separate equal-notional slot per symbol and is not a one-position account backtest. The production risk profile's simultaneous-position and portfolio-risk limits must cap actual deployment; this report does not size an account or compound returns.");

        sb.AppendLine();
        sb.AppendLine("This is one fixed-rule holdout check, not a parameter search or paper/live approval. A positive result would still require execution-aware costs, delisted-symbol coverage, and a longer untouched forward paper record.");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine("Wrote " + outputPath);
        return 0;
    }

    private static List<HoldoutTrade> SimulateOctober(Bar book, string symbol, int start, bool boundStops)
    {
        var trades = new List<HoldoutTrade>();
        for (var i = start; i + MaxHold < book.N;)
        {
            if (!Flat(book, i, out var lo, out var hi))
            {
                i++;
                continue;
            }

            var entry = book.C[i];
            var width = hi - lo;
            var position = (entry - lo) / width;
            var side = position <= EntryFrac ? 1 : position >= 1 - EntryFrac ? -1 : 0;
            if (side == 0)
            {
                i++;
                continue;
            }

            var stop = side > 0 ? lo : hi;
            var take = side > 0 ? hi : lo;
            var stopPct = Math.Abs(entry - stop) / entry;
            var takePct = Math.Abs(take - entry) / entry;
            if (boundStops && (stopPct < 0.002 || takePct <= stopPct))
            {
                i++;
                continue;
            }

            var exit = entry;
            var hold = 1;
            var last = Math.Min(book.N - 1, i + MaxHold);
            for (var j = i + 1; j <= last; j++)
            {
                hold = j - i;
                exit = book.C[j];
                if (boundStops)
                {
                    var hitStop = side > 0 ? book.L[j] <= stop : book.H[j] >= stop;
                    var hitTake = side > 0 ? book.H[j] >= take : book.L[j] <= take;
                    if (hitStop)
                    {
                        exit = stop;
                        break;
                    }

                    if (hitTake)
                    {
                        exit = take;
                        break;
                    }
                }
                else
                {
                    if ((side > 0 && book.L[j] < lo) || (side < 0 && book.H[j] > hi))
                    {
                        exit = side > 0 ? lo : hi;
                        break;
                    }

                    var mark = (book.C[j] - lo) / width;
                    if ((side > 0 && mark >= ExitFrac) || (side < 0 && mark <= 1 - ExitFrac)) break;
                }
            }

            var entryTime = book.OpenMs[i] + 3_600_000;
            var exitTime = book.OpenMs[i + hold] + 3_600_000;
            trades.Add(new HoldoutTrade(symbol, book.OpenMs[i], entryTime, exitTime, entry, side, side * (exit / entry - 1.0)));
            i += hold + 1;
        }

        return trades;
    }

    private static void AppendOctoberMetrics(StringBuilder sb, IReadOnlyList<(string Symbol, long At, double Gross)> trades)
    {
        foreach (var stress in new[] { 1.0, 1.5, 2.0 })
        {
            var cost = 2 * (FeePerSide + SlipPerSide) * stress;
            var net = trades.Select(trade => trade.Gross - cost).ToList();
            var gains = net.Where(value => value > 0).Sum();
            var losses = -net.Where(value => value < 0).Sum();
            var top = net.Where(value => value > 0).OrderByDescending(value => value).Take(5).ToList();
            var remaining = net.OrderByDescending(value => value).Skip(Math.Min(5, net.Count)).ToList();
            var pf = losses > 0 ? gains / losses : double.NaN;
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {stress:0.0}x ({cost:P2}) | {net.Count} | {Mean(net):P2} | {net.Sum():P2} | {Fmt(pf)} | {(net.Count == 0 ? 0 : net.Count(value => value > 0) / (double)net.Count):P0} | {(gains == 0 ? 0 : top.Sum() / gains):P0} | {(remaining.Count == 0 ? double.NaN : Mean(remaining)):P2} |"));
        }
    }

    private static async Task<FundingDownload> FetchFundingAsync(
        IReadOnlyList<string> symbols,
        string outputDirectory,
        long startTime,
        long endTime,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        using var http = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(30) };
        var rates = new ConcurrentDictionary<string, List<FundingSettlement>>(StringComparer.OrdinalIgnoreCase);
        var failures = new ConcurrentBag<string>();
        var successful = 0;
        var rowCount = 0;
        await Parallel.ForEachAsync(symbols, new ParallelOptions { MaxDegreeOfParallelism = 2, CancellationToken = cancellationToken }, async (symbol, token) =>
        {
            try
            {
                var path = Path.Combine(outputDirectory, symbol + ".json");
                if (File.Exists(path))
                {
                    try
                    {
                        var existing = JsonSerializer.Deserialize<List<FundingSettlement>>(await File.ReadAllTextAsync(path, token));
                        if (existing is not null)
                        {
                            rates[symbol] = existing;
                            Interlocked.Increment(ref successful);
                            Interlocked.Add(ref rowCount, existing.Count);
                            return;
                        }
                    }
                    catch (JsonException)
                    {
                        // Re-fetch a malformed partial file.
                    }
                }

                var url = string.Create(CultureInfo.InvariantCulture, $"fapi/v1/fundingRate?symbol={Uri.EscapeDataString(symbol)}&startTime={startTime}&endTime={endTime}&limit=1000");
                using var response = await GetFundingResponseAsync(http, url, token);
                using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(token));
                if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException($"Unexpected funding response for {symbol}.");
                var settlements = new List<FundingSettlement>();
                foreach (var row in document.RootElement.EnumerateArray())
                {
                    var time = row.GetProperty("fundingTime").GetInt64();
                    var rate = double.Parse(row.GetProperty("fundingRate").GetString()!, CultureInfo.InvariantCulture);
                    var mark = row.TryGetProperty("markPrice", out var markElement) && double.TryParse(markElement.GetString(), CultureInfo.InvariantCulture, out var value) ? value : 0.0;
                    settlements.Add(new FundingSettlement(time, rate, mark));
                }

                rates[symbol] = settlements;
                Interlocked.Increment(ref successful);
                Interlocked.Add(ref rowCount, settlements.Count);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(settlements), token);
                // Binance documents a shared 500-request/5-minute budget for this endpoint.
                await Task.Delay(TimeSpan.FromMilliseconds(2_200), token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(symbol + ": " + ex.GetType().Name + " " + ex.Message);
            }
        });

        var manifest = new
        {
            DownloadedAtUtc = DateTimeOffset.UtcNow,
            StartTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(startTime),
            EndTimeUtc = DateTimeOffset.FromUnixTimeMilliseconds(endTime),
            RequestedSymbols = symbols.Count,
            SuccessfulSymbols = successful,
            SettlementRows = rowCount,
            FailedSymbols = failures.OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            Source = "Binance USD-M GET /fapi/v1/fundingRate"
        };
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return new FundingDownload(rates, symbols.Count, successful, rowCount, failures.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static async Task<HttpResponseMessage> GetFundingResponseAsync(HttpClient http, string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await http.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            var retryAfter = response.Headers.RetryAfter?.Delta;
            response.Dispose();
            if (status != 418 && status != 429 && status < 500)
            {
                throw new HttpRequestException($"Binance funding endpoint returned {status}.");
            }

            await Task.Delay(retryAfter ?? TimeSpan.FromSeconds(Math.Min(60, 5 << attempt)), cancellationToken);
        }

        throw new HttpRequestException("Binance funding endpoint retry limit reached.");
    }

    private static Bar ReadHourlyFrom15m(string path)
    {
        var inv = CultureInfo.InvariantCulture;
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var buckets = new SortedDictionary<long, List<(long Open, float High, float Low, float Close)>>();
        foreach (var row in document.RootElement.EnumerateArray())
        {
            var open = DateTimeOffset.Parse(row.GetProperty("OpenTime").GetString()!, inv).ToUnixTimeMilliseconds();
            var hour = open - open % 3_600_000;
            if (!buckets.TryGetValue(hour, out var items)) buckets[hour] = items = [];
            items.Add((open, (float)row.GetProperty("High").GetDouble(), (float)row.GetProperty("Low").GetDouble(), (float)row.GetProperty("Close").GetDouble()));
        }

        var openMs = new List<long>();
        var high = new List<float>();
        var low = new List<float>();
        var close = new List<float>();
        foreach (var (hour, items) in buckets)
        {
            items.Sort(static (a, b) => a.Open.CompareTo(b.Open));
            if (items.Count != 4 || items[0].Open != hour || items[1].Open != hour + 900_000 || items[2].Open != hour + 1_800_000 || items[3].Open != hour + 2_700_000) continue;
            openMs.Add(hour);
            high.Add(items.Max(item => item.High));
            low.Add(items.Min(item => item.Low));
            close.Add(items[^1].Close);
        }

        return new Bar { OpenMs = openMs.ToArray(), H = high.ToArray(), L = low.ToArray(), C = close.ToArray() };
    }

    private static Bar Merge(Bar history, Bar recent)
    {
        var split = Array.BinarySearch(history.OpenMs, recent.OpenMs[0]);
        if (split < 0) split = ~split;
        return new Bar
        {
            OpenMs = history.OpenMs.Take(split).Concat(recent.OpenMs).ToArray(),
            H = history.H.Take(split).Concat(recent.H).ToArray(),
            L = history.L.Take(split).Concat(recent.L).ToArray(),
            C = history.C.Take(split).Concat(recent.C).ToArray()
        };
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

    private sealed record HoldoutTrade(string Symbol, long At, long EntryTimeMs, long ExitTimeMs, double EntryPrice, int Side, double Gross);

    private sealed record FundingSettlement(long FundingTime, double FundingRate, double MarkPrice);

    private sealed record FundingDownload(
        ConcurrentDictionary<string, List<FundingSettlement>> Rates,
        int RequestedSymbols,
        int SuccessfulSymbols,
        int SettlementRows,
        string[] Failures);
}
