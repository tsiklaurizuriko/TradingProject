using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using TradingPlatform.Backtesting;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.StrategyResearch;

public static class ProductionNewUniverse
{
    private static readonly string[] Timeframes = ["1h", "15m", "5m"];
    private static readonly string[] PriceStrategies =
    [
        StrategyTemplateKeys.TrendFollowingNew,
        StrategyTemplateKeys.BreakoutVolumeNew,
        StrategyTemplateKeys.MeanReversionRsiBollingerNew
    ];

    public static int Run(string root)
    {
        var git = GitRevision(root);
        var stamp = DateTimeOffset.UtcNow;
        var outDir = Path.Combine(root, "artifacts", "strategy-research", "production-new", stamp.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(outDir);
        var cache = Path.Combine(root, "artifacts", "strategy-validation-cache");
        var history = Path.Combine(root, "artifacts", "data", "futures-history-v1");
        var settings = ProductionNewBacktest.Settings();
        var symbols = Directory.Exists(cache)
            ? Directory.GetFiles(cache, "*_1h.json").Select(path => Path.GetFileName(path)[..^"_1h.json".Length]).OrderBy(name => name, StringComparer.Ordinal).ToArray()
            : [];
        var majors = symbols.Where(symbol => File.Exists(Path.Combine(history, "basis", symbol + "_1h.json"))).ToArray();
        Console.WriteLine($"production-new universe git={git} symbols={symbols.Length} basisSymbols={majors.Length}");
        Console.WriteLine($"Costs: fee {settings.FeePercent.ToString(CultureInfo.InvariantCulture)}% per side, slippage {settings.SlippagePercent.ToString(CultureInfo.InvariantCulture)}% per side, risk {settings.RiskPercent.ToString(CultureInfo.InvariantCulture)}%, leverage {settings.Leverage.ToString(CultureInfo.InvariantCulture)}.");
        Console.WriteLine("Parameters are the documented defaults. They were not selected from the final 20%.");
        Console.WriteLine("A single-series PASS is not VALIDATED. Portfolio output is DIAGNOSTIC_SHARED_CAPITAL.");

        var inventory = new List<object>();
        var summaries = new List<object>();
        var portfolios = new List<object>();
        foreach (var timeframe in Timeframes)
        {
            Console.WriteLine($"--- {timeframe} ---");
            var acc = PriceStrategies.ToDictionary(id => id, _ => new Totals(), StringComparer.Ordinal);
            acc[StrategyTemplateKeys.FundingBasisNew] = new Totals();
            acc[StrategyTemplateKeys.MarketRegimeNew] = new Totals();
            acc[StrategyTemplateKeys.MomentumRelativeStrengthNew] = new Totals();
            var book = new List<ProductionNewBookTrade>();
            var coverage = new ConcurrentBag<(string Symbol, int Bars, DateTimeOffset Start, DateTimeOffset End, int Gaps, int Duplicates, int Bad, bool Funding, bool Basis)>();
            var wfCounts = new ConcurrentDictionary<string, (int Windows, int WithTrades, int NonNegative)>(StringComparer.Ordinal);
            var symbolFile = Path.Combine(outDir, $"symbols-{timeframe}.jsonl");
            var wfFile = Path.Combine(outDir, $"walkforward-{timeframe}.jsonl");
            using var symbolWriter = new StreamWriter(symbolFile) { AutoFlush = true };
            using var wfWriter = new StreamWriter(wfFile) { AutoFlush = true };
            var writeLock = new object();
            var done = 0;
            Parallel.ForEach(symbols, new ParallelOptions { MaxDegreeOfParallelism = 3 }, symbol =>
            {
                var path = Path.Combine(cache, $"{symbol}_{timeframe}.json");
                if (!File.Exists(path))
                {
                    return;
                }

                List<MarketCandle> candles;
                try
                {
                    candles = Load(path);
                }
                catch (Exception ex)
                {
                    lock (writeLock)
                    {
                        symbolWriter.WriteLine(JsonSerializer.Serialize(new { symbol, timeframe, error = ex.Message }));
                    }

                    return;
                }

                if (candles.Count < 80)
                {
                    coverage.Add((symbol, candles.Count, DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0, 0, false, false));
                    return;
                }

                var (duplicates, bad, gaps) = Quality(candles, timeframe);
                var (a, b) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
                var inSampleEnd = candles[Math.Clamp(a, 0, candles.Count - 1)].OpenTime;
                var outOfSampleStart = candles[Math.Clamp(b, 0, candles.Count - 1)].OpenTime;
                var fundingPath = Path.Combine(history, "funding", symbol + ".json");
                var basisPath = Path.Combine(history, "basis", $"{symbol}_{timeframe}.json");
                IReadOnlyList<ReplayFundingSettlement>? settlements = null;
                StrategyFuturesSeries? futures = null;
                var fundingLoaded = false;
                var basisLoaded = false;
                try
                {
                    if (File.Exists(fundingPath))
                    {
                        var points = FuturesHistoryCache.ReadFunding(history, symbol, candles[0].OpenTime, candles[^1].CloseTime);
                        settlements = points.Select(point => new ReplayFundingSettlement(point.FundingTime, point.FundingRate)).ToList();
                        fundingLoaded = points.Count > 0;
                        if (File.Exists(basisPath))
                        {
                            var basis = FuturesHistoryCache.ReadBasis(history, symbol, timeframe, candles[0].OpenTime, candles[^1].CloseTime);
                            futures = FuturesHistoryCache.Align(candles, points, null, null, null, basis);
                            basisLoaded = basis.Count > 0 && futures.NormalizedBasis?.Any(value => value is not null) == true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    lock (writeLock)
                    {
                        symbolWriter.WriteLine(JsonSerializer.Serialize(new { symbol, timeframe, fundingError = ex.Message }));
                    }
                }

                coverage.Add((symbol, candles.Count, candles[0].OpenTime, candles[^1].CloseTime, gaps, duplicates, bad, fundingLoaded, basisLoaded));
                var engine = new StrategyEngine();
                var replay = new BacktestReplay(engine);
                var strategies = PriceStrategies.ToList();
                if (basisLoaded)
                {
                    strategies.Add(StrategyTemplateKeys.FundingBasisNew);
                }

                if (string.Equals(symbol, "BTCUSDT", StringComparison.Ordinal))
                {
                    strategies.Add(StrategyTemplateKeys.MarketRegimeNew);
                }

                foreach (var id in strategies)
                {
                    var definition = StrategyValidation.Definition(id, timeframe);
                    definition.Symbol = symbol;
                    var result = replay.Run(definition, candles, settings, futures: futures, fundingSettlements: settlements, symbol: symbol);
                    var slices = new Dictionary<string, object>(StringComparer.Ordinal);
                    foreach (var (label, from, to) in new (string Label, DateTimeOffset From, DateTimeOffset To)[]
                    {
                        ("inSample", DateTimeOffset.MinValue, inSampleEnd),
                        ("validation", inSampleEnd, outOfSampleStart),
                        ("outOfSample", outOfSampleStart, DateTimeOffset.MaxValue)
                    })
                    {
                        var slice = result.Trades.Where(trade => trade.OpenedAt >= from && trade.OpenedAt < to).ToList();
                        var totals = Totals.From(slice);
                        slices[label] = totals.ToAnonymous();
                        if (label == "outOfSample")
                        {
                            acc[id].Add(totals, symbol);
                            foreach (var trade in slice)
                            {
                                lock (book)
                                {
                                    book.Add(new ProductionNewBookTrade(id, symbol, trade.Side, trade.OpenedAt, trade.ClosedAt, trade.PnL, trade.GrossPnl, trade.Fees, trade.SlippageCost, trade.FundingPnl, settings.InitialBalance));
                                }
                            }
                        }

                        if (label == "inSample")
                        {
                            acc[id].AddInSample(totals);
                        }
                    }

                    var runWalkForward = timeframe == "1h" || majors.Contains(symbol, StringComparer.Ordinal);
                    if (runWalkForward && !string.Equals(id, StrategyTemplateKeys.MarketRegimeNew, StringComparison.Ordinal))
                    {
                        var rows = WalkForward(replay, definition, candles, settings, futures, settlements, symbol, timeframe);
                        var withTrades = rows.Count(row => row.Trades > 0);
                        var nonNegative = rows.Count(row => row.Trades > 0 && row.Net >= 0m);
                        wfCounts.AddOrUpdate(id, (rows.Count, withTrades, nonNegative), (_, prior) => (prior.Windows + rows.Count, prior.WithTrades + withTrades, prior.NonNegative + nonNegative));
                        lock (writeLock)
                        {
                            foreach (var row in rows)
                            {
                                wfWriter.WriteLine(JsonSerializer.Serialize(row));
                            }
                        }
                    }

                    lock (writeLock)
                    {
                        symbolWriter.WriteLine(JsonSerializer.Serialize(new
                        {
                            symbol,
                            timeframe,
                            strategy = id,
                            bars = candles.Count,
                            from = candles[0].OpenTime,
                            to = candles[^1].CloseTime,
                            gaps,
                            duplicates,
                            badBars = bad,
                            fundingLoaded,
                            basisLoaded,
                            costNotes = result.CostNotes,
                            slices
                        }));
                    }
                }

                var finished = Interlocked.Increment(ref done);
                if (finished % 25 == 0)
                {
                    Console.WriteLine($"{timeframe} {finished}/{symbols.Length}");
                }
            });

            if (timeframe == "1h")
            {
                RunMomentum(cache, symbols, timeframe, settings, acc, book, symbolWriter, writeLock, peerNames: null);
            }
            else
            {
                RunMomentum(cache, majors, timeframe, settings, acc, book, symbolWriter, writeLock, majors);
            }

            var covered = coverage.ToArray();
            inventory.Add(new
            {
                timeframe,
                files = covered.Length,
                bars = covered.Sum(row => row.Bars),
                from = covered.Where(row => row.Bars > 0).Select(row => row.Start).DefaultIfEmpty(DateTimeOffset.MinValue).Min(),
                to = covered.Where(row => row.Bars > 0).Select(row => row.End).DefaultIfEmpty(DateTimeOffset.MinValue).Max(),
                symbolsWithGaps = covered.Count(row => row.Gaps > 0),
                duplicateBars = covered.Sum(row => row.Duplicates),
                badBars = covered.Sum(row => row.Bad),
                symbolsWithFunding = covered.Count(row => row.Funding),
                symbolsWithBasis = covered.Count(row => row.Basis)
            });

            foreach (var pair in acc)
            {
                var wf = wfCounts.TryGetValue(pair.Key, out var counts) ? counts : (Windows: 0, WithTrades: 0, NonNegative: 0);
                var strategyTrades = book.Where(trade => trade.StrategyId == pair.Key).ToList();
                var overlay = strategyTrades.Count == 0
                    ? null
                    : ProductionNewPortfolio.Simulate(strategyTrades);
                var required = pair.Key switch
                {
                    StrategyTemplateKeys.FundingBasisNew => covered.Any(row => row.Basis),
                    StrategyTemplateKeys.MomentumRelativeStrengthNew => true,
                    StrategyTemplateKeys.MarketRegimeNew => true,
                    _ => covered.Length > 0
                };
                var evidence = new ProductionNewEvidence(
                    pair.Value.SymbolsWithTrades,
                    pair.Value.Trades,
                    pair.Value.Longs,
                    pair.Value.Shorts,
                    pair.Value.Net,
                    pair.Value.ProfitFactor ?? 0m,
                    pair.Value.Negatives > 0m,
                    overlay?.MaxDrawdownPercent ?? 0m,
                    pair.Value.InSampleTrades,
                    pair.Value.InSampleNet,
                    wf.Windows,
                    wf.WithTrades,
                    wf.NonNegative,
                    required);
                var verdict = pair.Key == StrategyTemplateKeys.MarketRegimeNew
                    ? ProductionNewVerdict.InsufficientEvidence
                    : ProductionNewVerdict.Judge(evidence);
                var row = new
                {
                    timeframe,
                    strategy = pair.Key,
                    verdict,
                    outOfSample = pair.Value.ToAnonymous(),
                    inSampleTrades = pair.Value.InSampleTrades,
                    inSampleNet = pair.Value.InSampleNet,
                    walkForwardWindows = wf.Windows,
                    walkForwardWindowsWithTrades = wf.WithTrades,
                    walkForwardWindowsNonNegative = wf.NonNegative,
                    portfolio = overlay,
                    profitFactorText = ProductionNewVerdict.FormatProfitFactor(pair.Value.Trades, pair.Value.Losses, pair.Value.ProfitFactor)
                };
                summaries.Add(row);
                Console.WriteLine($"{pair.Key} {timeframe} verdict={verdict} oosTrades={pair.Value.Trades} oosNet={pair.Value.Net.ToString("0.##", CultureInfo.InvariantCulture)} pf={row.profitFactorText ?? "n/a"} wf={wf.Windows}");
            }

            var combined = ProductionNewPortfolio.Simulate(book);
            portfolios.Add(new { timeframe, combined });
            Console.WriteLine($"portfolio {timeframe} accepted={combined.Accepted} rejected={combined.Rejected} net={combined.NetPnl.ToString("0.##", CultureInfo.InvariantCulture)} maxDD%={combined.MaxDrawdownPercent.ToString("0.##", CultureInfo.InvariantCulture)}");
        }

        var document = new
        {
            generatedUtc = stamp,
            git,
            label = "production-new",
            costs = new
            {
                feePercentPerSide = settings.FeePercent,
                slippagePercentPerSide = settings.SlippagePercent,
                riskPercent = settings.RiskPercent,
                leverage = settings.Leverage,
                initialEquity = settings.InitialBalance,
                entry = "next bar open",
                funding = "INCLUDED when artifacts/data/futures-history-v1/funding/{symbol}.json has rows. Missing funding is not zero."
            },
            parameters = "documented defaults, fixed before this run, not fitted on the final 20%",
            validationGates = new
            {
                ProductionNewVerdict.MinimumSymbols,
                ProductionNewVerdict.MinimumOutOfSampleTrades,
                ProductionNewVerdict.MinimumSideTrades,
                ProductionNewVerdict.MinimumWalkForwardWindows,
                ProductionNewVerdict.MinimumProfitFactor,
                ProductionNewVerdict.MaximumDrawdownPercent,
                inSampleNetMustBePositive = true,
                singleSeriesPassIsNotValidated = true
            },
            inventory,
            strategies = summaries,
            portfolios,
            omissions = new[]
            {
                "Walk-forward reset windows were executed for every 1h symbol and for symbols that have a basis file on 15m and 5m. Other 15m and 5m symbols have a full causal replay and chronological trade split, not a reset walk-forward.",
                "Momentum peers on 15m and 5m are the basis-file symbols only. The 1h momentum book uses every cached 1h symbol.",
                "Mark and index files exist only for the basis-file symbols. funding-basis-new does not run where basis is missing.",
                "The portfolio overlay scales isolated-book pnl onto shared equity. It does not re-simulate a replacement signal after a rejection.",
                "Listing survivorship is not corrected."
            }
        };
        var summaryPath = Path.Combine(outDir, "summary.json");
        File.WriteAllText(summaryPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"wrote {summaryPath}");
        return 0;
    }

    private static void RunMomentum(
        string cache,
        IReadOnlyList<string> traded,
        string timeframe,
        ReplaySettings settings,
        Dictionary<string, Totals> acc,
        List<ProductionNewBookTrade> book,
        StreamWriter writer,
        object writeLock,
        IReadOnlyList<string>? peerNames)
    {
        var names = (peerNames ?? traded).Where(symbol => File.Exists(Path.Combine(cache, $"{symbol}_{timeframe}.json"))).ToArray();
        if (names.Length < 2)
        {
            Console.WriteLine($"momentum {timeframe}: peer universe unavailable");
            return;
        }

        Console.WriteLine($"momentum {timeframe}: loading {names.Length} peers");
        var peers = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in names)
        {
            try
            {
                peers[symbol] = Load(Path.Combine(cache, $"{symbol}_{timeframe}.json"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"momentum {timeframe} {symbol} load failed: {ex.Message}");
            }
        }

        if (peers.Count < 2 || !peers.ContainsKey("BTCUSDT"))
        {
            Console.WriteLine($"momentum {timeframe}: BTCUSDT peer book missing");
            return;
        }

        var frame = TimeframeExtensions.TryParseInterval(timeframe, out var parsed) ? parsed.ToDuration() : TimeSpan.FromHours(1);
        var parameters = StrategyTemplates.DefaultsFor(StrategyTemplateKeys.MomentumRelativeStrengthNew, false) with { Timeframe = timeframe };
        var returns = new Dictionary<string, Dictionary<DateTimeOffset, (decimal Day, decimal Week)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in peers)
        {
            returns[pair.Key] = MomentumReturns(pair.Value, frame * 1.5);
        }

        Console.WriteLine($"momentum {timeframe}: ranking {peers["BTCUSDT"].Count} timestamps");
        var scorebook = new Dictionary<DateTimeOffset, IReadOnlyList<MomentumScore>>();
        foreach (var time in peers["BTCUSDT"].Select(candle => candle.CloseTime).Distinct())
        {
            var rows = new List<MomentumScore>();
            foreach (var pair in peers)
            {
                var end = ProductionNewMath.LastClosedIndex(pair.Value, time);
                if (end < 2 || !ProductionNewMath.BarUsable(pair.Value[end]))
                {
                    continue;
                }

                if (time - pair.Value[end].CloseTime > frame * 1.5)
                {
                    continue;
                }

                if (!returns[pair.Key].TryGetValue(pair.Value[end].CloseTime, out var value))
                {
                    continue;
                }

                rows.Add(new MomentumScore(pair.Key, parameters.MomentumWeight24h * value.Day + parameters.MomentumWeight7d * value.Week));
            }

            rows.Sort(static (left, right) =>
            {
                var score = right.Score.CompareTo(left.Score);
                return score != 0 ? score : string.Compare(left.Symbol, right.Symbol, StringComparison.Ordinal);
            });
            scorebook[time] = rows;
        }

        var replay = new BacktestReplay(new StrategyEngine());
        foreach (var symbol in traded)
        {
            if (!peers.TryGetValue(symbol, out var candles) || candles.Count < 80)
            {
                continue;
            }

            var definition = StrategyValidation.Definition(StrategyTemplateKeys.MomentumRelativeStrengthNew, timeframe);
            definition.Symbol = symbol;
            var result = replay.Run(
                definition,
                candles,
                settings,
                symbol: symbol,
                peerCandles: peers,
                momentumScorebook: scorebook);
            var (a, b) = StrategyValidation.ChronologicalSplitIndices(candles.Count);
            var inSampleEnd = candles[a].OpenTime;
            var outOfSampleStart = candles[b].OpenTime;
            var oos = result.Trades.Where(trade => trade.OpenedAt >= outOfSampleStart).ToList();
            var sample = result.Trades.Where(trade => trade.OpenedAt < inSampleEnd).ToList();
            acc[StrategyTemplateKeys.MomentumRelativeStrengthNew].Add(Totals.From(oos), symbol);
            acc[StrategyTemplateKeys.MomentumRelativeStrengthNew].AddInSample(Totals.From(sample));
            lock (book)
            {
                foreach (var trade in oos)
                {
                    book.Add(new ProductionNewBookTrade(StrategyTemplateKeys.MomentumRelativeStrengthNew, symbol, trade.Side, trade.OpenedAt, trade.ClosedAt, trade.PnL, trade.GrossPnl, trade.Fees, trade.SlippageCost, trade.FundingPnl, settings.InitialBalance));
                }
            }

            lock (writeLock)
            {
                writer.WriteLine(JsonSerializer.Serialize(new
                {
                    symbol,
                    timeframe,
                    strategy = StrategyTemplateKeys.MomentumRelativeStrengthNew,
                    peers = peers.Count,
                    outOfSampleTrades = oos.Count,
                    outOfSampleNet = oos.Sum(trade => trade.PnL),
                    inSampleTrades = sample.Count,
                    inSampleNet = sample.Sum(trade => trade.PnL)
                }));
            }
        }
    }

    private static Dictionary<DateTimeOffset, (decimal Day, decimal Week)> MomentumReturns(IReadOnlyList<MarketCandle> candles, TimeSpan maxGap)
    {
        var map = new Dictionary<DateTimeOffset, (decimal Day, decimal Week)>();
        var dayIndex = 0;
        var weekIndex = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            if (i < 2 || candles[i].Close <= 0m)
            {
                continue;
            }

            var dayTarget = candles[i].CloseTime - TimeSpan.FromHours(24);
            var weekTarget = candles[i].CloseTime - TimeSpan.FromDays(7);
            while (dayIndex + 1 < i && candles[dayIndex + 1].CloseTime <= dayTarget)
            {
                dayIndex++;
            }

            while (weekIndex + 1 < i && candles[weekIndex + 1].CloseTime <= weekTarget)
            {
                weekIndex++;
            }

            if (candles[dayIndex].CloseTime > dayTarget || candles[weekIndex].CloseTime > weekTarget)
            {
                continue;
            }

            if (candles[dayIndex].Close <= 0m || candles[weekIndex].Close <= 0m)
            {
                continue;
            }

            if (dayTarget - candles[dayIndex].CloseTime > maxGap || weekTarget - candles[weekIndex].CloseTime > maxGap)
            {
                continue;
            }

            map[candles[i].CloseTime] = (candles[i].Close / candles[dayIndex].Close - 1m, candles[i].Close / candles[weekIndex].Close - 1m);
        }

        return map;
    }

    private static List<WalkRow> WalkForward(
        BacktestReplay replay,
        TradingPlatform.Strategies.Engine.StrategyDefinition definition,
        IReadOnlyList<MarketCandle> candles,
        ReplaySettings settings,
        StrategyFuturesSeries? futures,
        IReadOnlyList<ReplayFundingSettlement>? settlements,
        string symbol,
        string timeframe)
    {
        var train = Math.Max(40, candles.Count / 5);
        var test = Math.Max(20, candles.Count / 10);
        var windows = StrategyValidation.WalkForwardWindows(candles.Count, train, test, test);
        var cache = new CausalIndicatorCache(candles);
        var rows = new List<WalkRow>(windows.Count);
        foreach (var (start, length) in windows)
        {
            var testStart = start + train;
            var testEnd = Math.Min(candles.Count, start + length);
            if (testStart >= testEnd)
            {
                continue;
            }

            var result = replay.Run(
                definition,
                candles,
                settings,
                cache,
                testStart,
                testEnd,
                futures: futures,
                fundingSettlements: settlements,
                symbol: symbol);
            var totals = Totals.From(result.Trades);
            rows.Add(new WalkRow(symbol, timeframe, definition.Template ?? "", candles[testStart].OpenTime, candles[testEnd - 1].CloseTime, totals.Trades, totals.Longs, totals.Shorts, totals.Net, totals.ProfitFactor, totals.Losses == 0 && totals.Trades > 0 ? "undefined_no_losing_trades" : null));
        }

        return rows;
    }

    private static (int Duplicates, int Bad, int Gaps) Quality(IReadOnlyList<MarketCandle> candles, string timeframe)
    {
        var duplicates = 0;
        var seen = new HashSet<DateTimeOffset>();
        var bad = 0;
        var gaps = 0;
        for (var i = 0; i < candles.Count; i++)
        {
            if (!seen.Add(candles[i].OpenTime))
            {
                duplicates++;
            }

            if (!ProductionNewMath.BarUsable(candles[i]))
            {
                bad++;
            }

            if (ProductionNewMath.HasGap(candles, i, timeframe))
            {
                gaps++;
            }
        }

        return (duplicates, bad, gaps);
    }

    private static List<MarketCandle> Load(string path)
    {
        var bars = JsonSerializer.Deserialize<List<CachedBar>>(File.ReadAllText(path)) ?? [];
        var candles = new List<MarketCandle>(bars.Count);
        foreach (var bar in bars)
        {
            candles.Add(new MarketCandle
            {
                OpenTime = bar.OpenTime,
                CloseTime = bar.CloseTime,
                Open = bar.Open,
                High = bar.High,
                Low = bar.Low,
                Close = bar.Close,
                Volume = bar.Volume,
                IsClosed = true
            });
        }

        candles.Sort(static (left, right) => left.OpenTime.CompareTo(right.OpenTime));
        if (candles.Count == 0)
        {
            return candles;
        }

        var unique = new List<MarketCandle>(candles.Count);
        unique.Add(candles[0]);
        for (var i = 1; i < candles.Count; i++)
        {
            if (candles[i].OpenTime == unique[^1].OpenTime)
            {
                continue;
            }

            unique.Add(candles[i]);
        }

        return unique;
    }

    private static string GitRevision(string root)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                UseShellExecute = false
            });
            return process?.StandardOutput.ReadToEnd().Trim() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private sealed class Totals
    {
        public int Trades;
        public int Longs;
        public int Shorts;
        public int Wins;
        public int Losses;
        public int SymbolsWithTrades;
        public int InSampleTrades;
        public decimal Net;
        public decimal Gross;
        public decimal Fees;
        public decimal Slip;
        public decimal Funding;
        public decimal Positives;
        public decimal Negatives;
        public decimal HoldHours;
        public decimal InSampleNet;
        public decimal? ProfitFactor => Negatives > 0m ? Positives / Negatives : null;

        public static Totals From(IReadOnlyList<ReplayTrade> trades)
        {
            var totals = new Totals();
            foreach (var trade in trades)
            {
                totals.AddTrade(trade.Side, trade.PnL, trade.GrossPnl, trade.Fees, trade.SlippageCost, trade.FundingPnl, trade.ClosedAt - trade.OpenedAt);
            }

            return totals;
        }

        public void Add(Totals other, string symbol)
        {
            lock (this)
            {
                Trades += other.Trades;
                Longs += other.Longs;
                Shorts += other.Shorts;
                Wins += other.Wins;
                Losses += other.Losses;
                Net += other.Net;
                Gross += other.Gross;
                Fees += other.Fees;
                Slip += other.Slip;
                Funding += other.Funding;
                Positives += other.Positives;
                Negatives += other.Negatives;
                HoldHours += other.HoldHours;
                if (other.Trades > 0)
                {
                    SymbolsWithTrades++;
                }
            }

            _ = symbol;
        }

        public void AddInSample(Totals other)
        {
            lock (this)
            {
                InSampleTrades += other.Trades;
                InSampleNet += other.Net;
            }
        }

        public object ToAnonymous() => new
        {
            trades = Trades,
            longs = Longs,
            shorts = Shorts,
            wins = Wins,
            losses = Losses,
            symbols = SymbolsWithTrades,
            net = Net,
            gross = Gross,
            fees = Fees,
            slippage = Slip,
            funding = Funding,
            profitFactor = ProfitFactor,
            profitFactorText = ProductionNewVerdict.FormatProfitFactor(Trades, Losses, ProfitFactor),
            expectancy = Trades == 0 ? 0m : Net / Trades,
            averageWin = Wins == 0 ? 0m : Positives / Wins,
            averageLoss = Losses == 0 ? 0m : -(Negatives / Losses),
            averageHoldHours = Trades == 0 ? 0m : HoldHours / Trades
        };

        private void AddTrade(string side, decimal net, decimal gross, decimal fees, decimal slip, decimal funding, TimeSpan hold)
        {
            Trades++;
            if (string.Equals(side, "Short", StringComparison.OrdinalIgnoreCase))
            {
                Shorts++;
            }
            else
            {
                Longs++;
            }

            Net += net;
            Gross += gross;
            Fees += fees;
            Slip += slip;
            Funding += funding;
            HoldHours += (decimal)hold.TotalHours;
            if (net > 0m)
            {
                Wins++;
                Positives += net;
            }
            else if (net < 0m)
            {
                Losses++;
                Negatives += -net;
            }
        }
    }

    private sealed record WalkRow(
        string Symbol,
        string Timeframe,
        string Strategy,
        DateTimeOffset From,
        DateTimeOffset To,
        int Trades,
        int Longs,
        int Shorts,
        decimal Net,
        decimal? ProfitFactor,
        string? ProfitFactorText);

    private sealed record CachedBar(
        DateTimeOffset OpenTime,
        DateTimeOffset CloseTime,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        decimal Volume);
}
