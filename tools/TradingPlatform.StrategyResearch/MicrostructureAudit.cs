using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

internal static class MicrostructureAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] Timeframes = ["5m", "15m", "1h"];

    private static readonly Arm[] Arms =
    [
        new("ema_rsi_trend", "FROZEN_TREND", "15m", ArmKind.Frozen),
        new("macd_trend", "FROZEN_TREND", "15m", ArmKind.Frozen),
        new("donchian_breakout", "FROZEN_TREND", "15m", ArmKind.Frozen),
        new("rsi_pullback", "FROZEN_MEAN", "15m", ArmKind.Frozen),
        new("bollinger_reversion", "FROZEN_MEAN", "15m", ArmKind.Frozen),
        new("FF-SWEEP-HOURLY", "PA_SWEEP", "15m", ArmKind.FinalFive),
        new("FF-FAILED-DOWN", "PA_FAILED", "15m", ArmKind.FinalFive),
        new("FF-EXPANSION-BOS", "PA_CONTINUATION", "15m", ArmKind.FinalFive),
        new("FF-RANGE-RELEASE", "PA_CONTINUATION", "1h", ArmKind.FinalFive),
        new("FF-RSI-QUIET", "PA_STRUCTURE", "1h", ArmKind.FinalFive),
        new("CPA-SWEEP", "PA_SWEEP", "5m", ArmKind.Contextual),
        new("CPA-PULLBACK", "PA_STRUCTURE", "5m", ArmKind.Contextual),
        new("CPA-WM", "PA_STRUCTURE", "5m", ArmKind.Contextual),
        new("CPA-COMPRESSION", "PA_CONTINUATION", "5m", ArmKind.Contextual),
        new("CPA-MTF", "PA_STRUCTURE", "5m", ArmKind.Contextual),
        new("CPA-FAILED_BREAKOUT", "PA_FAILED", "5m", ArmKind.Contextual)
    ];

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        var dir = Path.Combine(root, "artifacts", "strategy-research", "microstructure");
        Directory.CreateDirectory(dir);
        using var fapi = new HttpClient { BaseAddress = new Uri("https://fapi.binance.com/"), Timeout = TimeSpan.FromSeconds(90) };
        using var vision = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 8 }, disposeHandler: true)
        {
            BaseAddress = new Uri("https://data.binance.vision/"),
            Timeout = TimeSpan.FromSeconds(120)
        };
        var coverage = await ReadCoverageAsync(root);
        var windowStart = coverage.Where(row => FinalFiveCatalog.Symbols.Contains(row.Symbol) && Timeframes.Contains(row.Timeframe)).Min(row => row.RequestedFrom);
        var windowEnd = coverage.Where(row => FinalFiveCatalog.Symbols.Contains(row.Symbol) && Timeframes.Contains(row.Timeframe)).Max(row => row.RequestedTo);
        var notes = new List<string>();
        notes.Add(await ProbeOpenInterestAsync(fapi));
        notes.AddRange(await ProbeLiquidationsAsync(vision));
        notes.Add(await ProbeDepthArchiveAsync(vision));

        Console.WriteLine("Microstructure extending funding, mark, and index through the OHLCV window.");
        var history = new BinanceFuturesHistoryClient(fapi);
        var historyRoot = FuturesHistoryCache.Root(Path.Combine(root, "artifacts"));
        var funding = new Dictionary<string, IReadOnlyList<FundingPoint>>(StringComparer.Ordinal);
        var basis = new Dictionary<string, Dictionary<string, decimal?[]>>(StringComparer.Ordinal);
        var candles = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var fundStart = coverage.Where(row => row.Symbol == symbol && Timeframes.Contains(row.Timeframe)).Min(row => row.RequestedFrom);
            var fundEnd = coverage.Where(row => row.Symbol == symbol && Timeframes.Contains(row.Timeframe)).Max(row => row.RequestedTo);
            funding[symbol] = (await FuturesHistoryCache.LoadOrFetchFundingAsync(history, historyRoot, symbol, fundStart, fundEnd, false)).OrderBy(row => row.FundingTime).ToList();
            basis[symbol] = new Dictionary<string, decimal?[]>(StringComparer.Ordinal);
            foreach (var timeframe in Timeframes)
            {
                var range = coverage.First(row => row.Symbol == symbol && row.Timeframe == timeframe);
                candles[(symbol, timeframe)] = await LoadOne(cacheDir, root, symbol, timeframe);
                var mark = await FuturesHistoryCache.LoadOrFetchMarkAsync(history, historyRoot, symbol, timeframe, range.RequestedFrom, range.RequestedTo, false);
                var index = await FuturesHistoryCache.LoadOrFetchIndexAsync(history, historyRoot, symbol, timeframe, range.RequestedFrom, range.RequestedTo, false);
                var points = BinanceFuturesHistoryClient.BuildBasis(mark, index);
                basis[symbol][timeframe] = MicrostructureFeatures.AlignToBar(points.Select(row => (row.CloseTime, row.NormalizedBasis)).ToList(), candles[(symbol, timeframe)]);
                Console.WriteLine($"Basis {symbol} {timeframe} prints={points.Count} aligned={basis[symbol][timeframe].Count(value => value is not null)}/{candles[(symbol, timeframe)].Count}.");
            }
        }

        Console.WriteLine("Microstructure loading Vision open interest.");
        var visionDir = Path.Combine(root, "artifacts", "strategy-research", "wave-4", "vision", "metrics");
        var oi = new Dictionary<string, IReadOnlyList<VisionMetricsPoint>>(StringComparer.Ordinal);
        var visionClient = new BinanceVisionClient(vision);
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            oi[symbol] = await visionClient.LoadMetricsAsync(symbol, windowStart, windowEnd, visionDir);
            Console.WriteLine($"OI {symbol} prints={oi[symbol].Count} from={oi[symbol].FirstOrDefault()?.CreateTime:u} to={oi[symbol].LastOrDefault()?.CreateTime:u}.");
        }

        Console.WriteLine("Microstructure downloading kline taker-buy into a side cache.");
        var takerDir = Path.Combine(dir, "taker-klines");
        var takerBars = new Dictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>>();
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            foreach (var timeframe in Timeframes)
            {
                var range = coverage.First(row => row.Symbol == symbol && row.Timeframe == timeframe);
                var (loaded, _, got) = await ResearchKlineCache.LoadAsync(fapi, takerDir, symbol, timeframe, range.RequestedFrom, range.RequestedTo, requireTaker: true, strictCoverage: true);
                takerBars[(symbol, timeframe)] = loaded;
                var valid = loaded.Count(bar => MicrostructureFeatures.Taker(bar.Volume, bar.TakerBuyVolume).Ratio is not null);
                Console.WriteLine($"Taker {symbol} {timeframe} bars={loaded.Count} valid={valid} downloaded={got}.");
            }
        }

        Console.WriteLine("Microstructure downloading Vision book depth.");
        var depth = new Dictionary<string, IReadOnlyList<(DateTimeOffset Time, decimal Value)>>(StringComparer.Ordinal);
        var depthMissing = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var (depthSeries, missing, expected) = await LoadDepthAsync(vision, dir, symbol, windowStart, windowEnd);
            depth[symbol] = depthSeries;
            depthMissing[symbol] = missing;
            Console.WriteLine($"Depth {symbol} snapshots={depthSeries.Count} missingDays={missing}/{expected}.");
        }

        var datasets = Describe(coverage, funding, oi, basis, candles, takerBars, depth, depthMissing, windowStart, windowEnd, notes);
        datasets.AddRange(ReadRestOpenInterest(historyRoot));
        WriteDataAudit(root, datasets, notes);
        var scored = ScoredFeatures(datasets);
        var hash = WriteManifest(dir, scored, datasets);
        Console.WriteLine($"MICROSTRUCTURE MANIFEST {hash}");
        Console.WriteLine("Scored: " + string.Join(", ", scored));

        var rows = new List<Row>();
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var series = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.Ordinal);
            foreach (var timeframe in Timeframes)
            {
                series[timeframe] = candles[(symbol, timeframe)];
            }

            var caches = series.ToDictionary(pair => pair.Key, pair => new CausalIndicatorCache(pair.Value), StringComparer.OrdinalIgnoreCase);
            var contextual = ContextualPriceActionSignals.BuildAll(caches);
            var oiByTf = Timeframes.ToDictionary(tf => tf, tf => MicrostructureFeatures.AlignToBar(oi[symbol].Select(point => (point.CreateTime, point.SumOpenInterest)).ToList(), series[tf]), StringComparer.Ordinal);
            var takerImbalance = new Dictionary<string, decimal?[]>(StringComparer.Ordinal);
            var takerRatio = new Dictionary<string, decimal?[]>(StringComparer.Ordinal);
            foreach (var timeframe in Timeframes)
            {
                var map = takerBars[(symbol, timeframe)].GroupBy(bar => bar.CloseTime).ToDictionary(group => group.Key, group => group.Last());
                var imbalance = new decimal?[series[timeframe].Count];
                var ratio = new decimal?[series[timeframe].Count];
                for (var i = 0; i < series[timeframe].Count; i++)
                {
                    if (!map.TryGetValue(series[timeframe][i].CloseTime, out var bar))
                    {
                        continue;
                    }

                    var flow = MicrostructureFeatures.Taker(bar.Volume, bar.TakerBuyVolume);
                    imbalance[i] = flow.Imbalance;
                    ratio[i] = flow.Ratio;
                }

                takerImbalance[timeframe] = imbalance;
                takerRatio[timeframe] = ratio;
            }

            var depthByTf = Timeframes.ToDictionary(tf => tf, tf => MicrostructureFeatures.AlignToBar(depth[symbol], series[tf]), StringComparer.Ordinal);
            foreach (var arm in Arms)
            {
                var bars = series[arm.Timeframe];
                var trades = Replay(arm, symbol, bars, SignalsFor(arm, caches, contextual));
                var opens = new Dictionary<DateTimeOffset, int>();
                for (var i = 0; i < bars.Count; i++)
                {
                    opens[bars[i].OpenTime] = i;
                }

                var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(bars.Count);
                foreach (var trade in trades)
                {
                    if (!opens.TryGetValue(trade.OpenedAt, out var fill) || fill <= 0)
                    {
                        continue;
                    }

                    var signal = fill - 1;
                    var phase = signal < insEnd ? "IS" : signal < valEnd ? "VALIDATION" : "OOS";
                    var block = Math.Clamp((signal * 4 / bars.Count) + 1, 1, 4);
                    var longSide = trade.Side.Equals("Long", StringComparison.OrdinalIgnoreCase);
                    rows.Add(new Row(
                        arm.Group, arm.Id, symbol, arm.Timeframe, longSide ? "LONG" : "SHORT", phase, block,
                        trade.GrossPnl, trade.PnL, trade.PnL > 0m,
                        MicrostructureFeatures.Compute(signal, oiByTf[arm.Timeframe], funding[symbol], bars[signal].CloseTime, takerImbalance[arm.Timeframe], takerRatio[arm.Timeframe], basis[symbol][arm.Timeframe], depthByTf[arm.Timeframe])));
                }
            }
        }

        var results = scored.Select(name => Score(name, rows)).ToList();
        File.WriteAllText(Path.Combine(dir, "feature-results.json"), JsonSerializer.Serialize(results.Select(row => new
        {
            row.Name, row.Label, row.N, row.IsRho, row.ValRho, row.OosRho, row.Effect
        }), JsonOptions));
        WriteSignalAudit(root, hash, rows, results, datasets, scored);
        Console.WriteLine("FUTURES MICROSTRUCTURE AUDIT COMPLETE");
        Console.WriteLine($"Trades {rows.Count}. " + string.Join(", ", results.Select(row => row.Name + "=" + row.Label)));
        return 0;
    }

    private static List<string> ScoredFeatures(IReadOnlyList<DatasetRow> datasets)
    {
        bool Ready(string name) => datasets.Where(row => row.Dataset == name && row.Symbol != "ARCHIVE").All(row => row.Status == "AVAILABLE");
        var names = new List<string>();
        if (Ready("Open interest"))
        {
            names.AddRange(["oi_change_1", "oi_change_3", "oi_change_12", "oi_percentile"]);
        }

        if (Ready("Funding"))
        {
            names.AddRange(["funding_rate", "funding_percentile", "funding_change"]);
        }

        if (Ready("Taker flow"))
        {
            names.AddRange(["taker_imbalance", "taker_imbalance_change", "taker_buy_ratio"]);
        }

        if (Ready("Basis"))
        {
            names.AddRange(["normalized_basis", "basis_change", "basis_percentile"]);
        }

        if (Ready("Depth"))
        {
            names.AddRange(["depth_imbalance_1pct", "depth_imbalance_change"]);
        }

        return names;
    }

    private static string WriteManifest(string dir, IReadOnlyList<string> scored, IReadOnlyList<DatasetRow> datasets)
    {
        var body = JsonSerializer.Serialize(new
        {
            MicrostructureCatalog.Version,
            MicrostructureCatalog.RepeatableRule,
            Scored = scored,
            Excluded = MicrostructureCatalog.Features.Except(scored).ToArray(),
            MicrostructureCatalog.LevelLookback,
            MicrostructureCatalog.FundingLookback,
            MicrostructureCatalog.MinimumDayCoverage,
            MinimumBarCoverage = 0.80,
            Definitions = new
            {
                Oi = "Vision metrics sum_open_interest. Last create_time inside the signal bar. oi_change is (now-prev)/prev over 1, 3, and 12 signal bars. oi_percentile is the fraction of the prior 100 fresh prints strictly below the current print. A missing print is null, not zero, and is not carried into the next bar.",
                Funding = "Settled GET /fapi/v1/fundingRate at fundingTime <= signal close. funding_change is the current settled rate minus the previous settlement. funding_percentile uses the prior 89 settlements. The next settlement is not used.",
                Taker = "Kline field 9 taker buy base volume, re-downloaded into the side cache. Ratio = buy/volume and imbalance = 2*ratio-1 only when buy > 0 and buy <= volume. Stored zeros in the old OHLCV cache are not used.",
                Basis = "normalized basis = (mark close - index close) / index close on the same closed bar. basis_change is the difference, not a ratio. basis_percentile uses the prior 100 fresh bars.",
                Depth = "Vision bookDepth notional at -1% versus +1%. Imbalance = (bid - ask) / (bid + ask). Last snapshot inside the signal bar. Spread and top-of-book size are not in this file.",
                NotUsed = "Liquidations, best bid/ask spread, REST openInterestHist, price/OI divergence, and OI/funding interaction were not scored."
            },
            Coverage = datasets.Select(row => new { row.Dataset, row.Symbol, row.Timeframe, row.Status, row.Observations, row.MissingPercent })
        }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, "feature-manifest.json"), JsonSerializer.Serialize(new { Sha256 = hash, Body = JsonDocument.Parse(body).RootElement }, JsonOptions));
        return hash;
    }

    private static List<DatasetRow> Describe(
        IReadOnlyList<CoverageRow> ohlcv,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>> funding,
        IReadOnlyDictionary<string, IReadOnlyList<VisionMetricsPoint>> oi,
        IReadOnlyDictionary<string, Dictionary<string, decimal?[]>> basis,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> candles,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> taker,
        IReadOnlyDictionary<string, IReadOnlyList<(DateTimeOffset Time, decimal Value)>> depth,
        IReadOnlyDictionary<string, int> depthMissing,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyList<string> notes)
    {
        var rows = new List<DatasetRow>();
        foreach (var row in ohlcv.Where(item => FinalFiveCatalog.Symbols.Contains(item.Symbol) && Timeframes.Contains(item.Timeframe)))
        {
            rows.Add(new DatasetRow("OHLCV", row.Symbol, row.Timeframe, "AVAILABLE", row.RequestedFrom, row.RequestedTo, row.BarCount, row.GapCount, row.DuplicateCount, 0, "yes", "artifacts/strategy-validation-cache fapi klines", "Same window as the reconstructed book."));
        }

        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            var fund = funding[symbol];
            var fundStat = Cadence(fund.Select(point => point.FundingTime).ToList(), TimeSpan.FromHours(8));
            rows.Add(new DatasetRow("Funding", symbol, "8h", fund.Count == 0 ? "DATA_UNAVAILABLE" : fundStat.Missing > 0.20 ? "PARTIAL" : "AVAILABLE", fund.FirstOrDefault()?.FundingTime, fund.LastOrDefault()?.FundingTime, fund.Count, fundStat.Gaps, fundStat.Duplicates, fundStat.Missing, "yes", "GET /fapi/v1/fundingRate settled fundingTime", "Predicted next funding rate is not used."));

            var prints = oi[symbol];
            var oiStat = Cadence(prints.Select(point => point.CreateTime).ToList(), TimeSpan.FromMinutes(5));
            var oiStatus = prints.Count == 0 ? "DATA_UNAVAILABLE" : oiStat.Missing > 0.20 ? "PARTIAL" : "AVAILABLE";
            rows.Add(new DatasetRow("Open interest", symbol, "5m", oiStatus, prints.FirstOrDefault()?.CreateTime, prints.LastOrDefault()?.CreateTime, prints.Count, oiStat.Gaps, oiStat.Duplicates, oiStat.Missing, "yes", "https://data.binance.vision/data/futures/um/daily/metrics/ sum_open_interest", "REST openInterestHist is a separate ~30 day source and is not used here."));

            var days = Math.Max(1, (windowEnd.UtcDateTime.Date - windowStart.UtcDateTime.Date).Days + 1);
            var depthMissingRatio = depthMissing[symbol] / (double)days;
            var depthRows = depth[symbol];
            var depthStatus = depthRows.Count == 0 ? "DATA_UNAVAILABLE" : depthMissingRatio > 1d - MicrostructureCatalog.MinimumDayCoverage ? "PARTIAL" : "AVAILABLE";
            rows.Add(new DatasetRow("Depth", symbol, "snapshot", depthStatus, depthRows.Count == 0 ? null : depthRows[0].Time, depthRows.Count == 0 ? null : depthRows[^1].Time, depthRows.Count, depthMissing[symbol], 0, depthMissingRatio, "yes", "Vision um/daily/bookDepth notional at ±1%", "Best bid/ask spread is not in this file. Missing days are not interpolated."));

            foreach (var timeframe in Timeframes)
            {
                var bars = candles[(symbol, timeframe)];
                var aligned = basis[symbol][timeframe];
                var present = aligned.Count(value => value is not null);
                var missing = bars.Count == 0 ? 1 : 1d - (present / (double)bars.Count);
                rows.Add(new DatasetRow("Basis", symbol, timeframe, present == 0 ? "DATA_UNAVAILABLE" : missing > 0.20 ? "PARTIAL" : "AVAILABLE", bars.FirstOrDefault()?.OpenTime, bars.LastOrDefault()?.CloseTime, present, 0, 0, missing, "yes", "markPriceKlines and indexPriceKlines close, same bar", "No forward fill. A bar without both closes is null."));

                var flow = taker[(symbol, timeframe)];
                var valid = 0;
                var matched = 0;
                var byClose = flow.GroupBy(bar => bar.CloseTime).ToDictionary(group => group.Key, group => group.Last());
                foreach (var bar in bars)
                {
                    if (!byClose.TryGetValue(bar.CloseTime, out var source))
                    {
                        continue;
                    }

                    matched++;
                    if (MicrostructureFeatures.Taker(source.Volume, source.TakerBuyVolume).Ratio is not null)
                    {
                        valid++;
                    }
                }

                var takerMissing = bars.Count == 0 ? 1 : 1d - (valid / (double)bars.Count);
                rows.Add(new DatasetRow("Taker flow", symbol, timeframe, valid == 0 ? "DATA_UNAVAILABLE" : takerMissing > 0.20 ? "PARTIAL" : "AVAILABLE", flow.FirstOrDefault()?.OpenTime, flow.LastOrDefault()?.CloseTime, valid, ResearchKlineCache.GapCount(flow, timeframe), Math.Max(0, flow.Count - byClose.Count), takerMissing, "yes", "GET /fapi/v1/klines field 9, side cache only", $"Matched {matched}/{bars.Count} OHLCV bars. Old cache zeros were not treated as flow."));
            }
        }

        rows.Add(new DatasetRow("Liquidations", "BTCUSDT ETHUSDT BNBUSDT", "n/a", "DATA_UNAVAILABLE", null, null, 0, 0, 0, 1, "n/a", "Vision um/daily/liquidationSnapshot", string.Join(" ", notes.Where(note => note.Contains("liquidation", StringComparison.OrdinalIgnoreCase)))));
        rows.Add(new DatasetRow("Spread", "BTCUSDT ETHUSDT BNBUSDT", "n/a", "DATA_UNAVAILABLE", null, null, 0, 0, 0, 1, "n/a", "not in bookDepth", "The depth archive has percentage notional, not best bid/ask."));
        return rows;
    }

    private static List<DatasetRow> ReadRestOpenInterest(string historyRoot)
    {
        var rows = new List<DatasetRow>();
        foreach (var symbol in FinalFiveCatalog.Symbols)
        {
            foreach (var period in Timeframes)
            {
                var path = Path.Combine(historyRoot, "oi", $"{symbol}_{period}.json");
                if (!File.Exists(path))
                {
                    rows.Add(new DatasetRow("Open interest REST", symbol, period, "DATA_UNAVAILABLE", null, null, 0, 0, 0, 1, "n/a", "GET /futures/data/openInterestHist", "No local REST history file."));
                    continue;
                }

                var points = JsonSerializer.Deserialize<List<OpenInterestPoint>>(File.ReadAllText(path)) ?? [];
                var ordered = points.OrderBy(point => point.Timestamp).ToList();
                var stat = ordered.Count == 0
                    ? (Gaps: 0, Duplicates: 0, Missing: 1d)
                    : Cadence(ordered.Select(point => point.Timestamp).ToList(), TimeSpan.FromMinutes(period == "1h" ? 60 : period == "15m" ? 15 : 5));
                var spanDays = ordered.Count == 0 ? 0 : (ordered[^1].Timestamp - ordered[0].Timestamp).TotalDays;
                rows.Add(new DatasetRow(
                    "Open interest REST",
                    symbol,
                    period,
                    spanDays >= 300 ? "AVAILABLE" : ordered.Count == 0 ? "DATA_UNAVAILABLE" : "PARTIAL",
                    ordered.FirstOrDefault()?.Timestamp,
                    ordered.LastOrDefault()?.Timestamp,
                    ordered.Count,
                    stat.Gaps,
                    stat.Duplicates,
                    stat.Missing,
                    "yes",
                    "GET /futures/data/openInterestHist",
                    $"On-disk span {spanDays.ToString("0.0", CultureInfo.InvariantCulture)} days. This is not the Vision series and was not used for features."));
            }
        }

        return rows;
    }

    private static async Task<string> ProbeOpenInterestAsync(HttpClient http)
    {
        var start = DateTimeOffset.Parse("2024-09-01T00:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var end = DateTimeOffset.Parse("2024-10-01T00:00:00Z", CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();
        var url = $"futures/data/openInterestHist?symbol=BTCUSDT&period=1h&startTime={start}&endTime={end}&limit=500";
        try
        {
            using var response = await http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                return $"REST OI probe status {(int)response.StatusCode}. Body was not used as history.";
            }

            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            {
                return "REST OI probe returned no rows for September 2024. Historical OI was not invented from this endpoint.";
            }

            var first = DateTimeOffset.FromUnixTimeMilliseconds(doc.RootElement[0].GetProperty("timestamp").GetInt64());
            var last = DateTimeOffset.FromUnixTimeMilliseconds(doc.RootElement[doc.RootElement.GetArrayLength() - 1].GetProperty("timestamp").GetInt64());
            return $"REST OI probe asked for 2024-09-01 to 2024-10-01 and received {doc.RootElement.GetArrayLength()} rows from {first:u} to {last:u}. That response is not the research series.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return "REST OI probe failed. The on-disk cache remains the ~29 day record. No OI was invented.";
        }
    }

    private static async Task<List<string>> ProbeLiquidationsAsync(HttpClient http)
    {
        var notes = new List<string>();
        foreach (var day in new[] { "2023-06-01", "2024-03-15", "2024-03-31", "2025-01-01" })
        {
            var url = $"data/futures/um/daily/liquidationSnapshot/BTCUSDT/BTCUSDT-liquidationSnapshot-{day}.zip";
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            notes.Add($"liquidation {day} status {(int)response.StatusCode}.");
        }

        return notes;
    }

    private static async Task<string> ProbeDepthArchiveAsync(HttpClient http)
    {
        var found = new List<string>();
        var missing = new List<string>();
        foreach (var day in new[] { "2022-01-01", "2022-06-01", "2022-12-01", "2023-01-01" })
        {
            var url = $"data/futures/um/daily/bookDepth/BTCUSDT/BTCUSDT-bookDepth-{day}.zip";
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                found.Add(day);
            }
            else
            {
                missing.Add(day + "=" + (int)response.StatusCode);
            }
        }

        return $"Depth archive probe BTCUSDT found {string.Join(", ", found)}. Absent {string.Join(", ", missing)}. Local ingest starts at the OHLCV window, not at the archive origin.";
    }

    private static async Task<(List<(DateTimeOffset Time, decimal Value)> Rows, int Missing, int Expected)> LoadDepthAsync(
        HttpClient http,
        string root,
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var folder = Path.Combine(root, "book-depth", symbol);
        Directory.CreateDirectory(folder);
        var days = new List<DateTime>();
        for (var day = start.UtcDateTime.Date; day <= end.UtcDateTime.Date; day = day.AddDays(1))
        {
            days.Add(day);
        }

        var gate = new SemaphoreSlim(6);
        await Task.WhenAll(days.Select(async day =>
        {
            await gate.WaitAsync();
            try
            {
                var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var path = Path.Combine(folder, $"{symbol}-bookDepth-{stamp}.zip");
                if (File.Exists(path) && new FileInfo(path).Length > 32)
                {
                    return;
                }

                var url = $"data/futures/um/daily/bookDepth/{symbol}/{symbol}-bookDepth-{stamp}.zip";
                using var response = await http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                if (bytes.Length < 32)
                {
                    return;
                }

                var tmp = path + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes);
                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                gate.Release();
            }
        }));
        var missing = days.Count(day =>
        {
            var path = Path.Combine(folder, $"{symbol}-bookDepth-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.zip");
            return !File.Exists(path) || new FileInfo(path).Length <= 32;
        });

        var reduced = Path.Combine(root, "book-depth", $"{symbol}-imbalance-1pct.csv");
        if (File.Exists(reduced))
        {
            var cached = ReadDepthCsv(reduced);
            if (cached.Count > 0 && cached[0].Time <= start.AddDays(2) && cached[^1].Time >= end.AddDays(-2))
            {
                return (cached, missing, days.Count);
            }
        }

        var rows = new List<(DateTimeOffset Time, decimal Value)>();
        foreach (var day in days)
        {
            var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var path = Path.Combine(folder, $"{symbol}-bookDepth-{stamp}.zip");
            if (!File.Exists(path))
            {
                continue;
            }

            rows.AddRange(ReadDepthZip(path));
        }

        var deduped = rows.GroupBy(row => row.Time).Select(group => group.Last()).OrderBy(row => row.Time).ToList();
        var csv = new StringBuilder();
        foreach (var row in deduped)
        {
            csv.Append(row.Time.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
            csv.Append(',');
            csv.AppendLine(row.Value.ToString(CultureInfo.InvariantCulture));
        }

        await File.WriteAllTextAsync(reduced, csv.ToString());
        return (deduped, missing, days.Count);
    }

    private static List<(DateTimeOffset Time, decimal Value)> ReadDepthCsv(string path)
    {
        var rows = new List<(DateTimeOffset Time, decimal Value)>();
        foreach (var line in File.ReadLines(path))
        {
            var split = line.IndexOf(',');
            if (split <= 0)
            {
                continue;
            }

            if (!long.TryParse(line[..split], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            {
                continue;
            }

            if (!decimal.TryParse(line[(split + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                continue;
            }

            rows.Add((DateTimeOffset.FromUnixTimeMilliseconds(ms), value));
        }

        return rows;
    }

    private static List<(DateTimeOffset Time, decimal Value)> ReadDepthZip(string path)
    {
        var rows = new List<(DateTimeOffset Time, decimal Value)>();
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.Entries.FirstOrDefault(item => item.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return rows;
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        _ = reader.ReadLine();
        string? line;
        DateTimeOffset? stamp = null;
        decimal? bid = null;
        decimal? ask = null;
        while ((line = reader.ReadLine()) is not null)
        {
            var parts = line.Split(',');
            if (parts.Length < 4)
            {
                continue;
            }

            if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var time))
            {
                continue;
            }

            var at = new DateTimeOffset(time, TimeSpan.Zero);
            if (stamp is { } prior && at != prior)
            {
                Emit(rows, prior, bid, ask);
                bid = null;
                ask = null;
            }

            stamp = at;
            if (!decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percentage)
                || !decimal.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var notional))
            {
                continue;
            }

            if (percentage == -1m)
            {
                bid = notional;
            }
            else if (percentage == 1m)
            {
                ask = notional;
            }
        }

        if (stamp is { } last)
        {
            Emit(rows, last, bid, ask);
        }

        return rows;
    }

    private static void Emit(List<(DateTimeOffset Time, decimal Value)> rows, DateTimeOffset time, decimal? bid, decimal? ask)
    {
        if (bid is not { } bidNotional || ask is not { } askNotional)
        {
            return;
        }

        var total = bidNotional + askNotional;
        if (total == 0m)
        {
            return;
        }

        rows.Add((time, (bidNotional - askNotional) / total));
    }

    private static (int Gaps, int Duplicates, double Missing) Cadence(IReadOnlyList<DateTimeOffset> times, TimeSpan step)
    {
        if (times.Count == 0)
        {
            return (0, 0, 1);
        }

        var ordered = times.OrderBy(time => time).ToList();
        var distinct = ordered.Distinct().Count();
        var gaps = 0;
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i] - ordered[i - 1] > step + TimeSpan.FromMinutes(1))
            {
                gaps++;
            }
        }

        var expected = (int)((ordered[^1] - ordered[0]).Ticks / step.Ticks) + 1;
        var missing = expected <= 0 ? 0 : Math.Max(0, 1d - (distinct / (double)expected));
        return (gaps, ordered.Count - distinct, missing);
    }

    private static void WriteDataAudit(string root, IReadOnlyList<DatasetRow> rows, IReadOnlyList<string> notes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Futures microstructure data audit");
        sb.AppendLine();
        sb.AppendLine("This is a coverage audit. It does not create a strategy and it does not call any series an edge.");
        sb.AppendLine();
        sb.AppendLine("The comparison window is the same OHLCV book used for the 11,125 reconstructed trades: BTCUSDT, ETHUSDT, and BNBUSDT, 5m and 15m for about one year, 1h for about two years.");
        sb.AppendLine();
        sb.AppendLine("## Coverage");
        sb.AppendLine();
        sb.AppendLine("| Dataset | Coin | Timeframe | Status | From | To | Records | Gaps | Duplicates | Missing | Causal | Source | Limitation |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | --- | --- | --- |");
        foreach (var row in rows)
        {
            sb.AppendLine($"| {row.Dataset} | {row.Symbol} | {row.Timeframe} | {row.Status} | {When(row.From)} | {When(row.To)} | {row.Observations} | {row.Gaps} | {row.Duplicates} | {row.MissingPercent.ToString("0.0%", CultureInfo.InvariantCulture)} | {row.Causal} | {row.Source} | {row.Limitation} |");
        }

        sb.AppendLine();
        sb.AppendLine("## A. Data available");
        sb.AppendLine();
        foreach (var name in rows.Select(row => row.Dataset).Distinct())
        {
            var set = rows.Where(row => row.Dataset == name).ToList();
            if (set.All(row => row.Status == "AVAILABLE"))
            {
                sb.AppendLine($"- {name}.");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## B. Data partially available");
        sb.AppendLine();
        foreach (var name in rows.Select(row => row.Dataset).Distinct())
        {
            var set = rows.Where(row => row.Dataset == name).ToList();
            if (set.Any(row => row.Status == "PARTIAL") && set.Any(row => row.Status != "DATA_UNAVAILABLE"))
            {
                sb.AppendLine($"- {name}: {string.Join("; ", set.Where(row => row.Status != "AVAILABLE").Select(row => row.Symbol + " " + row.Timeframe + " " + row.Status))}.");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## C. Data unavailable");
        sb.AppendLine();
        foreach (var name in rows.Select(row => row.Dataset).Distinct())
        {
            var set = rows.Where(row => row.Dataset == name).ToList();
            if (set.All(row => row.Status == "DATA_UNAVAILABLE"))
            {
                sb.AppendLine($"- {name}: {set[0].Limitation}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Probes");
        sb.AppendLine();
        foreach (var note in notes)
        {
            sb.AppendLine($"- {note}");
        }

        sb.AppendLine();
        sb.AppendLine("Vision metrics also contain count and sum long/short ratios and `sum_taker_long_short_vol_ratio`. Those columns were not added to the feature list. Kline taker-buy is the taker definition in this phase.");
        sb.AppendLine();
        sb.AppendLine("No microstructure value was interpolated. A current order-book snapshot was not written back into history.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.");
        File.WriteAllText(Path.Combine(root, "docs", "FUTURES_MICROSTRUCTURE_DATA_AUDIT.md"), sb.ToString());
    }

    private static void WriteSignalAudit(string root, string hash, IReadOnlyList<Row> rows, IReadOnlyList<FeatureScore> scores, IReadOnlyList<DatasetRow> datasets, IReadOnlyList<string> scored)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Futures microstructure signal audit");
        sb.AppendLine();
        sb.AppendLine($"Feature manifest SHA-256 `{hash}`.");
        sb.AppendLine();
        sb.AppendLine("This audit joins causal futures data to the existing reconstructed book. It does not create a strategy, change a parameter, or call any relationship an edge.");
        sb.AppendLine();
        sb.AppendLine("## 1. Dataset");
        sb.AppendLine();
        sb.AppendLine($"Trades: {rows.Count}. Winners: {rows.Count(row => row.Win)}. Losers: {rows.Count(row => !row.Win)}.");
        if (rows.Count != 11125)
        {
            sb.AppendLine("This reconstruction is not the 11,125-trade book. Feature labels below are not comparable to the earlier audits.");
        }
        sb.AppendLine("Same arms as the signal-quality audit: Frozen Five at 15m, Final Five primary timeframes, Phase 8 baselines at 5m, BTCUSDT, ETHUSDT, BNBUSDT, Model B. Near-miss was not added. Entry and exit rules were not changed.");
        sb.AppendLine($"Gross expectancy {Money(rows.Count == 0 ? 0 : rows.Average(row => row.Gross))}. Net expectancy {Money(rows.Count == 0 ? 0 : rows.Average(row => row.Net))}.");
        sb.AppendLine();
        sb.AppendLine("## 2. Feature definitions");
        sb.AppendLine();
        sb.AppendLine(MicrostructureCatalog.RepeatableRule);
        sb.AppendLine();
        sb.AppendLine($"Scored features ({scored.Count}): {string.Join(", ", scored)}.");
        var excluded = MicrostructureCatalog.Features.Except(scored).ToList();
        sb.AppendLine(excluded.Count == 0
            ? "Every catalog feature had coverage on every coin and timeframe before aggregation."
            : "Excluded before aggregation because coverage was below the pre-registered bar: " + string.Join(", ", excluded) + ".");
        sb.AppendLine("Open-interest change is a fractional change. Funding change, basis change, taker-imbalance change, and depth-imbalance change are differences. Percentiles use only prior fresh observations.");
        sb.AppendLine();
        sb.AppendLine("## 3. Winner versus loser distributions");
        sb.AppendLine();
        sb.AppendLine("| Feature | n | Winner p25 | Winner p50 | Winner p75 | Loser p25 | Loser p50 | Loser p75 | Effect | Gross | Net | Win rate | PF |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var score in scores)
        {
            var sample = rows.Where(row => row.Features[score.Name] is not null).ToList();
            sb.AppendLine($"| {score.Name} | {score.N} | {Num(score.WinnerP25)} | {Num(score.WinnerP50)} | {Num(score.WinnerP75)} | {Num(score.LoserP25)} | {Num(score.LoserP50)} | {Num(score.LoserP75)} | {Num(score.Effect)} | {Money(Mean(sample, row => row.Gross))} | {Money(Mean(sample, row => row.Net))} | {Share(sample.Count(row => row.Win), sample.Count)} | {Pf(sample)} |");
        }

        sb.AppendLine();
        sb.AppendLine("Effect is the winner median minus the loser median, divided by the interquartile range. Profit factor is shown when the sample has at least 200 trades and both gains and losses.");
        sb.AppendLine();
        sb.AppendLine("## 4. Feature-by-feature results");
        sb.AppendLine();
        sb.AppendLine("| Feature | IS rho | VAL rho | OOS rho | IS n | VAL n | OOS n | IS high-low net | VAL high-low net | OOS high-low net | Blocks | Families | Symbols | Label |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |");
        foreach (var score in scores)
        {
            sb.AppendLine($"| {score.Name} | {Num(score.IsRho)} | {Num(score.ValRho)} | {Num(score.OosRho)} | {score.IsN} | {score.ValN} | {score.OosN} | {Money(score.IsGap)} | {Money(score.ValGap)} | {Money(score.OosGap)} | {score.Blocks} | {score.Families} | {score.Symbols} | {score.Label} |");
        }

        sb.AppendLine();
        sb.AppendLine("High-low net uses tertile cuts taken from IS feature values only. A zero gap means a bucket had fewer than 30 trades.");
        sb.AppendLine();
        sb.AppendLine("## 5. IS, validation, OOS");
        sb.AppendLine();
        sb.AppendLine("| Feature | Phase | n | Winner p50 | Loser p50 | Rho | Net |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var score in scores)
        {
            foreach (var phase in new[] { "IS", "VALIDATION", "OOS" })
            {
                var sample = rows.Where(row => row.Phase == phase && row.Features[score.Name] is not null).ToList();
                var winners = Sorted(sample.Where(row => row.Win).Select(row => row.Features[score.Name]!.Value));
                var losers = Sorted(sample.Where(row => !row.Win).Select(row => row.Features[score.Name]!.Value));
                var rho = phase == "IS" ? score.IsRho : phase == "VALIDATION" ? score.ValRho : score.OosRho;
                sb.AppendLine($"| {score.Name} | {phase} | {sample.Count} | {Num(Median(winners))} | {Num(Median(losers))} | {Num(rho)} | {Money(Mean(sample, row => row.Net))} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 6. Chronological stability");
        sb.AppendLine();
        sb.AppendLine("Blocks count how many of the four equal index quarters have at least 100 trades and absolute rho of at least 0.05 with the reference sign.");
        sb.AppendLine();
        sb.AppendLine("| Feature | Block 1 rho | Block 2 rho | Block 3 rho | Block 4 rho |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var score in scores)
        {
            sb.AppendLine($"| {score.Name} | {Num(score.BlockRho[0])} | {Num(score.BlockRho[1])} | {Num(score.BlockRho[2])} | {Num(score.BlockRho[3])} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Strategy-family stability");
        sb.AppendLine();
        SliceTable(sb, rows, scores, rows.Select(row => row.Group).Distinct().OrderBy(name => name).ToList(), row => row.Group);
        sb.AppendLine();
        sb.AppendLine("## 8. Symbol stability");
        sb.AppendLine();
        SliceTable(sb, rows, scores, FinalFiveCatalog.Symbols.ToList(), row => row.Symbol);
        sb.AppendLine();
        sb.AppendLine("## 9. Long and short stability");
        sb.AppendLine();
        SliceTable(sb, rows, scores, ["LONG", "SHORT"], row => row.Side);
        sb.AppendLine();
        sb.AppendLine("## 10. BTC context");
        sb.AppendLine();
        sb.AppendLine("No BTC-context interaction was built. The BTCUSDT rows in the symbol table are the BTC book itself, not a lead-lag feature.");
        sb.AppendLine();
        sb.AppendLine("## 11. Simultaneous-signal analysis");
        sb.AppendLine();
        sb.AppendLine("Simultaneous signals were not recomputed. This phase does not combine microstructure with the earlier signal-count features.");
        sb.AppendLine();
        sb.AppendLine("Timeframe slices:");
        sb.AppendLine();
        SliceTable(sb, rows, scores, Timeframes.ToList(), row => row.Timeframe);
        sb.AppendLine();
        sb.AppendLine("## 12. Strongest recurring relationships");
        sb.AppendLine();
        var notable = scores.Where(score => score.Label != "NO_EVIDENCE").ToList();
        sb.AppendLine(notable.Count == 0 ? "No scored feature received a label other than NO_EVIDENCE." : string.Join(Environment.NewLine, notable.Select(score => $"- {score.Name}: {score.Label}. IS rho {Num(score.IsRho)}, validation rho {Num(score.ValRho)}, OOS rho {Num(score.OosRho)}.")));
        sb.AppendLine();
        sb.AppendLine("## 13. Unstable relationships");
        sb.AppendLine();
        var unstable = scores.Where(score => score.Label is "UNSTABLE" or "OOS_ONLY" or "FAMILY_SPECIFIC" or "SYMBOL_SPECIFIC").ToList();
        sb.AppendLine(unstable.Count == 0 ? "No scored feature was large in one slice and weak in another under the pre-registered labels." : string.Join(", ", unstable.Select(score => score.Name + " (" + score.Label + ")")) + ".");
        sb.AppendLine();
        sb.AppendLine("## 14. Data limitations");
        sb.AppendLine();
        foreach (var row in datasets.Where(row => row.Status != "AVAILABLE"))
        {
            sb.AppendLine($"- {row.Dataset} {row.Symbol} {row.Timeframe}: {row.Status}. {row.Limitation}");
        }

        sb.AppendLine("- Price versus open-interest divergence and open-interest versus funding were not constructed.");
        sb.AppendLine("- `sum_taker_long_short_vol_ratio` is present in the Vision metrics files and was not scored.");
        sb.AppendLine("- Features were not combined with EMA slope or VWAP slope.");
        sb.AppendLine();
        sb.AppendLine("## 15. Final conclusion");
        sb.AppendLine();
        foreach (var label in new[] { "REPEATABLE", "FAMILY_SPECIFIC", "SYMBOL_SPECIFIC", "OOS_ONLY", "UNSTABLE", "NO_EVIDENCE" })
        {
            var names = scores.Where(score => score.Label == label).Select(score => score.Name).ToList();
            sb.AppendLine($"- {label}: {(names.Count == 0 ? "none" : string.Join(", ", names))}.");
        }

        sb.AppendLine();
        sb.AppendLine("## A. Data available");
        sb.AppendLine();
        sb.AppendLine(string.Join(", ", datasets.Where(row => row.Status == "AVAILABLE").Select(row => row.Dataset).Distinct()) + ".");
        sb.AppendLine();
        sb.AppendLine("## B. Data partially available");
        sb.AppendLine();
        var partial = datasets.Where(row => row.Status == "PARTIAL").Select(row => row.Dataset).Distinct().ToList();
        sb.AppendLine(partial.Count == 0 ? "None." : string.Join(", ", partial) + ".");
        sb.AppendLine();
        sb.AppendLine("## C. Data unavailable");
        sb.AppendLine();
        var unavailable = datasets.Where(row => row.Status == "DATA_UNAVAILABLE").Select(row => row.Dataset).Distinct().ToList();
        sb.AppendLine(unavailable.Count == 0 ? "None." : string.Join(", ", unavailable) + ".");
        sb.AppendLine();
        sb.AppendLine("## D. Features with repeatable relationships");
        sb.AppendLine();
        var repeatable = scores.Where(score => score.Label == "REPEATABLE").Select(score => score.Name).ToList();
        sb.AppendLine(repeatable.Count == 0 ? "None. No scored feature met the pre-registered REPEATABLE bar." : string.Join(", ", repeatable) + ". The label is not an edge.");
        sb.AppendLine();
        sb.AppendLine("## E. Features without evidence");
        sb.AppendLine();
        var quiet = scores.Where(score => score.Label == "NO_EVIDENCE").Select(score => score.Name).ToList();
        sb.AppendLine(quiet.Count == 0 ? "None." : string.Join(", ", quiet) + ".");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.");
        File.WriteAllText(Path.Combine(root, "docs", "FUTURES_MICROSTRUCTURE_SIGNAL_AUDIT.md"), sb.ToString());
    }

    private static void SliceTable(StringBuilder sb, IReadOnlyList<Row> rows, IReadOnlyList<FeatureScore> scores, IReadOnlyList<string> keys, Func<Row, string> key)
    {
        sb.AppendLine("| Feature | Slice | n | Rho | Winner p50 | Loser p50 | Net |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var score in scores)
        {
            foreach (var name in keys)
            {
                var sample = rows.Where(row => key(row) == name && row.Features[score.Name] is not null).ToList();
                var rho = SignalQualityMath.Spearman(sample.Select(row => row.Features[score.Name]!.Value).ToList(), sample.Select(row => row.Net).ToList());
                var winners = Sorted(sample.Where(row => row.Win).Select(row => row.Features[score.Name]!.Value));
                var losers = Sorted(sample.Where(row => !row.Win).Select(row => row.Features[score.Name]!.Value));
                sb.AppendLine($"| {score.Name} | {name} | {sample.Count} | {Num(rho)} | {Num(Median(winners))} | {Num(Median(losers))} | {Money(Mean(sample, row => row.Net))} |");
            }
        }
    }

    private static FeatureScore Score(string name, IReadOnlyList<Row> rows)
    {
        var ready = rows.Where(row => row.Features.TryGetValue(name, out var value) && value is not null).ToList();
        decimal? Rho(IReadOnlyList<Row> sample) => SignalQualityMath.Spearman(sample.Select(row => row.Features[name]!.Value).ToList(), sample.Select(row => row.Net).ToList());
        decimal? RhoPhase(string phase) => Rho(ready.Where(row => row.Phase == phase).ToList());
        int Count(string phase) => ready.Count(row => row.Phase == phase);
        var isRho = RhoPhase("IS");
        var valRho = RhoPhase("VALIDATION");
        var oosRho = RhoPhase("OOS");
        var blockRho = Enumerable.Range(1, 4).Select(block => Rho(ready.Where(row => row.Block == block).ToList())).ToArray();
        var sign = isRho is { } seed && Math.Abs(seed) >= SignalQualityCatalog.MinimumAbsRho ? Math.Sign(seed) : oosRho is { } alt && Math.Abs(alt) >= SignalQualityCatalog.MinimumAbsRho ? Math.Sign(alt) : 0;
        var blocks = sign == 0 ? 0 : Enumerable.Range(1, 4).Count(block => Agrees(ready.Where(row => row.Block == block).ToList(), name, sign));
        var families = sign == 0 ? 0 : ready.GroupBy(row => row.Group).Count(group => Agrees(group.ToList(), name, sign));
        var symbols = sign == 0 ? 0 : FinalFiveCatalog.Symbols.Count(symbol => Agrees(ready.Where(row => row.Symbol == symbol).ToList(), name, sign));
        var winners = Sorted(ready.Where(row => row.Win).Select(row => row.Features[name]!.Value));
        var losers = Sorted(ready.Where(row => !row.Win).Select(row => row.Features[name]!.Value));
        var all = Sorted(ready.Select(row => row.Features[name]!.Value));
        var iqr = SignalQualityMath.Quantile(all, 0.75m) - SignalQualityMath.Quantile(all, 0.25m);
        var effect = winners.Count == 0 || losers.Count == 0 || iqr == 0m ? 0m : (SignalQualityMath.Quantile(winners, 0.5m) - SignalQualityMath.Quantile(losers, 0.5m)) / iqr;
        var cuts = SignalQualityMath.TertileCuts(ready.Where(row => row.Phase == "IS").Select(row => row.Features[name]!.Value).ToList());
        return new FeatureScore(
            name, ready.Count,
            QuantileOrNull(winners, 0.25m), QuantileOrNull(winners, 0.5m), QuantileOrNull(winners, 0.75m),
            QuantileOrNull(losers, 0.25m), QuantileOrNull(losers, 0.5m), QuantileOrNull(losers, 0.75m),
            effect, isRho, valRho, oosRho, Count("IS"), Count("VALIDATION"), Count("OOS"),
            Gap(ready, name, "IS", cuts), Gap(ready, name, "VALIDATION", cuts), Gap(ready, name, "OOS", cuts),
            blocks, families, symbols, blockRho,
            SignalQualityFeatures.Classify(isRho, Count("IS"), valRho, Count("VALIDATION"), oosRho, Count("OOS"), blocks, families, symbols));
    }

    private static bool Agrees(IReadOnlyList<Row> rows, string name, int sign)
    {
        var rho = SignalQualityMath.Spearman(rows.Select(row => row.Features[name]!.Value).ToList(), rows.Select(row => row.Net).ToList());
        return rows.Count >= SignalQualityCatalog.MinimumSliceTrades && rho is { } value && Math.Abs(value) >= SignalQualityCatalog.MinimumAbsRho && Math.Sign(value) == sign;
    }

    private static decimal Gap(IReadOnlyList<Row> rows, string name, string phase, (decimal Low, decimal High) cuts)
    {
        var sample = rows.Where(row => row.Phase == phase).ToList();
        var low = sample.Where(row => row.Features[name]!.Value <= cuts.Low).ToList();
        var high = sample.Where(row => row.Features[name]!.Value >= cuts.High).ToList();
        if (low.Count < SignalQualityCatalog.MinimumBucketTrades || high.Count < SignalQualityCatalog.MinimumBucketTrades)
        {
            return 0m;
        }

        return high.Average(row => row.Net) - low.Average(row => row.Net);
    }

    private static async Task<IReadOnlyList<CoverageRow>> ReadCoverageAsync(string root)
    {
        var path = Path.Combine(root, "artifacts", "strategy-research", "price-action-alpha", "coverage-post.json");
        return JsonSerializer.Deserialize<CoverageFile>(await File.ReadAllTextAsync(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Coverage
            ?? throw new InvalidOperationException("Coverage artifact missing.");
    }

    private static async Task<IReadOnlyList<MarketCandle>> LoadOne(string cacheDir, string root, string symbol, string timeframe)
    {
        var coverage = await ReadCoverageAsync(root);
        var range = coverage.First(row => row.Symbol == symbol && row.Timeframe == timeframe);
        var bars = await ResearchKlineCache.ReadCachedAsync(cacheDir, symbol, timeframe);
        return bars.Where(bar => bar.OpenTime >= range.RequestedFrom && bar.CloseTime <= range.RequestedTo && bar.IsClosed).OrderBy(bar => bar.OpenTime).ToList();
    }

    private static IReadOnlyList<SignalType> SignalsFor(Arm arm, Dictionary<string, CausalIndicatorCache> caches, IReadOnlyList<ContextualSignalRow> contextual)
    {
        if (arm.Kind == ArmKind.Frozen)
        {
            return [];
        }

        if (arm.Kind == ArmKind.FinalFive)
        {
            return FinalFiveSignals.Build(arm.Id + "|" + arm.Timeframe, caches[arm.Timeframe], caches["1h"]);
        }

        var id = arm.Id switch
        {
            "CPA-SWEEP" => "CPA-SWEEP|BASELINE|5m",
            "CPA-PULLBACK" => "CPA-PULLBACK|BASELINE|5m",
            "CPA-WM" => "CPA-WM|BASELINE|5m",
            "CPA-COMPRESSION" => "CPA-COMPRESSION|CONTINUATION|5m",
            "CPA-MTF" => "CPA-MTF|BASELINE|5m",
            "CPA-FAILED_BREAKOUT" => "CPA-FAILED_BREAKOUT|BASELINE|5m",
            _ => arm.Id
        };
        return contextual.First(row => row.CandidateId == id).Signals;
    }

    private static List<ReplayTrade> Replay(Arm arm, string symbol, IReadOnlyList<MarketCandle> candles, IReadOnlyList<SignalType> signals)
    {
        if (candles.Count < 200)
        {
            return [];
        }

        var created = DateTimeOffset.Parse("2026-09-23T00:00:00Z", CultureInfo.InvariantCulture);
        var template = arm.Kind == ArmKind.Frozen ? arm.Id : StrategyTemplateKeys.EmaRsiTrend;
        var candidate = new ResearchCandidate(
            arm.Id + "|" + arm.Timeframe + "|" + symbol, template, 1, "Unmodified existing rule.",
            ResearchKinds.ParentFilter, template, "", [], "existing", "Model B",
            new ResearchFilters(), new ResearchNativeParams(), [arm.Timeframe], ["LONG", "SHORT"], [],
            created, "microstructure audit", ResearchStatuses.Researching);
        var definition = ResearchRunner.DefinitionFor(candidate, arm.Timeframe);
        IStrategyEngine engine = arm.Kind == ArmKind.Frozen ? new ResearchStrategyEngine(candidate) : new PrecomputedResearchSignalEngine(signals);
        var settings = StrategyValidation.LowIsolatedRisk(candles[0].OpenTime, candles[^1].CloseTime);
        var warmup = arm.Kind == ArmKind.Frozen ? StrategyValidation.WarmupBars(definition) : 0;
        return new BacktestReplay(engine).Run(definition, candles, settings, new CausalIndicatorCache(candles), warmup, candles.Count).Trades.ToList();
    }

    private static List<decimal> Sorted(IEnumerable<decimal> values) => values.OrderBy(value => value).ToList();

    private static decimal? Median(IReadOnlyList<decimal> sorted) => sorted.Count == 0 ? null : SignalQualityMath.Quantile(sorted, 0.5m);

    private static decimal? QuantileOrNull(IReadOnlyList<decimal> sorted, decimal p) => sorted.Count == 0 ? null : SignalQualityMath.Quantile(sorted, p);

    private static decimal Mean(IReadOnlyList<Row> rows, Func<Row, decimal> value) => rows.Count == 0 ? 0m : rows.Average(value);

    private static string Pf(IReadOnlyList<Row> rows)
    {
        if (rows.Count < SignalQualityCatalog.MinimumWindowTrades)
        {
            return "n/a";
        }

        var gain = rows.Where(row => row.Net > 0m).Sum(row => row.Net);
        var loss = rows.Where(row => row.Net < 0m).Sum(row => row.Net);
        return loss == 0m ? "n/a" : (gain / Math.Abs(loss)).ToString("0.000", CultureInfo.InvariantCulture);
    }

    private static string Share(int n, int d) => d == 0 ? "n/a" : (n / (decimal)d).ToString("0.0%", CultureInfo.InvariantCulture);

    private static string Num(decimal? value) => value is { } number ? number.ToString("0.000", CultureInfo.InvariantCulture) : "n/a";

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string When(DateTimeOffset? value) => value is { } time && time != default ? time.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "n/a";

    private sealed record Arm(string Id, string Group, string Timeframe, ArmKind Kind);

    private enum ArmKind { Frozen, FinalFive, Contextual }

    private sealed class Row
    {
        public Row(string group, string strategy, string symbol, string timeframe, string side, string phase, int block, decimal gross, decimal net, bool win, Dictionary<string, decimal?> features)
        {
            Group = group;
            Strategy = strategy;
            Symbol = symbol;
            Timeframe = timeframe;
            Side = side;
            Phase = phase;
            Block = block;
            Gross = gross;
            Net = net;
            Win = win;
            Features = features;
        }

        public string Group { get; }
        public string Strategy { get; }
        public string Symbol { get; }
        public string Timeframe { get; }
        public string Side { get; }
        public string Phase { get; }
        public int Block { get; }
        public decimal Gross { get; }
        public decimal Net { get; }
        public bool Win { get; }
        public Dictionary<string, decimal?> Features { get; }
    }

    private sealed record FeatureScore(
        string Name, int N,
        decimal? WinnerP25, decimal? WinnerP50, decimal? WinnerP75,
        decimal? LoserP25, decimal? LoserP50, decimal? LoserP75,
        decimal Effect, decimal? IsRho, decimal? ValRho, decimal? OosRho,
        int IsN, int ValN, int OosN, decimal IsGap, decimal ValGap, decimal OosGap,
        int Blocks, int Families, int Symbols, decimal?[] BlockRho, string Label);

    private sealed record DatasetRow(
        string Dataset, string Symbol, string Timeframe, string Status,
        DateTimeOffset? From, DateTimeOffset? To, int Observations, int Gaps, int Duplicates,
        double MissingPercent, string Causal, string Source, string Limitation);

    private sealed class CoverageFile
    {
        public List<CoverageRow> Coverage { get; set; } = [];
    }

    private sealed class CoverageRow
    {
        public string Symbol { get; set; } = "";
        public string Timeframe { get; set; } = "";
        public DateTimeOffset RequestedFrom { get; set; }
        public DateTimeOffset RequestedTo { get; set; }
        public int BarCount { get; set; }
        public int GapCount { get; set; }
        public int DuplicateCount { get; set; }
    }
}
