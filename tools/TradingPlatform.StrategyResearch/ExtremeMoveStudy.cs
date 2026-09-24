using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.StrategyResearch;

/// <summary>
/// Research-only extreme-move event study. Does not place orders, enable Paper, or change production flags.
/// </summary>
internal static class ExtremeMoveStudy
{
    private const int Horizon = 24;
    private const int Warmup = 220;
    private const int MinBars = 1500;
    private const int Cooldown = 24;
    private static readonly DateTimeOffset IsEnd = new(2025, 9, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValEnd = new(2026, 3, 23, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Block1 = new(2025, 3, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Block2 = new(2025, 9, 18, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Block3 = new(2026, 3, 18, 0, 0, 0, TimeSpan.Zero);

    private static readonly (string Id, double Level, int Dir)[] Thresholds =
    [
        ("UP_10", 0.10, 1), ("UP_20", 0.20, 1), ("UP_30", 0.30, 1), ("UP_50", 0.50, 1),
        ("UP_80", 0.80, 1), ("UP_100", 1.00, 1), ("UP_150", 1.50, 1),
        ("DOWN_10", -0.10, -1), ("DOWN_20", -0.20, -1), ("DOWN_30", -0.30, -1),
        ("DOWN_50", -0.50, -1), ("DOWN_80", -0.80, -1)
    ];

    private static readonly string[] KeyThresholds = ["UP_30", "UP_50", "UP_80", "UP_100", "DOWN_30", "DOWN_50", "DOWN_80"];
    private static readonly int[] Leads = [24, 12, 8, 4, 2, 1, 0];
    private static readonly string[] MicroSymbols =
    [
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT"
    ];

    private static readonly string[] FeatureIds =
    [
        "ret_1", "ret_3", "ret_5", "ret_10", "ret_20", "ret_50", "roc_10", "mom_accel",
        "ema_dist_9", "ema_dist_20", "ema_dist_50", "ema_dist_100", "ema_dist_200",
        "ema20_slope_1", "ema20_slope_3", "ema20_slope_6", "ema20_slope_12", "ema20_slope_24",
        "ema_cross_9_20", "macd", "macd_signal", "macd_hist", "macd_hist_accel",
        "rsi_7", "rsi_14", "rsi_21", "stoch_k", "stoch_rsi", "cci_20", "willr_14", "mfi_14",
        "adx_14", "di_plus", "di_minus", "aroon_osc", "trix_15", "ppo",
        "atr_pct", "atr_change", "atr_accel", "rv_24", "parkinson_24", "garman_klass_24", "rogers_satchell_24", "yang_zhang_24",
        "range_pct", "tr_pct", "vol_compression", "vol_expansion", "bb_width", "bb_pct", "keltner_width", "bb_keltner_squeeze", "vol_of_vol",
        "volume", "volume_change", "volume_roc", "rel_volume", "volume_pct", "volume_z", "volume_accel", "volume_atr", "volume_range",
        "obv_slope", "cmf_20", "pv_divergence", "volume_climax", "volume_compression",
        "vwap_dist", "vwap_slope", "poc_dist",
        "oi_change_1", "oi_change_3", "oi_change_6", "oi_change_12", "oi_change_24", "oi_pct_change_12", "oi_z", "oi_pct", "oi_accel", "oi_vol", "oi_over_volume",
        "price_up_oi_up", "price_up_oi_down", "price_down_oi_up", "price_down_oi_down", "ret_x_oi", "oi_x_volume", "oi_x_vol", "oi_x_funding",
        "funding_rate", "funding_pct", "funding_z", "funding_change", "funding_accel", "cum_funding", "funding_persistence", "funding_extreme", "funding_reversal",
        "funding_x_oi", "funding_x_price", "funding_x_mom",
        "basis", "basis_change", "basis_accel", "basis_pct", "basis_z", "basis_x_oi", "basis_x_funding", "basis_x_price",
        "taker_buy_ratio", "taker_imbalance", "taker_imbalance_change", "taker_imbalance_accel", "taker_imbalance_pct", "taker_imbalance_z",
        "taker_x_oi", "taker_x_funding", "taker_x_volume", "taker_x_ret",
        "depth_imbalance", "depth_imbalance_change", "depth_imbalance_accel", "depth_pct", "depth_z", "depth_ratio",
        "breadth_pos", "btc_ret_24", "btc_rv_24", "dispersion",
        "pa_hh", "pa_bos", "pa_choch", "pa_sweep", "pa_compress", "pa_inside", "pa_outside", "pa_engulf", "pa_pin", "pa_hammer", "pa_star", "pa_marubozu",
        "pa_flag", "pa_hs", "pa_wedge", "pa_triangle",
        "ret5_x_oi", "ret5_x_funding", "ret5_x_oi_x_funding", "ret5_x_volz", "volz_x_atr", "oi_x_taker", "funding_x_basis", "depth_x_taker", "cs_rank_x_oi", "cs_rank_x_funding"
    ];

    private static readonly string[] UniverseModel =
    [
        "ret_1", "ret_5", "ret_20", "mom_accel", "ema_dist_20", "ema_dist_200", "ema20_slope_6",
        "macd_hist", "rsi_14", "stoch_k", "atr_pct", "atr_change", "rv_24", "bb_width", "volume_z",
        "volume_roc", "obv_slope", "vwap_dist", "breadth_pos", "btc_ret_24", "pa_bos", "pa_compress", "range_pct"
    ];

    private static readonly string[] MicroModel =
    [
        "oi_change_12", "oi_z", "funding_rate", "funding_change", "basis", "taker_imbalance", "taker_imbalance_change",
        "depth_imbalance", "ret5_x_oi", "funding_x_oi", "oi_x_taker", "volz_x_atr"
    ];

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        Console.WriteLine("Extreme-move precursor study. Research only. No orders. Paper off. Live off.");
        var manifest = ManifestText();
        var featureHash = Sha(manifest);
        var eventHash = Sha(EventDefinitionText());
        var experimentHash = Sha(manifest + "\n" + EventDefinitionText());
        var outDir = Path.Combine(root, "artifacts", "strategy-research", "extreme-move");
        Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir, "pre_oos_manifest.txt"), manifest + "\n" + EventDefinitionText());

        var self = SelfCheck();
        if (!self.Ok)
        {
            Console.WriteLine("Event-definition self-check failed: " + self.Detail);
            return 1;
        }

        var files = Directory.GetFiles(cacheDir, "*_1h.json");
        Console.WriteLine($"1h files {files.Length}");
        var books = new List<Book>(files.Length);
        var skipped = 0;
        foreach (var file in files)
        {
            var symbol = Path.GetFileName(file);
            var us = symbol.IndexOf("_1h.json", StringComparison.OrdinalIgnoreCase);
            if (us < 0)
            {
                continue;
            }

            symbol = symbol[..us];
            var book = ReadBook(file, symbol);
            if (book.N < MinBars)
            {
                skipped++;
                continue;
            }

            books.Add(book);
        }

        Console.WriteLine($"Books {books.Count}. Short history skipped {skipped}.");
        var btc = books.FirstOrDefault(b => b.Symbol == "BTCUSDT");
        var eth = books.FirstOrDefault(b => b.Symbol == "ETHUSDT");
        var breadth = BuildBreadth(books);
        var micro = await LoadMicroAsync(root, books);
        var episodes = new List<Episode>(65536);
        var controls = new List<Episode>(65536);
        var nested = new int[Thresholds.Length];
        var independent = new int[Thresholds.Length];
        var takerSymbols = 0;
        var lookahead = "NOT_RUN";
        var done = 0;
        foreach (var book in books)
        {
            var feat = ComputeFeatures(book, btc, eth, breadth, micro);
            var eps = FindEpisodes(book, feat);
            foreach (var ep in eps)
            {
                episodes.Add(ep);
                for (var t = 0; t < Thresholds.Length; t++)
                {
                    if (ep.Hit[t])
                    {
                        nested[t]++;
                        if (t == ep.PrimaryThreshold)
                        {
                            independent[t]++;
                        }
                    }
                }
            }

            controls.AddRange(MatchControls(book, feat, eps));
            if (book.TakerOk)
            {
                takerSymbols++;
            }

            if ((done % 25) == 0)
            {
                Console.WriteLine($"features {book.Symbol} {done}/{books.Count} episodes {episodes.Count}");
            }

            done++;
            if (book.Symbol == "BTCUSDT")
            {
                lookahead = LookaheadCheck(book);
            }
        }

        Console.WriteLine($"Independent episodes {episodes.Count}. Controls {controls.Count}. Taker-ok symbols {takerSymbols}.");
        var shortHorizon = RunShortHorizon(cacheDir, micro);
        var rows = Score(episodes, controls);
        var models = FitModels(episodes, controls);
        var costs = CostStress(episodes, controls, models);
        var labels = LabelRows(rows);
        var safety = ReadSafety(root);
        WriteReports(root, new StudyContext(
            featureHash, eventHash, experimentHash, books.Count, skipped, episodes, controls,
            nested, independent, rows, labels, models, costs, shortHorizon, lookahead, self.Detail,
            takerSymbols, micro, safety));
        Console.WriteLine(safety.Line);
        Console.WriteLine($"feature_manifest_hash {featureHash}");
        Console.WriteLine($"event_definition_hash {eventHash}");
        Console.WriteLine($"experiment_manifest_hash {experimentHash}");
        return 0;
    }

    private static (bool Ok, string Detail) SelfCheck()
    {
        var n = 80;
        var c = new float[n];
        var h = new float[n];
        var l = new float[n];
        for (var i = 0; i < n; i++)
        {
            c[i] = 100;
            h[i] = 101;
            l[i] = 99;
        }

        for (var i = 40; i <= 50; i++)
        {
            c[i] = 100 + (i - 40) * 6;
            h[i] = c[i] + 1;
            l[i] = c[i] - 1;
        }

        for (var i = 51; i < n; i++)
        {
            c[i] = c[50];
            h[i] = c[i];
            l[i] = c[i];
        }

        var book = new Book
        {
            Symbol = "TEST",
            OpenMs = Enumerable.Range(0, n).Select(i => DateTimeOffset.UnixEpoch.AddHours(i).ToUnixTimeMilliseconds()).ToArray(),
            C = c,
            H = h,
            L = l,
            V = Enumerable.Repeat(1f, n).ToArray(),
            Tb = Enumerable.Repeat(0.4f, n).ToArray()
        };
        var feat = new float[FeatureIds.Length, n];
        for (var f = 0; f < FeatureIds.Length; f++)
        {
            for (var i = 0; i < n; i++)
            {
                feat[f, i] = float.NaN;
            }
        }

        var eps = FindEpisodes(book, feat, 30);
        var up = eps.Count(e => e.Dir > 0);
        var nestedHits = eps.Where(e => e.Dir > 0).Sum(e => e.Hit.Count(x => x));
        var ok = up == 1 && nestedHits >= 4;
        return (ok, $"synthetic ramp episodes={eps.Count} up={up} nestedHits={nestedHits}");
    }

    private static List<Episode> FindEpisodes(Book book, float[,] feat, int warmup = Warmup)
    {
        var list = new List<Episode>();
        var i = warmup;
        var n = book.N;
        while (i < n - Horizon - 1)
        {
            if (!book.GapOk(i, Horizon))
            {
                i++;
                continue;
            }

            var close = book.C[i];
            if (close <= 0)
            {
                i++;
                continue;
            }

            var tUp = FirstHit(book, i, 0.10, 1);
            var tDn = FirstHit(book, i, -0.10, -1);
            if (tUp < 0 && tDn < 0)
            {
                i++;
                continue;
            }

            var dir = 1;
            if (tUp < 0)
            {
                dir = -1;
            }
            else if (tDn >= 0 && (tDn < tUp || (tDn == tUp && AbsExcursion(book, i, -1) > AbsExcursion(book, i, 1))))
            {
                dir = -1;
            }

            var maxBar = i + 1;
            var maxExc = dir > 0 ? -1f : 1f;
            for (var j = 1; j <= Horizon; j++)
            {
                var exc = dir > 0
                    ? book.H[i + j] / close - 1f
                    : book.L[i + j] / close - 1f;
                if (dir > 0 ? exc > maxExc : exc < maxExc)
                {
                    maxExc = exc;
                    maxBar = i + j;
                }
            }

            var hit = new bool[Thresholds.Length];
            var tth = new int[Thresholds.Length];
            Array.Fill(tth, -1);
            var primary = -1;
            for (var t = 0; t < Thresholds.Length; t++)
            {
                if (Thresholds[t].Dir != dir)
                {
                    continue;
                }

                var hitBar = FirstHit(book, i, Thresholds[t].Level, dir);
                if (hitBar >= 0)
                {
                    hit[t] = true;
                    tth[t] = hitBar;
                    primary = t;
                }
            }

            if (primary < 0)
            {
                i++;
                continue;
            }

            var when = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i]);
            var ep = new Episode
            {
                Symbol = book.Symbol,
                Start = i,
                StartMs = book.OpenMs[i],
                Dir = dir,
                MaxExc = maxExc,
                MaxBar = maxBar,
                TimeToMax = maxBar - i,
                Hit = hit,
                TimeTo = tth,
                PrimaryThreshold = primary,
                Split = SplitOf(when),
                Block = BlockOf(when),
                Speed = SpeedOf(tth[primary]),
                Micro = MicroSymbols.Contains(book.Symbol),
                Feat = Snapshot(feat, i, maxBar, Math.Max(1, (dir > 0 ? Math.Max(tUp, 1) : Math.Max(tDn, 1)) / 2))
            };
            var atr = feat[IndexOf("atr_pct"), i];
            ep.VolBucket = float.IsNaN(atr) ? -1 : atr < 0.33f ? 0 : atr > 0.66f ? 2 : 1;
            var btcRet = feat[IndexOf("btc_ret_24"), i];
            ep.BtcRegime = float.IsNaN(btcRet) ? 0 : btcRet > 0.02f ? 1 : btcRet < -0.02f ? -1 : 0;
            list.Add(ep);
            i = maxBar + Cooldown;
        }

        return list;
    }

    private static int FirstHit(Book book, int i, double level, int dir)
    {
        var close = book.C[i];
        for (var j = 1; j <= Horizon; j++)
        {
            var exc = dir > 0 ? book.H[i + j] / close - 1.0 : book.L[i + j] / close - 1.0;
            if (dir > 0 ? exc >= level : exc <= level)
            {
                return j;
            }
        }

        return -1;
    }

    private static float AbsExcursion(Book book, int i, int dir)
    {
        var close = book.C[i];
        var best = 0f;
        for (var j = 1; j <= Horizon; j++)
        {
            var exc = dir > 0 ? book.H[i + j] / close - 1f : book.L[i + j] / close - 1f;
            if (Math.Abs(exc) > Math.Abs(best))
            {
                best = exc;
            }
        }

        return Math.Abs(best);
    }

    private static float[][] Snapshot(float[,] feat, int start, int maxBar, int duringOffset)
    {
        var leads = Leads.Length + 1;
        var snap = new float[leads][];
        for (var L = 0; L < Leads.Length; L++)
        {
            snap[L] = Row(feat, start - Leads[L]);
        }

        var during = Math.Clamp(start + Math.Max(1, duringOffset), 0, feat.GetLength(1) - 1);
        if (during > maxBar)
        {
            during = maxBar;
        }

        snap[^1] = Row(feat, during);
        return snap;
    }

    private static float[] Row(float[,] feat, int index)
    {
        var row = new float[FeatureIds.Length];
        if (index < 0 || index >= feat.GetLength(1))
        {
            Array.Fill(row, float.NaN);
            return row;
        }

        for (var f = 0; f < FeatureIds.Length; f++)
        {
            row[f] = feat[f, index];
        }

        return row;
    }

    private static List<Episode> MatchControls(Book book, float[,] feat, List<Episode> eps)
    {
        var zones = eps.Select(e => (Math.Max(0, e.Start - Horizon), e.MaxBar + Cooldown)).ToArray();
        var list = new List<Episode>(eps.Count);
        foreach (var ep in eps)
        {
            var found = -1;
            for (var step = 1; step < book.N; step++)
            {
                var candidate = ep.Start - (48 + step);
                if (candidate < Warmup)
                {
                    candidate = ep.MaxBar + Cooldown + step;
                }

                if (candidate < Warmup || candidate >= book.N - Horizon - 1)
                {
                    continue;
                }

                if (zones.Any(z => candidate >= z.Item1 && candidate <= z.Item2))
                {
                    continue;
                }

                if (SplitOf(DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[candidate])) != ep.Split)
                {
                    continue;
                }

                var atr = feat[IndexOf("atr_pct"), candidate];
                var bucket = float.IsNaN(atr) ? -1 : atr < 0.33f ? 0 : atr > 0.66f ? 2 : 1;
                if (ep.VolBucket >= 0 && bucket >= 0 && bucket != ep.VolBucket)
                {
                    continue;
                }

                found = candidate;
                break;
            }

            if (found < 0)
            {
                continue;
            }

            var when = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[found]);
            list.Add(new Episode
            {
                Symbol = book.Symbol,
                Start = found,
                StartMs = book.OpenMs[found],
                Dir = ep.Dir,
                MaxExc = 0,
                MaxBar = found,
                TimeToMax = 0,
                Hit = new bool[Thresholds.Length],
                TimeTo = new int[Thresholds.Length],
                PrimaryThreshold = ep.PrimaryThreshold,
                Split = ep.Split,
                Block = BlockOf(when),
                Speed = ep.Speed,
                Micro = ep.Micro,
                VolBucket = ep.VolBucket,
                BtcRegime = ep.BtcRegime,
                ControlFor = ep.PrimaryThreshold,
                Feat = Snapshot(feat, found, found, 1)
            });
        }

        return list;
    }

    private static float[,] ComputeFeatures(Book book, Book? btc, Book? eth, Breadth breadth, MicroCache micro)
    {
        var n = book.N;
        var f = FeatureIds.Length;
        var m = new float[f, n];
        for (var a = 0; a < f; a++)
        {
            for (var b = 0; b < n; b++)
            {
                m[a, b] = float.NaN;
            }
        }

        var ema9 = Ema(book.C, 9);
        var ema20 = Ema(book.C, 20);
        var ema50 = Ema(book.C, 50);
        var ema100 = Ema(book.C, 100);
        var ema200 = Ema(book.C, 200);
        var ema12 = Ema(book.C, 12);
        var ema26 = Ema(book.C, 26);
        var macd = new float[n];
        for (var i = 0; i < n; i++)
        {
            macd[i] = ema12[i] - ema26[i];
        }

        var signal = Ema(macd, 9);
        var (rsi7, rsi14, rsi21) = (Rsi(book.C, 7), Rsi(book.C, 14), Rsi(book.C, 21));
        var atr = Atr(book, 14);
        var atrPctSeries = new float[n];
        var tr = TrueRange(book);
        var obv = new double[n];
        for (var i = 1; i < n; i++)
        {
            var sign = book.C[i] > book.C[i - 1] ? 1 : book.C[i] < book.C[i - 1] ? -1 : 0;
            obv[i] = obv[i - 1] + sign * book.V[i];
        }

        var candles = new MarketCandle[n];
        for (var i = 0; i < n; i++)
        {
            candles[i] = new MarketCandle
            {
                Open = (decimal)book.C[Math.Max(0, i)],
                High = (decimal)book.H[i],
                Low = (decimal)book.L[i],
                Close = (decimal)book.C[i],
                Volume = (decimal)book.V[i],
                IsClosed = true,
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(book.OpenMs[i])
            };
            if (i > 0)
            {
                candles[i].Open = (decimal)book.C[i - 1];
            }
        }

        for (var i = 0; i < n; i++)
        {
            candles[i].Open = i == 0 ? (decimal)book.C[i] : (decimal)book.C[i - 1];
        }

        var geoms = CandleGeom.Series(candles);
        var candleEvents = CandlePatternDetector.Detect(candles, geoms);
        var (highs, lows) = CausalSwingSeries.Detect(candles, 3);
        var structure = MarketStructureEngine.Detect(candles, highs, lows);
        var sequences = CandleSequenceEngine.Detect(geoms);
        var chartAt = new HashSet<int>();
        var hsAt = new HashSet<int>();
        var wedgeAt = new HashSet<int>();
        var triAt = new HashSet<int>();
        if (MicroSymbols.Contains(book.Symbol))
        {
            var charts = ChartPatternEngine.Detect(candles, geoms, highs, lows);
            foreach (var occ in charts)
            {
                var idx = occ.ConfirmationIndex ?? occ.DetectionIndex;
                if (idx < 0 || idx >= n || occ.Status == PatternKinds.Forming)
                {
                    continue;
                }

                if (occ.PatternType is PatternKinds.BullFlag or PatternKinds.BearFlag or PatternKinds.Pennant)
                {
                    chartAt.Add(idx);
                }

                if (occ.PatternType is PatternKinds.HeadShoulders or PatternKinds.InverseHeadShoulders or PatternKinds.WDoubleBottom or PatternKinds.MDoubleTop)
                {
                    hsAt.Add(idx);
                }

                if (occ.PatternType is PatternKinds.RisingWedge or PatternKinds.FallingWedge)
                {
                    wedgeAt.Add(idx);
                }

                if (occ.PatternType is PatternKinds.AscendingTriangle or PatternKinds.DescendingTriangle or PatternKinds.SymmetricalTriangle)
                {
                    triAt.Add(idx);
                }
            }
        }

        var rangeSeries = new float[n];
        for (var i = 0; i < n; i++)
        {
            rangeSeries[i] = book.H[i] - book.L[i];
        }

        micro.TryGet(book.Symbol, out var series);
        for (var i = Warmup; i < n; i++)
        {
            if (!book.GapOk(i, 1))
            {
                continue;
            }

            SetRet(m, book, i);
            var c = book.C[i];
            Set(m, "ema_dist_9", i, Dist(c, ema9[i]));
            Set(m, "ema_dist_20", i, Dist(c, ema20[i]));
            Set(m, "ema_dist_50", i, Dist(c, ema50[i]));
            Set(m, "ema_dist_100", i, Dist(c, ema100[i]));
            Set(m, "ema_dist_200", i, Dist(c, ema200[i]));
            Set(m, "ema20_slope_1", i, Slope(ema20, i, 1));
            Set(m, "ema20_slope_3", i, Slope(ema20, i, 3));
            Set(m, "ema20_slope_6", i, Slope(ema20, i, 6));
            Set(m, "ema20_slope_12", i, Slope(ema20, i, 12));
            Set(m, "ema20_slope_24", i, Slope(ema20, i, 24));
            if (!float.IsNaN(ema9[i]) && !float.IsNaN(ema20[i]) && ema20[i] != 0)
            {
                Set(m, "ema_cross_9_20", i, ema9[i] > ema20[i] ? 1 : -1);
            }

            Set(m, "macd", i, macd[i]);
            Set(m, "macd_signal", i, signal[i]);
            var hist = macd[i] - signal[i];
            Set(m, "macd_hist", i, hist);
            if (i >= 1)
            {
                Set(m, "macd_hist_accel", i, hist - (macd[i - 1] - signal[i - 1]));
            }

            Set(m, "rsi_7", i, rsi7[i]);
            Set(m, "rsi_14", i, rsi14[i]);
            Set(m, "rsi_21", i, rsi21[i]);
            Set(m, "stoch_k", i, Stoch(book, i, 14));
            Set(m, "stoch_rsi", i, StochOf(rsi14, i, 14));
            Set(m, "cci_20", i, Cci(book, i, 20));
            Set(m, "willr_14", i, WillR(book, i, 14));
            Set(m, "mfi_14", i, Mfi(book, i, 14));
            var adx = Adx(book, tr, i, 14);
            Set(m, "adx_14", i, adx.Adx);
            Set(m, "di_plus", i, adx.Plus);
            Set(m, "di_minus", i, adx.Minus);
            Set(m, "aroon_osc", i, Aroon(book, i, 25));
            Set(m, "trix_15", i, Trix(book.C, i, 15));
            if (ema26[i] != 0 && !float.IsNaN(ema12[i]))
            {
                Set(m, "ppo", i, (ema12[i] - ema26[i]) / Math.Abs(ema26[i]));
            }

            var atrNow = atr[i];
            atrPctSeries[i] = Percentile(atr, i, 100);
            Set(m, "atr_pct", i, atrPctSeries[i]);
            if (i >= 1 && atr[i - 1] > 0 && !float.IsNaN(atrNow))
            {
                Set(m, "atr_change", i, atrNow / atr[i - 1] - 1f);
            }

            if (i >= 2 && !float.IsNaN(m[IndexOf("atr_change"), i]) && !float.IsNaN(m[IndexOf("atr_change"), i - 1]))
            {
                Set(m, "atr_accel", i, m[IndexOf("atr_change"), i] - m[IndexOf("atr_change"), i - 1]);
            }

            Set(m, "rv_24", i, RealizedVol(book, i, 24));
            Set(m, "parkinson_24", i, Parkinson(book, i, 24));
            Set(m, "garman_klass_24", i, GarmanKlass(book, i, 24));
            Set(m, "rogers_satchell_24", i, RogersSatchell(book, i, 24));
            Set(m, "yang_zhang_24", i, YangZhang(book, i, 24));
            Set(m, "range_pct", i, Percentile(rangeSeries, i, 100));
            Set(m, "tr_pct", i, Percentile(tr, i, 100));
            var bb = Bollinger(book.C, i, 20);
            Set(m, "bb_width", i, bb.Width);
            Set(m, "bb_pct", i, bb.Pct);
            var kelt = !float.IsNaN(atrNow) && ema20[i] > 0 ? 4f * atrNow / ema20[i] : float.NaN;
            Set(m, "keltner_width", i, kelt);
            if (!float.IsNaN(bb.Width) && !float.IsNaN(kelt))
            {
                Set(m, "bb_keltner_squeeze", i, bb.Width < kelt ? 1 : 0);
            }

            Set(m, "vol_of_vol", i, VolOfVol(atr, i, 24));
            var rv = m[IndexOf("rv_24"), i];
            var rvMed = MedianWindow(m, IndexOf("rv_24"), i, 48);
            if (!float.IsNaN(rv) && !float.IsNaN(rvMed) && rvMed > 0)
            {
                Set(m, "vol_compression", i, rv < rvMed * 0.7f ? 1 : 0);
                Set(m, "vol_expansion", i, rv > rvMed * 1.3f ? 1 : 0);
            }

            Set(m, "volume", i, book.V[i]);
            if (book.V[i - 1] > 0)
            {
                Set(m, "volume_change", i, book.V[i] / book.V[i - 1] - 1f);
            }

            if (i >= 10 && book.V[i - 10] > 0)
            {
                Set(m, "volume_roc", i, book.V[i] / book.V[i - 10] - 1f);
            }

            var volMean = Mean(book.V, i, 48);
            if (volMean > 0)
            {
                Set(m, "rel_volume", i, book.V[i] / volMean);
            }

            Set(m, "volume_pct", i, Percentile(book.V, i, 100));
            Set(m, "volume_z", i, ZScore(book.V, i, 48));
            if (i >= 2 && !float.IsNaN(m[IndexOf("volume_change"), i]) && !float.IsNaN(m[IndexOf("volume_change"), i - 1]))
            {
                Set(m, "volume_accel", i, m[IndexOf("volume_change"), i] - m[IndexOf("volume_change"), i - 1]);
            }

            var range = book.H[i] - book.L[i];
            if (!float.IsNaN(atrNow) && atrNow > 0)
            {
                Set(m, "volume_atr", i, book.V[i] / atrNow);
            }

            if (range > 0)
            {
                Set(m, "volume_range", i, book.V[i] / range);
            }

            if (i >= 10)
            {
                Set(m, "obv_slope", i, (float)(obv[i] - obv[i - 10]));
            }

            Set(m, "cmf_20", i, Cmf(book, i, 20));
            var ret20 = m[IndexOf("ret_20"), i];
            var obvS = m[IndexOf("obv_slope"), i];
            if (!float.IsNaN(ret20) && !float.IsNaN(obvS) && ret20 != 0 && obvS != 0)
            {
                Set(m, "pv_divergence", i, Math.Sign(ret20) != Math.Sign(obvS) ? 1 : 0);
            }

            var vz = m[IndexOf("volume_z"), i];
            var rp = m[IndexOf("range_pct"), i];
            if (!float.IsNaN(vz) && !float.IsNaN(rp))
            {
                Set(m, "volume_climax", i, vz > 2 && rp > 0.8f ? 1 : 0);
                Set(m, "volume_compression", i, vz < -0.5f && rp < 0.3f ? 1 : 0);
            }

            var vwap = Vwap(book, i, 24);
            if (!float.IsNaN(vwap) && vwap != 0)
            {
                Set(m, "vwap_dist", i, c / vwap - 1f);
            }

            if (i >= 6)
            {
                var prev = Vwap(book, i - 6, 24);
                if (!float.IsNaN(vwap) && !float.IsNaN(prev) && prev != 0)
                {
                    Set(m, "vwap_slope", i, vwap / prev - 1f);
                }
            }

            Set(m, "poc_dist", i, PocDist(book, i, 24));
            FillMicro(m, book, series, i, c);
            var hour = book.OpenMs[i];
            if (breadth.Pos.TryGetValue(hour, out var pos) && breadth.Count.TryGetValue(hour, out var cnt) && cnt >= 30)
            {
                Set(m, "breadth_pos", i, pos / (float)cnt);
                if (breadth.Disp.TryGetValue(hour, out var disp))
                {
                    Set(m, "dispersion", i, disp);
                }
            }

            Set(m, "btc_ret_24", i, AlignReturn(btc, hour, 24));
            Set(m, "btc_rv_24", i, AlignRv(btc, hour));
            var ev = candleEvents[i];
            Set(m, "pa_inside", i, ev.Contains(PatternKinds.InsideBar) ? 1 : 0);
            Set(m, "pa_outside", i, ev.Contains(PatternKinds.OutsideBar) ? 1 : 0);
            Set(m, "pa_engulf", i, ev.Contains(PatternKinds.BullishEngulfing) || ev.Contains(PatternKinds.BearishEngulfing) ? 1 : 0);
            Set(m, "pa_pin", i, ev.Contains(PatternKinds.PinBar) ? 1 : 0);
            Set(m, "pa_hammer", i, ev.Contains(PatternKinds.Hammer) ? 1 : 0);
            Set(m, "pa_star", i, ev.Contains(PatternKinds.ShootingStar) ? 1 : 0);
            Set(m, "pa_marubozu", i, ev.Contains(PatternKinds.Marubozu) ? 1 : 0);
            var st = structure[i];
            Set(m, "pa_hh", i, st.Hh ? 1 : 0);
            Set(m, "pa_bos", i, st.BosBull || st.BosBear ? 1 : 0);
            Set(m, "pa_choch", i, st.ChochBull || st.ChochBear ? 1 : 0);
            Set(m, "pa_compress", i, sequences[i].CompressionRun >= 3 || st.RangeContraction ? 1 : 0);
            Set(m, "pa_sweep", i, 0);
            if (MicroSymbols.Contains(book.Symbol))
            {
                Set(m, "pa_flag", i, chartAt.Contains(i) ? 1 : 0);
                Set(m, "pa_hs", i, hsAt.Contains(i) ? 1 : 0);
                Set(m, "pa_wedge", i, wedgeAt.Contains(i) ? 1 : 0);
                Set(m, "pa_triangle", i, triAt.Contains(i) ? 1 : 0);
            }

            var r5 = m[IndexOf("ret_5"), i];
            var oi12 = m[IndexOf("oi_change_12"), i];
            var fund = m[IndexOf("funding_rate"), i];
            var taker = m[IndexOf("taker_imbalance"), i];
            var depth = m[IndexOf("depth_imbalance"), i];
            if (!float.IsNaN(r5) && !float.IsNaN(oi12))
            {
                Set(m, "ret5_x_oi", i, r5 * oi12);
            }

            if (!float.IsNaN(r5) && !float.IsNaN(fund))
            {
                Set(m, "ret5_x_funding", i, r5 * fund);
            }

            if (!float.IsNaN(r5) && !float.IsNaN(oi12) && !float.IsNaN(fund))
            {
                Set(m, "ret5_x_oi_x_funding", i, r5 * oi12 * fund);
            }

            if (!float.IsNaN(r5) && !float.IsNaN(vz))
            {
                Set(m, "ret5_x_volz", i, r5 * vz);
            }

            if (!float.IsNaN(vz) && !float.IsNaN(atrPctSeries[i]))
            {
                Set(m, "volz_x_atr", i, vz * atrPctSeries[i]);
            }

            if (!float.IsNaN(oi12) && !float.IsNaN(taker))
            {
                Set(m, "oi_x_taker", i, oi12 * taker);
            }

            var basis = m[IndexOf("basis"), i];
            if (!float.IsNaN(fund) && !float.IsNaN(basis))
            {
                Set(m, "funding_x_basis", i, fund * basis);
            }

            if (!float.IsNaN(depth) && !float.IsNaN(taker))
            {
                Set(m, "depth_x_taker", i, depth * taker);
            }

            var cs = m[IndexOf("breadth_pos"), i];
            if (!float.IsNaN(cs) && !float.IsNaN(oi12))
            {
                Set(m, "cs_rank_x_oi", i, cs * oi12);
            }

            if (!float.IsNaN(cs) && !float.IsNaN(fund))
            {
                Set(m, "cs_rank_x_funding", i, cs * fund);
            }
        }

        return m;
    }

    private static void SetRet(float[,] m, Book book, int i)
    {
        Set(m, "ret_1", i, Ret(book, i, 1));
        Set(m, "ret_3", i, Ret(book, i, 3));
        Set(m, "ret_5", i, Ret(book, i, 5));
        Set(m, "ret_10", i, Ret(book, i, 10));
        Set(m, "ret_20", i, Ret(book, i, 20));
        Set(m, "ret_50", i, Ret(book, i, 50));
        Set(m, "roc_10", i, Ret(book, i, 10));
        var a = Ret(book, i, 5);
        var b = Ret(book, i - 5, 5);
        if (!float.IsNaN(a) && !float.IsNaN(b))
        {
            Set(m, "mom_accel", i, a - b);
        }
    }

    private static void FillMicro(float[,] m, Book book, MicroSeries? series, int i, float price)
    {
        if (series is null)
        {
            return;
        }

        var close = book.OpenMs[i] + 3_600_000L - 1;
        var oi = series.OiAt(close, book.OpenMs[i]);
        var oi1 = series.OiLag(close, 1);
        var oi3 = series.OiLag(close, 3);
        var oi6 = series.OiLag(close, 6);
        var oi12 = series.OiLag(close, 12);
        var oi24 = series.OiLag(close, 24);
        Set(m, "oi_change_1", i, Chg(oi, oi1));
        Set(m, "oi_change_3", i, Chg(oi, oi3));
        Set(m, "oi_change_6", i, Chg(oi, oi6));
        Set(m, "oi_change_12", i, Chg(oi, oi12));
        Set(m, "oi_change_24", i, Chg(oi, oi24));
        Set(m, "oi_pct_change_12", i, Chg(oi, oi12));
        Set(m, "oi_z", i, series.OiZ(close));
        Set(m, "oi_pct", i, series.OiPct(close));
        var c1 = Chg(oi, oi1);
        var c2 = Chg(oi1, series.OiLag(close, 2));
        if (!float.IsNaN(c1) && !float.IsNaN(c2))
        {
            Set(m, "oi_accel", i, c1 - c2);
        }

        Set(m, "oi_vol", i, series.OiVol(close));
        if (!float.IsNaN(oi) && book.V[i] > 0)
        {
            Set(m, "oi_over_volume", i, oi / book.V[i]);
        }

        var ret = m[IndexOf("ret_1"), i];
        if (!float.IsNaN(ret) && !float.IsNaN(c1))
        {
            Set(m, "price_up_oi_up", i, ret > 0 && c1 > 0 ? 1 : 0);
            Set(m, "price_up_oi_down", i, ret > 0 && c1 < 0 ? 1 : 0);
            Set(m, "price_down_oi_up", i, ret < 0 && c1 > 0 ? 1 : 0);
            Set(m, "price_down_oi_down", i, ret < 0 && c1 < 0 ? 1 : 0);
            Set(m, "ret_x_oi", i, ret * c1);
        }

        var volChg = m[IndexOf("volume_change"), i];
        if (!float.IsNaN(c1) && !float.IsNaN(volChg))
        {
            Set(m, "oi_x_volume", i, c1 * volChg);
        }

        var rv = m[IndexOf("rv_24"), i];
        if (!float.IsNaN(c1) && !float.IsNaN(rv))
        {
            Set(m, "oi_x_vol", i, c1 * rv);
        }

        var fund = series.FundingAt(close);
        if (!float.IsNaN(c1) && !float.IsNaN(fund))
        {
            Set(m, "oi_x_funding", i, c1 * fund);
            Set(m, "funding_x_oi", i, fund * c1);
        }

        Set(m, "funding_rate", i, fund);
        Set(m, "funding_pct", i, series.FundingPct(close));
        Set(m, "funding_z", i, series.FundingZ(close));
        Set(m, "funding_change", i, series.FundingChange(close));
        Set(m, "funding_accel", i, series.FundingAccel(close));
        Set(m, "cum_funding", i, series.CumFunding(close, 21));
        Set(m, "funding_persistence", i, series.FundingPersistence(close));
        Set(m, "funding_extreme", i, series.FundingExtreme(close));
        Set(m, "funding_reversal", i, series.FundingReversal(close));
        if (!float.IsNaN(fund))
        {
            Set(m, "funding_x_price", i, fund * (price > 0 ? 1 : 0));
            var mom = m[IndexOf("ret_20"), i];
            if (!float.IsNaN(mom))
            {
                Set(m, "funding_x_mom", i, fund * mom);
            }
        }

        var basis = series.BasisAt(close);
        Set(m, "basis", i, basis);
        Set(m, "basis_change", i, series.BasisChange(close));
        Set(m, "basis_accel", i, series.BasisAccel(close));
        Set(m, "basis_pct", i, series.BasisPct(close));
        Set(m, "basis_z", i, series.BasisZ(close));
        var oi12chg = m[IndexOf("oi_change_12"), i];
        if (!float.IsNaN(basis) && !float.IsNaN(oi12chg))
        {
            Set(m, "basis_x_oi", i, basis * oi12chg);
        }

        if (!float.IsNaN(basis) && !float.IsNaN(fund))
        {
            Set(m, "basis_x_funding", i, basis * fund);
        }

        if (!float.IsNaN(basis))
        {
            Set(m, "basis_x_price", i, basis * retSafe(m, i));
        }

        if (book.TakerOk && book.V[i] > 0 && book.Tb[i] > 0 && book.Tb[i] <= book.V[i])
        {
            var ratio = book.Tb[i] / book.V[i];
            var imb = 2f * ratio - 1f;
            Set(m, "taker_buy_ratio", i, ratio);
            Set(m, "taker_imbalance", i, imb);
            if (i >= 1 && book.V[i - 1] > 0 && book.Tb[i - 1] > 0 && book.Tb[i - 1] <= book.V[i - 1])
            {
                var prev = 2f * (book.Tb[i - 1] / book.V[i - 1]) - 1f;
                Set(m, "taker_imbalance_change", i, imb - prev);
            }
        }

        Set(m, "taker_imbalance_pct", i, PercentileFeature(m, IndexOf("taker_imbalance"), i, 100));
        Set(m, "taker_imbalance_z", i, ZFeature(m, IndexOf("taker_imbalance"), i, 48));
        if (i >= 2 && !float.IsNaN(m[IndexOf("taker_imbalance_change"), i]) && !float.IsNaN(m[IndexOf("taker_imbalance_change"), i - 1]))
        {
            Set(m, "taker_imbalance_accel", i, m[IndexOf("taker_imbalance_change"), i] - m[IndexOf("taker_imbalance_change"), i - 1]);
        }

        var taker = m[IndexOf("taker_imbalance"), i];
        if (!float.IsNaN(taker) && !float.IsNaN(oi12))
        {
            Set(m, "taker_x_oi", i, taker * oi12);
        }

        if (!float.IsNaN(taker) && !float.IsNaN(fund))
        {
            Set(m, "taker_x_funding", i, taker * fund);
        }

        if (!float.IsNaN(taker) && !float.IsNaN(volChg))
        {
            Set(m, "taker_x_volume", i, taker * volChg);
        }

        if (!float.IsNaN(taker) && !float.IsNaN(ret))
        {
            Set(m, "taker_x_ret", i, taker * ret);
        }

        var depth = series.DepthAt(close, book.OpenMs[i]);
        Set(m, "depth_imbalance", i, depth);
        Set(m, "depth_imbalance_change", i, series.DepthChange(close));
        Set(m, "depth_imbalance_accel", i, series.DepthAccel(close));
        Set(m, "depth_pct", i, series.DepthPct(close));
        Set(m, "depth_z", i, series.DepthZ(close));
        if (!float.IsNaN(depth))
        {
            Set(m, "depth_ratio", i, (1f + depth) / Math.Max(1e-6f, 1f - depth));
        }
    }

    private static float retSafe(float[,] m, int i) => m[IndexOf("ret_1"), i];

    private static List<StatRow> Score(List<Episode> events, List<Episode> controls)
    {
        var rows = new List<StatRow>();
        var leadIndex = new Dictionary<int, int> { [24] = 0, [12] = 1, [8] = 2, [4] = 3, [2] = 4, [1] = 5, [0] = 6 };
        foreach (var thrName in KeyThresholds)
        {
            var thr = IndexOfThreshold(thrName);
            for (var fi = 0; fi < FeatureIds.Length; fi++)
            {
                foreach (var lead in Leads)
                {
                    var li = leadIndex[lead];
                    var packs = new Dictionary<string, (List<float> E, List<float> C)>(StringComparer.Ordinal);
                    void Add(string key, Episode ep, bool evt)
                    {
                        if (!packs.TryGetValue(key, out var pack))
                        {
                            pack = ([], []);
                            packs[key] = pack;
                        }

                        var v = ep.Feat[li][fi];
                        if (float.IsNaN(v))
                        {
                            return;
                        }

                        (evt ? pack.E : pack.C).Add(v);
                    }

                    foreach (var ep in events)
                    {
                        if (!ep.Hit[thr])
                        {
                            continue;
                        }

                        Add(ep.Split, ep, true);
                        Add("ALL", ep, true);
                        Add("B" + ep.Block, ep, true);
                        Add("SYM:" + ep.Symbol, ep, true);
                        Add(ep.VolBucket switch { 0 => "VOL_LOW", 2 => "VOL_HIGH", _ => "VOL_MID" }, ep, true);
                        Add(ep.BtcRegime switch { 1 => "BTC_UP", -1 => "BTC_DN", _ => "BTC_FLAT" }, ep, true);
                        Add("SPD:" + ep.Speed, ep, true);
                    }

                    foreach (var ct in controls)
                    {
                        if (ct.PrimaryThreshold != thr && !ct.Hit.Any(x => x))
                        {
                            if (Thresholds[ct.PrimaryThreshold].Dir != Thresholds[thr].Dir)
                            {
                                continue;
                            }

                            if (ct.ControlFor != thr && ct.PrimaryThreshold != thr)
                            {
                                var sameSide = Thresholds[ct.PrimaryThreshold].Dir == Thresholds[thr].Dir;
                                if (!sameSide)
                                {
                                    continue;
                                }
                            }
                        }

                        if (Thresholds[ct.PrimaryThreshold].Dir != Thresholds[thr].Dir)
                        {
                            continue;
                        }

                        Add(ct.Split, ct, false);
                        Add("ALL", ct, false);
                        Add("B" + ct.Block, ct, false);
                        Add("SYM:" + ct.Symbol, ct, false);
                        Add(ct.VolBucket switch { 0 => "VOL_LOW", 2 => "VOL_HIGH", _ => "VOL_MID" }, ct, false);
                        Add(ct.BtcRegime switch { 1 => "BTC_UP", -1 => "BTC_DN", _ => "BTC_FLAT" }, ct, false);
                        Add("SPD:" + ct.Speed, ct, false);
                    }

                    if (!packs.TryGetValue("IS", out var isPack) || isPack.E.Count < 30 || isPack.C.Count < 30)
                    {
                        rows.Add(StatRow.Insufficient(FeatureIds[fi], thrName, lead));
                        continue;
                    }

                    var orient = Median(isPack.E) >= Median(isPack.C) ? 1 : -1;
                    StatSlice Slice(string key)
                    {
                        if (!packs.TryGetValue(key, out var p) || p.E.Count < 30 || p.C.Count < 30)
                        {
                            return StatSlice.None();
                        }

                        return StatSlice.Compute(p.E, p.C, orient);
                    }

                    var symbols = packs.Keys.Where(k => k.StartsWith("SYM:", StringComparison.Ordinal)).ToList();
                    var pos = 0;
                    var neg = 0;
                    double sumEff = 0;
                    var symN = 0;
                    double best = double.NegativeInfinity;
                    double worst = double.PositiveInfinity;
                    foreach (var key in symbols)
                    {
                        var p = packs[key];
                        if (p.E.Count < 15 || p.C.Count < 15)
                        {
                            continue;
                        }

                        var d = (Median(p.E) - Median(p.C)) * orient;
                        symN++;
                        sumEff += d;
                        if (d > 0)
                        {
                            pos++;
                        }
                        else if (d < 0)
                        {
                            neg++;
                        }

                        best = Math.Max(best, d);
                        worst = Math.Min(worst, d);
                    }

                    var blockSigns = new int[4];
                    for (var b = 0; b < 4; b++)
                    {
                        if (!packs.TryGetValue("B" + b, out var p) || p.E.Count < 20 || p.C.Count < 20)
                        {
                            blockSigns[b] = 0;
                            continue;
                        }

                        var d = Median(p.E) - Median(p.C);
                        blockSigns[b] = Math.Sign(d) == orient ? 1 : Math.Sign(d) == 0 ? 0 : -1;
                    }

                    rows.Add(new StatRow
                    {
                        Feature = FeatureIds[fi],
                        Event = thrName,
                        Direction = Thresholds[thr].Dir > 0 ? "UP" : "DOWN",
                        LeadHours = lead,
                        Orient = orient,
                        Is = Slice("IS"),
                        Val = Slice("VAL"),
                        Oos = Slice("OOS"),
                        All = Slice("ALL"),
                        Symbols = symN,
                        SymbolsPos = pos,
                        SymbolsNeg = neg,
                        SymbolMean = symN == 0 ? double.NaN : sumEff / symN,
                        Best = symN == 0 ? double.NaN : best,
                        Worst = symN == 0 ? double.NaN : worst,
                        BlockSame = blockSigns.Count(x => x > 0),
                        BlockKnown = blockSigns.Count(x => x != 0),
                        VolLow = Slice("VOL_LOW"),
                        VolHigh = Slice("VOL_HIGH"),
                        DuringNote = lead
                    });
                }
            }
        }

        return rows;
    }

    private static List<Labeled> LabelRows(List<StatRow> rows)
    {
        var primary = rows.Where(r => r.LeadHours == 1 && r.Is.NEvent >= 30).ToList();
        var pvals = primary.Select(r => r.Is.PValue).ToList();
        var reject = BenjaminiHochberg(pvals, 0.10);
        var list = new List<Labeled>(rows.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            list.Add(new Labeled { Row = rows[i], Label = "SEE_PRIMARY", BhReject = false });
        }

        for (var i = 0; i < primary.Count; i++)
        {
            var r = primary[i];
            var label = Classify(r, reject[i]);
            var idx = rows.IndexOf(r);
            list[idx] = new Labeled { Row = r, Label = label, BhReject = reject[i] };
        }

        return list;
    }

    private static string Classify(StatRow r, bool bh)
    {
        if (r.Is.NEvent < 30 || r.Val.Missing || r.Oos.Missing)
        {
            return r.Oos.NEvent >= 30 && (r.Is.Missing || r.Is.Effect * r.Orient <= 0) && r.Oos.Effect * r.Orient > 0
                ? "OOS_ONLY"
                : "NO_EVIDENCE";
        }

        var isDir = Math.Sign(r.Is.Effect) == r.Orient || r.Is.Auc >= 0.53;
        var valDir = !r.Val.Missing && r.Val.Auc >= 0.52 && Math.Sign(r.Val.Effect) == Math.Sign(r.Is.Effect);
        var oosDir = !r.Oos.Missing && r.Oos.Auc >= 0.52 && Math.Sign(r.Oos.Effect) == Math.Sign(r.Is.Effect);
        if (!isDir)
        {
            return "NO_EVIDENCE";
        }

        if (oosDir && !valDir && r.Is.Auc < 0.53)
        {
            return "OOS_ONLY";
        }

        if (!(valDir && oosDir))
        {
            return "UNSTABLE";
        }

        if (r.BlockSame < 3 || r.Symbols < 5 || r.SymbolsPos < 3)
        {
            if (r.Symbols <= 2 && r.Symbols > 0)
            {
                return "SYMBOL_SPECIFIC";
            }

            var volDisagree = !r.VolLow.Missing && !r.VolHigh.Missing
                && Math.Sign(r.VolLow.Effect) != 0
                && Math.Sign(r.VolHigh.Effect) != 0
                && Math.Sign(r.VolLow.Effect) != Math.Sign(r.VolHigh.Effect);
            if (volDisagree)
            {
                return "REGIME_SPECIFIC";
            }

            return "UNSTABLE";
        }

        var familyOnly = r.Feature.StartsWith("pa_", StringComparison.Ordinal) && r.SymbolsPos < r.Symbols * 0.6;
        if (!bh)
        {
            return familyOnly ? "FAMILY_SPECIFIC" : "UNSTABLE";
        }

        var siblings = KeyThresholds.Count(name => name != r.Event && name.StartsWith(r.Direction == "UP" ? "UP" : "DOWN", StringComparison.Ordinal));
        if (siblings < 1)
        {
            return "UNSTABLE";
        }

        return "REPEATABLE";
    }

    private static ModelReport FitModels(List<Episode> events, List<Episode> controls)
    {
        var report = new ModelReport();
        FitOne(report, "UNIVERSE", UniverseModel, events, controls, _ => true);
        FitOne(report, "MICRO", MicroModel.Concat(UniverseModel).Distinct().ToArray(), events, controls, e => e.Micro);
        return report;
    }

    private static void FitOne(ModelReport report, string name, string[] names, List<Episode> events, List<Episode> controls, Func<Episode, bool> keep)
    {
        var idx = names.Select(IndexOf).ToArray();
        var li = 5;
        var trainX = new List<float[]>();
        var trainY = new List<int>();
        var val = new List<(float[] X, int Y)>();
        var oos = new List<(float[] X, int Y)>();
        void Push(Episode ep, int y)
        {
            if (!keep(ep))
            {
                return;
            }

            var row = new float[idx.Length];
            var missing = 0;
            for (var i = 0; i < idx.Length; i++)
            {
                var v = ep.Feat[li][idx[i]];
                if (float.IsNaN(v))
                {
                    missing++;
                    row[i] = float.NaN;
                }
                else
                {
                    row[i] = v;
                }
            }

            if (missing > idx.Length / 2)
            {
                return;
            }

            var target = ep.Split == "IS" ? trainX : null;
            if (ep.Split == "IS")
            {
                trainX.Add(row);
                trainY.Add(y);
            }
            else if (ep.Split == "VAL")
            {
                val.Add((row, y));
            }
            else
            {
                oos.Add((row, y));
            }

            _ = target;
        }

        foreach (var ep in events.Where(e => e.Hit[IndexOfThreshold("UP_30")] || e.Hit[IndexOfThreshold("DOWN_30")]))
        {
            Push(ep, 1);
        }

        foreach (var ep in controls.Where(e => e.PrimaryThreshold == IndexOfThreshold("UP_30") || e.PrimaryThreshold == IndexOfThreshold("DOWN_30")))
        {
            Push(ep, 0);
        }

        if (trainY.Count < 80 || trainY.Count(y => y == 1) < 30)
        {
            report.Lines.Add($"{name}: INSUFFICIENT_DATA for logistic/tree (IS n={trainY.Count}).");
            return;
        }

        var mean = new double[idx.Length];
        var sd = new double[idx.Length];
        for (var j = 0; j < idx.Length; j++)
        {
            var vals = trainX.Select(r => r[j]).Where(v => !float.IsNaN(v)).Select(v => (double)v).ToArray();
            mean[j] = vals.Length == 0 ? 0 : vals.Average();
            var variance = vals.Length < 2 ? 1 : vals.Select(v => (v - mean[j]) * (v - mean[j])).Average();
            sd[j] = Math.Sqrt(variance);
            if (sd[j] < 1e-12)
            {
                sd[j] = 1;
            }
        }

        float[] Z(float[] row)
        {
            var z = new float[row.Length];
            for (var j = 0; j < row.Length; j++)
            {
                var v = float.IsNaN(row[j]) ? mean[j] : row[j];
                z[j] = (float)((v - mean[j]) / sd[j]);
            }

            return z;
        }

        var zx = trainX.Select(Z).ToList();
        var w = new double[idx.Length];
        var b = 0.0;
        for (var epoch = 0; epoch < 80; epoch++)
        {
            for (var n = 0; n < zx.Count; n++)
            {
                var p = Sigmoid(Dot(w, zx[n]) + b);
                var err = p - trainY[n];
                for (var j = 0; j < w.Length; j++)
                {
                    w[j] -= 0.05 * err * zx[n][j];
                }

                b -= 0.05 * err;
            }
        }

        double AucOf(List<(float[] X, int Y)> sample)
        {
            if (sample.Count < 40 || sample.All(s => s.Y == 0) || sample.All(s => s.Y == 1))
            {
                return double.NaN;
            }

            var scores = sample.Select(s => Dot(w, Z(s.X)) + b).ToArray();
            var y = sample.Select(s => s.Y).ToArray();
            return Auc(scores, y);
        }

        var isAuc = Auc(zx.Select(r => Dot(w, r) + b).ToArray(), trainY.ToArray());
        var valAuc = AucOf(val);
        var oosAuc = AucOf(oos);
        var equalIs = EqualAuc(zx, trainY);
        var rankIs = RankAuc(trainX, trainY);
        report.Lines.Add($"{name} T-1h frozen features {idx.Length}. IS n={trainY.Count} events={trainY.Count(y => y == 1)}.");
        report.Lines.Add($"{name} A equal-weight IS AUC {Fmt(equalIs)}. B rank IS AUC {Fmt(rankIs)}. C logistic IS {Fmt(isAuc)} VAL {Fmt(valAuc)} OOS {Fmt(oosAuc)}. D depth-2 stump IS {Fmt(StumpAuc(zx, trainY))}.");
        report.LogisticOos = double.IsNaN(oosAuc) ? report.LogisticOos : oosAuc;
        report.LogisticVal = double.IsNaN(valAuc) ? report.LogisticVal : valAuc;
        report.LogisticIs = isAuc;
        if (name == "UNIVERSE")
        {
            report.Weights = w;
            report.Bias = b;
            report.Means = mean;
            report.Sds = sd;
            report.Idx = idx;
        }
    }

    private static CostReport CostStress(List<Episode> events, List<Episode> controls, ModelReport models)
    {
        var report = new CostReport();
        if (models.Idx is null || models.Weights is null)
        {
            report.Lines.Add("Cost stress: INSUFFICIENT_DATA (universe model was not fit).");
            return report;
        }

        var baseCost = 0.0004 + 0.0002;
        var roundTrip = baseCost * 2;
        foreach (var stress in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
        {
            foreach (var split in new[] { "IS", "VAL", "OOS" })
            {
                var pnls = new List<double>();
                var hits = 0;
                var fires = 0;
                foreach (var ep in events.Where(e => e.Split == split))
                {
                    if (!Fire(ep, models))
                    {
                        continue;
                    }

                    fires++;
                    var gross = (double)ep.MaxExc * 0.5;
                    if (ep.Dir < 0)
                    {
                        gross = Math.Abs(gross);
                    }

                    pnls.Add(gross - roundTrip * stress);
                    hits++;
                }

                foreach (var ct in controls.Where(c => c.Split == split))
                {
                    if (!Fire(ct, models))
                    {
                        continue;
                    }

                    fires++;
                    pnls.Add(-roundTrip * stress);
                }

                var mean = pnls.Count == 0 ? double.NaN : pnls.Average();
                report.Lines.Add($"COST {stress:0.##}x {split} fires={fires} eventFires={hits} meanNet={Fmt(mean)}");
                if (split == "OOS" && Math.Abs(stress - 1) < 0.01)
                {
                    report.OosBase = mean;
                }

                if (split == "OOS" && Math.Abs(stress - 2) < 0.01)
                {
                    report.Oos2x = mean;
                }
            }
        }

        report.Lines.Add("Cost model is Model B taker fee 0.04% plus slippage 0.02% per fill, round trip, stressed. Entry is the T-1h research score, not an order. Exit proxy is half the labeled maximum excursion for events and flat for controls. This is not a trading backtest and is not an edge.");
        return report;
    }

    private static bool Fire(Episode ep, ModelReport models)
    {
        var row = new float[models.Idx!.Length];
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = ep.Feat[5][models.Idx[i]];
        }

        double s = models.Bias;
        var known = 0;
        for (var j = 0; j < row.Length; j++)
        {
            if (float.IsNaN(row[j]))
            {
                continue;
            }

            known++;
            s += models.Weights![j] * ((row[j] - models.Means![j]) / models.Sds![j]);
        }

        return known >= row.Length / 2 && s > 0;
    }

    private static ShortHorizon RunShortHorizon(string cacheDir, MicroCache micro)
    {
        var text = new StringBuilder();
        text.AppendLine("Short-horizon scan uses the 10 coins that already have Vision metrics. 5m and 15m files already on disk. No download.");
        foreach (var tf in new[] { "15m", "5m" })
        {
            var minutes = tf == "5m" ? 5 : 15;
            var horizonBars = 24 * 60 / minutes;
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var id in KeyThresholds)
            {
                counts[id] = 0;
            }

            var symbols = 0;
            foreach (var symbol in MicroSymbols)
            {
                var path = Path.Combine(cacheDir, $"{symbol}_{tf}.json");
                if (!File.Exists(path))
                {
                    text.AppendLine($"{symbol} {tf}: DATA_UNAVAILABLE");
                    continue;
                }

                var book = ReadBook(path, symbol);
                symbols++;
                var i = 100;
                while (i < book.N - horizonBars - 1)
                {
                    var tUp = FirstHitBars(book, i, 0.30, 1, horizonBars);
                    var tDn = FirstHitBars(book, i, -0.30, -1, horizonBars);
                    if (tUp < 0 && tDn < 0)
                    {
                        i++;
                        continue;
                    }

                    var dir = tDn >= 0 && (tUp < 0 || tDn < tUp) ? -1 : 1;
                    var close = book.C[i];
                    var maxBar = i + 1;
                    foreach (var thr in Thresholds)
                    {
                        if (thr.Dir != dir || !KeyThresholds.Contains(thr.Id))
                        {
                            continue;
                        }

                        if (FirstHitBars(book, i, thr.Level, dir, horizonBars) >= 0)
                        {
                            counts[thr.Id]++;
                        }
                    }

                    for (var j = 1; j <= horizonBars; j++)
                    {
                        var exc = dir > 0 ? book.H[i + j] / close - 1f : book.L[i + j] / close - 1f;
                        if (dir > 0 ? exc > (dir > 0 ? book.H[maxBar] / close - 1f : 0) : exc < book.L[maxBar] / close - 1f)
                        {
                            maxBar = i + j;
                        }
                    }

                    i = maxBar + horizonBars;
                    _ = micro;
                }
            }

            text.AppendLine($"{tf} symbols={symbols} horizon={horizonBars} bars. Nested key-threshold hits (clustered by the same 24h separation):");
            foreach (var id in KeyThresholds)
            {
                text.AppendLine($"- {id}: {counts[id]}");
            }

            text.AppendLine(tf == "5m"
                ? "T-5m and T-1m leads exist only on this 5m clock. They are counted here and are not mixed into the 1h effect table."
                : "T-15m and T-30m leads exist on this 15m clock. The 1h table does not pretend to observe them.");
        }

        text.AppendLine("Sub-hour feature lead/lag on the full 529-coin universe is DATA_UNAVAILABLE because that study clock is 1h.");
        return new ShortHorizon { Text = text.ToString() };
    }

    private static int FirstHitBars(Book book, int i, double level, int dir, int horizon)
    {
        var close = book.C[i];
        if (close <= 0)
        {
            return -1;
        }

        for (var j = 1; j <= horizon && i + j < book.N; j++)
        {
            var exc = dir > 0 ? book.H[i + j] / close - 1.0 : book.L[i + j] / close - 1.0;
            if (dir > 0 ? exc >= level : exc <= level)
            {
                return j;
            }
        }

        return -1;
    }

    private static string LookaheadCheck(Book book)
    {
        var full = Rsi(book.C, 14);
        var cut = book.N - 5;
        var trunc = new float[cut];
        Array.Copy(book.C, trunc, cut);
        var part = Rsi(trunc, 14);
        var mismatch = 0;
        for (var i = 30; i < cut; i++)
        {
            if (float.IsNaN(full[i]) && float.IsNaN(part[i]))
            {
                continue;
            }

            if (Math.Abs(full[i] - part[i]) > 1e-4)
            {
                mismatch++;
            }
        }

        var retFull = book.C[cut - 1] / book.C[cut - 2] - 1f;
        var retPart = trunc[cut - 1] / trunc[cut - 2] - 1f;
        return mismatch == 0 && Math.Abs(retFull - retPart) < 1e-6
            ? $"PASS BTCUSDT 1h RSI(14) and ret_1 unchanged at T when 5 future bars are withheld. mismatches={mismatch}"
            : $"FAIL mismatches={mismatch}";
    }

    private static void WriteReports(string root, StudyContext ctx)
    {
        var docs = Path.Combine(root, "docs");
        Directory.CreateDirectory(docs);
        File.WriteAllText(Path.Combine(docs, "EXTREME_MOVE_FEATURE_CATALOG.md"), Catalog(ctx));
        File.WriteAllText(Path.Combine(docs, "EXTREME_MOVE_EVENT_STUDY.md"), EventStudy(ctx));
        File.WriteAllText(Path.Combine(docs, "EXTREME_MOVE_OI_RESEARCH.md"), OiReport(ctx));
        File.WriteAllText(Path.Combine(docs, "EXTREME_MOVE_MICROSTRUCTURE_RESEARCH.md"), MicroReport(ctx));
        File.WriteAllText(Path.Combine(docs, "EXTREME_MOVE_VALIDATION.md"), ValidationReport(ctx));
        File.WriteAllText(Path.Combine(docs, "EXTREME_MOVE_RESEARCH.md"), MainReport(ctx));
        Console.WriteLine("Wrote docs/EXTREME_MOVE_*.md");
    }

    private static string Catalog(StudyContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme move feature catalog");
        sb.AppendLine();
        sb.AppendLine("Research catalog only. No feature here is a trading signal. No feature is an edge.");
        sb.AppendLine();
        sb.AppendLine($"feature_manifest_hash `{ctx.FeatureHash}`");
        sb.AppendLine();
        sb.AppendLine("Timestamp alignment: the feature at bar T uses the closed 1h bar and earlier closed bars only. Future bars are used only to label the event. Missing prints stay null. They are not filled with zero and they are not forward-filled.");
        sb.AppendLine();
        sb.AppendLine("| FeatureId | Family | Description | Formula | Required data | Lookback | Alignment | Causality | Availability | Research status |");
        sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |");
        foreach (var id in FeatureIds)
        {
            var family = FamilyOf(id);
            var avail = Availability(id);
            sb.AppendLine($"| {id} | {family} | {Describe(id)} | {Formula(id)} | {Need(id)} | {LookbackOf(id)} | closed 1h bar | causal | {avail} | RESEARCHING |");
        }

        sb.AppendLine();
        sb.AppendLine("## Not computed");
        sb.AppendLine();
        sb.AppendLine("- Liquidations: DATA_UNAVAILABLE. Binance Vision liquidation snapshots are not on disk. They were not reconstructed from price.");
        sb.AppendLine("- Best bid/ask spread: DATA_UNAVAILABLE. Book-depth files are ±1% notional imbalance, not a spread.");
        sb.AppendLine("- NR4, NR7: NOT_IMPLEMENTED in the existing price-action engine. A second pattern engine was not added.");
        sb.AppendLine("- Cup and handle: existing engine marks it not implemented.");
        sb.AppendLine("- Exchange volume-profile snapshots: DATA_UNAVAILABLE. `poc_dist` is a causal 24-bar close-bin profile built from OHLCV, not an exchange profile.");
        sb.AppendLine("- REST open-interest history (~30 days) was not used. Open interest is the Vision 5m `sum_open_interest` series, last print inside the closed hour.");
        sb.AppendLine();
        sb.AppendLine(ctx.Safety.Line);
        return sb.ToString();
    }

    private static string EventStudy(StudyContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme move event study");
        sb.AppendLine();
        sb.AppendLine("This is an event study. A feature association is not a trading edge.");
        sb.AppendLine();
        sb.AppendLine($"event_definition_hash `{ctx.EventHash}`");
        sb.AppendLine($"experiment_manifest_hash `{ctx.ExperimentHash}`");
        sb.AppendLine();
        sb.AppendLine("## Label");
        sb.AppendLine();
        sb.AppendLine("Clock: closed 1h bars. Horizon: 24 closed hours after the signal bar. UP excursion is future high / signal close - 1. DOWN excursion is future low / signal close - 1. The signal bar itself is not in the future window.");
        sb.AppendLine();
        sb.AppendLine("## Separation rule (frozen before this run)");
        sb.AppendLine();
        sb.AppendLine("An independent episode starts at the first bar where +10% or -10% is reached inside 24h, after warmup. Direction is whichever 10% threshold is touched first. On a tie, the larger absolute excursion wins. The episode then records every nested threshold hit by that same path, the time to each threshold, the maximum excursion, and the bar of that maximum. The next independent episode on that coin cannot start until 24 hours after the maximum-excursion bar. This 24h gap was not tuned on OOS.");
        sb.AppendLine();
        sb.AppendLine("NESTED_EVENT counts every threshold hit inside an episode. INDEPENDENT_EVENT counts the episode once, at its highest threshold.");
        sb.AppendLine();
        sb.AppendLine("| Threshold | Nested hits | Independent episodes at that highest threshold |");
        sb.AppendLine("| --- | ---: | ---: |");
        for (var i = 0; i < Thresholds.Length; i++)
        {
            sb.AppendLine($"| {Thresholds[i].Id} | {ctx.Nested[i]} | {ctx.Independent[i]} |");
        }

        sb.AppendLine();
        sb.AppendLine($"Symbols in the 1h study: {ctx.Symbols}. Symbols skipped for short history (<{MinBars} bars): {ctx.Skipped}.");
        sb.AppendLine($"Matched controls: {ctx.Controls.Count}. A control is the same coin, same IS/VAL/OOS window, same trailing ATR-percentile bucket, and outside every episode exclusion zone. Unmatched episodes are omitted, not filled.");
        sb.AppendLine();
        sb.AppendLine("## Speed (frozen)");
        sb.AppendLine();
        sb.AppendLine("On the 1h clock, FAST means the primary threshold is reached in 1 hour or less. MEDIUM means 2 to 4 hours. SLOW means more than 4 hours. The 30-minute FAST cut cannot be observed on 1h bars. The 5m/15m section is the place that cut can be discussed, and only for the 10-coin set.");
        sb.AppendLine();
        sb.AppendLine("| Speed | Episodes |");
        sb.AppendLine("| --- | ---: |");
        foreach (var speed in new[] { "FAST", "MEDIUM", "SLOW" })
        {
            sb.AppendLine($"| {speed} | {ctx.Episodes.Count(e => e.Speed == speed)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Splits (frozen from the known BTC cache window, not from results)");
        sb.AppendLine();
        sb.AppendLine("- IS: bar open before 2025-09-23");
        sb.AppendLine("- VAL: 2025-09-23 inclusive to 2026-03-23 exclusive");
        sb.AppendLine("- OOS: 2026-03-23 inclusive onward");
        sb.AppendLine("- Blocks: before 2025-03-18, before 2025-09-18, before 2026-03-18, and after");
        sb.AppendLine();
        sb.AppendLine("## Self-check");
        sb.AppendLine();
        sb.AppendLine(ctx.SelfCheck);
        sb.AppendLine();
        sb.AppendLine("## No-lookahead");
        sb.AppendLine();
        sb.AppendLine(ctx.Lookahead);
        sb.AppendLine();
        sb.AppendLine("## Short horizon");
        sb.AppendLine();
        sb.AppendLine(ctx.Short.Text);
        sb.AppendLine();
        sb.AppendLine(ctx.Safety.Line);
        return sb.ToString();
    }

    private static string OiReport(StudyContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme move open-interest research");
        sb.AppendLine();
        sb.AppendLine("Open interest is a research family. A state such as price up and OI up is not a signal.");
        sb.AppendLine();
        sb.AppendLine($"Vision metrics on disk for {ctx.Micro.Symbols.Count} coins. A 1h feature uses the last 5m print whose create_time falls inside that closed hour. Hours without a print are null.");
        sb.AppendLine();
        sb.AppendLine(TableFor(ctx, id => id.StartsWith("oi_", StringComparison.Ordinal) || id is "ret_x_oi" or "ret5_x_oi" or "price_up_oi_up" or "price_up_oi_down" or "price_down_oi_up" or "price_down_oi_down"));
        sb.AppendLine();
        sb.AppendLine("Sample sizes below 30 in a split are INSUFFICIENT_DATA. Effects are median(event) - median(control) at T-1h, oriented on IS.");
        sb.AppendLine();
        sb.AppendLine(ctx.Safety.Line);
        return sb.ToString();
    }

    private static string MicroReport(StudyContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme move microstructure research");
        sb.AppendLine();
        sb.AppendLine("Funding, basis, taker flow, and ±1% depth. Liquidations and spread are DATA_UNAVAILABLE.");
        sb.AppendLine();
        sb.AppendLine($"Taker buy volume is used only when a coin's 1h cache has taker buy > 0 and <= volume on most bars. Coins passing that check: {ctx.TakerSymbols}. Stored zeros are not flow.");
        sb.AppendLine("Depth imbalance is on disk for BTCUSDT, ETHUSDT, and BNBUSDT only. Other coins are DATA_UNAVAILABLE for depth.");
        sb.AppendLine();
        sb.AppendLine(TableFor(ctx, id => id.StartsWith("funding", StringComparison.Ordinal) || id.StartsWith("basis", StringComparison.Ordinal) || id.StartsWith("taker", StringComparison.Ordinal) || id.StartsWith("depth", StringComparison.Ordinal) || id is "oi_x_taker" or "funding_x_basis" or "depth_x_taker"));
        sb.AppendLine();
        sb.AppendLine("Whether a feature moves before, at, or after the excursion is judged by comparing T-4h, T-1h, T0, and the mid-path bar. T0 is still before the future window. The mid-path bar is during the move and is not a precursor.");
        sb.AppendLine();
        sb.AppendLine(ctx.Safety.Line);
        return sb.ToString();
    }

    private static string ValidationReport(StudyContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme move validation");
        sb.AppendLine();
        sb.AppendLine("Chronological IS / VAL / OOS. No random split. No OOS feature selection. No OOS threshold search.");
        sb.AppendLine();
        sb.AppendLine($"Hypotheses in the primary family: feature × key threshold × T-1h. Count = {ctx.Labels.Count(l => l.Row.LeadHours == 1)}.");
        sb.AppendLine($"Features in the registry: {FeatureIds.Length}. Key thresholds: {KeyThresholds.Length}. Leads: {Leads.Length}. Interactions are inside the registry, not an extra search.");
        sb.AppendLine("Benjamini-Hochberg FDR 0.10 is applied to primary-family IS p-values (normal approximation of the Mann-Whitney statistic). Raw and corrected calls are both stored. A small p-value is not an edge.");
        sb.AppendLine();
        sb.AppendLine("## Repeatable rule (frozen)");
        sb.AppendLine();
        sb.AppendLine("REPEATABLE requires the same effect sign on IS, VAL, and OOS, AUC at least 0.52 on VAL and OOS, at least 3 chronological blocks agreeing, at least 5 coins with 3 positive, BH rejection on the IS test, and more than one threshold existing in the family. These labels are research labels.");
        sb.AppendLine();
        sb.AppendLine("## Models");
        sb.AppendLine();
        foreach (var line in ctx.Models.Lines)
        {
            sb.AppendLine("- " + line);
        }

        sb.AppendLine();
        sb.AppendLine("Model C logistic weights are fit on IS only. VAL and OOS are scored once. Model D is a depth-2 stump on the same frozen, IS-standardized matrix. IS-mean imputation is used only inside the model matrix when fewer than half the features are missing. Effect-size rows never impute.");
        sb.AppendLine();
        sb.AppendLine("## Cost stress");
        sb.AppendLine();
        foreach (var line in ctx.Costs.Lines)
        {
            sb.AppendLine("- " + line);
        }

        sb.AppendLine();
        sb.AppendLine("## Primary labels at T-1h");
        sb.AppendLine();
        sb.AppendLine("| Feature | Event | Label | BH | IS AUC | VAL AUC | OOS AUC | IS n |");
        sb.AppendLine("| --- | --- | --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var item in ctx.Labels.Where(l => l.Row.LeadHours == 1 && l.Label != "SEE_PRIMARY").OrderBy(l => l.Row.Event).ThenBy(l => l.Row.Feature))
        {
            if (item.Label is "NO_EVIDENCE" && item.Row.Is.Auc < 0.55)
            {
                continue;
            }

            sb.AppendLine($"| {item.Row.Feature} | {item.Row.Event} | {item.Label} | {(item.BhReject ? "reject" : "not rejected")} | {Fmt(item.Row.Is.Auc)} | {Fmt(item.Row.Val.Auc)} | {Fmt(item.Row.Oos.Auc)} | {item.Row.Is.NEvent} |");
        }

        sb.AppendLine();
        sb.AppendLine(ctx.Safety.Line);
        return sb.ToString();
    }

    private static string MainReport(StudyContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Extreme move / pump and crash precursor research");
        sb.AppendLine();
        sb.AppendLine("Research only. Nothing in this document is a trading strategy, a Paper bot, or an edge.");
        sb.AppendLine();
        sb.AppendLine("Correlation with a future return was not the success test. Each row is an event-versus-matched-control comparison at a fixed lead.");
        sb.AppendLine();
        sb.AppendLine($"feature_manifest_hash `{ctx.FeatureHash}`");
        sb.AppendLine($"event_definition_hash `{ctx.EventHash}`");
        sb.AppendLine($"experiment_manifest_hash `{ctx.ExperimentHash}`");
        sb.AppendLine();
        sb.AppendLine("## Final table");
        sb.AppendLine();
        sb.AppendLine("Rows are T-1h primary tests with a research label other than a weak NO_EVIDENCE. The full primary set is in the validation report. Features are not ranked as tradable.");
        sb.AppendLine();
        sb.AppendLine("| Feature | Family | Event | Direction | Lead Time | IS Effect | VAL Effect | OOS Effect | AUC | Precision | Recall | False Positive Rate | Symbol Coverage | Block Stability | Regime Stability | Multiple Testing Status | Cost Robustness | Final Research Label |");
        sb.AppendLine("| --- | --- | --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- | --- | --- | --- | --- |");
        var shown = 0;
        foreach (var item in ctx.Labels.Where(l => l.Row.LeadHours == 1 && l.Label is not "SEE_PRIMARY" and not "NO_EVIDENCE"))
        {
            var r = item.Row;
            sb.AppendLine($"| {r.Feature} | {FamilyOf(r.Feature)} | {r.Event} | {r.Direction} | T-{r.LeadHours}h | {Fmt(r.Is.Effect)} | {Fmt(r.Val.Effect)} | {Fmt(r.Oos.Effect)} | {Fmt(r.Oos.Auc)} | {Fmt(r.Oos.Precision)} | {Fmt(r.Oos.Recall)} | {Fmt(r.Oos.Fpr)} | {r.Symbols} ({r.SymbolsPos}+/{r.SymbolsNeg}-) | {r.BlockSame}/{r.BlockKnown} | {RegimeText(r)} | {(item.BhReject ? "BH reject" : "BH not rejected")} | {CostText(ctx)} | {item.Label} |");
            shown++;
        }

        if (shown == 0)
        {
            sb.AppendLine("| — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | — | NO_EVIDENCE |");
        }

        sb.AppendLine();
        sb.AppendLine("AUC, precision, recall, and false-positive rate in this table are the OOS figures. The IS decile cut is frozen from IS and applied to OOS. Precision is the share of the top oriented decile that are events.");
        sb.AppendLine();
        sb.AppendLine("## Answers");
        sb.AppendLine();
        foreach (var thr in KeyThresholds)
        {
            var hits = ctx.Labels.Where(l => l.Row.LeadHours == 1 && l.Row.Event == thr && l.Label is "REPEATABLE" or "FAMILY_SPECIFIC" or "SYMBOL_SPECIFIC" or "REGIME_SPECIFIC").Select(l => $"{l.Row.Feature} ({l.Label}, lead T-1h, IS AUC {Fmt(l.Row.Is.Auc)})").Take(12).ToList();
            sb.AppendLine($"**Before {thr}:** {(hits.Count == 0 ? "No feature met the repeatability rule at T-1h. See NO_EVIDENCE and UNSTABLE rows in the validation report. Association, when present, is not early warning and is not an edge." : string.Join("; ", hits))}");
            sb.AppendLine();
        }

        sb.AppendLine("**How early:** On the 1h clock the earliest scored leads are T-24h, T-12h, T-8h, T-4h, T-2h, and T-1h. T-30m through T-1m are not observed on that clock. The 5m/15m counts for the 10-coin set are in the event-study report. A feature that differs only on the mid-path bar is during the move, not a precursor.");
        sb.AppendLine();
        sb.AppendLine(FamilyAnswer(ctx, "oi_", "Is OI useful?"));
        sb.AppendLine(FamilyAnswer(ctx, "funding", "Is funding useful?"));
        sb.AppendLine(FamilyAnswer(ctx, "basis", "Is basis useful?"));
        sb.AppendLine(FamilyAnswer(ctx, "taker", "Is taker flow useful?"));
        sb.AppendLine(FamilyAnswer(ctx, "depth", "Is depth useful?"));
        sb.AppendLine(FamilyAnswer(ctx, "volume", "Is volume useful?"));
        sb.AppendLine("**Is volatility useful?** " + FamilySentence(ctx, id => id is "atr_pct" or "rv_24" or "bb_width" or "vol_compression" or "parkinson_24"));
        sb.AppendLine();
        sb.AppendLine("**Is price action useful?** " + FamilySentence(ctx, id => id.StartsWith("pa_", StringComparison.Ordinal)));
        sb.AppendLine();
        var repeatable = ctx.Labels.Where(l => l.Label == "REPEATABLE").Select(l => l.Row.Feature + " @ " + l.Row.Event).Distinct().ToList();
        sb.AppendLine($"**Repeatable combinations:** {(repeatable.Count == 0 ? "None under the frozen rule." : string.Join("; ", repeatable))}.");
        sb.AppendLine();
        var oosOnly = ctx.Labels.Where(l => l.Label == "OOS_ONLY").Select(l => l.Row.Feature + " @ " + l.Row.Event).Distinct().Take(20).ToList();
        sb.AppendLine($"**OOS-only relationships:** {(oosOnly.Count == 0 ? "None flagged." : string.Join("; ", oosOnly))}.");
        sb.AppendLine();
        var sym = ctx.Labels.Where(l => l.Label == "SYMBOL_SPECIFIC").Select(l => l.Row.Feature + " @ " + l.Row.Event).Distinct().Take(20).ToList();
        sb.AppendLine($"**Symbol-specific relationships:** {(sym.Count == 0 ? "None flagged. Leave-one-symbol-out was not used to build a coin whitelist." : string.Join("; ", sym))}.");
        sb.AppendLine();
        var bh = ctx.Labels.Count(l => l.BhReject && l.Row.LeadHours == 1);
        sb.AppendLine($"**Multiple-testing survivors:** {bh} primary T-1h tests rejected BH FDR 0.10 on the IS p-value. Rejection is not a trading approval. Corrected status is in the validation report next to the raw AUC.");
        sb.AppendLine();
        sb.AppendLine($"**Cost stress:** OOS mean net at 1x = {Fmt(ctx.Costs.OosBase)}; at 2x = {Fmt(ctx.Costs.Oos2x)}. A positive classification score with a non-positive stressed net is not a trading edge. The proxy enters at the research score and books half the labeled excursion. It is not an execution simulator with a book.");
        sb.AppendLine();
        sb.AppendLine("**UP versus DOWN:** They are scored as separate events. A feature can associate with one side only. Opposite-sign claims require the UP and DOWN rows to both clear IS and to flip sign. See the final table. Symmetry was not assumed.");
        sb.AppendLine();
        sb.AppendLine("**Breadth across coins:** Symbol coverage on each primary row is the count of coins with at least 15 events and 15 controls. REPEATABLE requires at least 5 coins and 3 with the IS sign.");
        sb.AppendLine();
        sb.AppendLine("**Regime:** VOL_LOW and VOL_HIGH slices, and BTC up/down/flat, are stored on each episode from causal trailing ATR percentile and BTC 24h return. A sign flip between low and high volatility is REGIME_SPECIFIC. These slices are not trading filters.");
        sb.AppendLine();
        sb.AppendLine("**Early warning before most of the move:** Warning time is only meaningful when the T-4h or earlier effect keeps the IS sign and the mid-path effect is not the only difference. Where the table shows T-1h UNSTABLE or NO_EVIDENCE, the study does not support a detector that fires before the majority of the excursion. Model AUC near 0.5 means the frozen combination does not separate events from matched controls.");
        sb.AppendLine();
        sb.AppendLine("**Missing data:** Liquidations. Spread. Depth outside BTCUSDT, ETHUSDT, BNBUSDT. Vision open interest, funding, and basis outside the 10-coin set. Sub-hour leads on the other coins. NR4/NR7 and cup-and-handle as implemented detectors. REST OI was ignored on purpose.");
        sb.AppendLine();
        sb.AppendLine("**Next research:** Keep the same frozen event rule. Add sub-hour leads only where 5m bars and Vision prints both exist, still without OOS selection. Do not promote a score to Paper. Do not whitelist coins from leave-one-out.");
        sb.AppendLine();
        sb.AppendLine("## Interpretation");
        sb.AppendLine();
        sb.AppendLine("1. Feature correlation is not this study.");
        sb.AppendLine("2. Event association is the median gap versus a matched control.");
        sb.AppendLine("3. Early warning requires the gap at a lead before T0, not on the mid-path bar.");
        sb.AppendLine("4. Classification is the frozen IS model scored on VAL and OOS.");
        sb.AppendLine("5. A trading edge is none of the above. No result here is VALIDATED_FOR_PAPER.");
        sb.AppendLine();
        sb.AppendLine("## Safety");
        sb.AppendLine();
        sb.AppendLine(ctx.Safety.Line);
        sb.AppendLine();
        sb.AppendLine("FINAL STATUS:");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF");
        sb.AppendLine("PAPER = OFF");
        sb.AppendLine("VALIDATED_FOR_PAPER = NONE");
        sb.AppendLine("LIVE_APPROVED = false");
        return sb.ToString();
    }

    private static string TableFor(StudyContext ctx, Func<string, bool> keep)
    {
        var sb = new StringBuilder();
        sb.AppendLine("| Feature | Event | Lead | IS effect | VAL effect | OOS effect | OOS AUC | Label |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");
        var any = false;
        foreach (var item in ctx.Labels.Where(l => l.Row.LeadHours == 1 && keep(l.Row.Feature) && l.Label != "SEE_PRIMARY"))
        {
            if (item.Label == "NO_EVIDENCE" && item.Row.Is.Auc < 0.55)
            {
                continue;
            }

            any = true;
            var r = item.Row;
            sb.AppendLine($"| {r.Feature} | {r.Event} | {r.LeadHours} | {Fmt(r.Is.Effect)} | {Fmt(r.Val.Effect)} | {Fmt(r.Oos.Effect)} | {Fmt(r.Oos.Auc)} | {item.Label} |");
        }

        if (!any)
        {
            sb.AppendLine("| — | — | — | — | — | — | — | NO_EVIDENCE or INSUFFICIENT_DATA |");
        }

        return sb.ToString();
    }

    private static string FamilyAnswer(StudyContext ctx, string prefix, string title)
    {
        return $"**{title}** " + FamilySentence(ctx, id => id.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static string FamilySentence(StudyContext ctx, Func<string, bool> keep)
    {
        var hits = ctx.Labels.Where(l => l.Row.LeadHours == 1 && keep(l.Row.Feature) && l.Label is "REPEATABLE" or "UNSTABLE" or "SYMBOL_SPECIFIC" or "REGIME_SPECIFIC" or "FAMILY_SPECIFIC" or "OOS_ONLY").Take(8).ToList();
        if (hits.Count == 0)
        {
            return "No primary T-1h test in this family cleared even the unstable bar. Treat the family as NO_EVIDENCE on this sample, or INSUFFICIENT_DATA where the series does not exist.";
        }

        return string.Join("; ", hits.Select(h => $"{h.Row.Feature} {h.Row.Event} {h.Label}")) + ".";
    }

    private static string RegimeText(StatRow r)
    {
        if (r.VolLow.Missing || r.VolHigh.Missing)
        {
            return "INSUFFICIENT_DATA";
        }

        return Math.Sign(r.VolLow.Effect) == Math.Sign(r.VolHigh.Effect) ? "same sign" : "sign flip";
    }

    private static string CostText(StudyContext ctx)
    {
        if (double.IsNaN(ctx.Costs.OosBase))
        {
            return "INSUFFICIENT_DATA";
        }

        return ctx.Costs.OosBase > 0 && ctx.Costs.Oos2x > 0 ? "OOS proxy positive at 2x" : "not cost-robust";
    }

    private static string FamilyOf(string id)
    {
        if (id.StartsWith("pa_", StringComparison.Ordinal)) return "price_action";
        if (id.StartsWith("oi_", StringComparison.Ordinal) || id.StartsWith("price_", StringComparison.Ordinal) || id is "ret_x_oi") return "open_interest";
        if (id.StartsWith("funding", StringComparison.Ordinal)) return "funding";
        if (id.StartsWith("basis", StringComparison.Ordinal)) return "basis";
        if (id.StartsWith("taker", StringComparison.Ordinal)) return "taker";
        if (id.StartsWith("depth", StringComparison.Ordinal)) return "depth";
        if (id.StartsWith("volume", StringComparison.Ordinal) || id is "obv_slope" or "cmf_20" or "pv_divergence" or "rel_volume") return "volume";
        if (id.StartsWith("vwap", StringComparison.Ordinal) || id is "poc_dist") return "vwap_profile";
        if (id.Contains("atr", StringComparison.Ordinal) || id.StartsWith("rv_", StringComparison.Ordinal) || id.StartsWith("bb_", StringComparison.Ordinal) || id.StartsWith("vol_", StringComparison.Ordinal) || id is "parkinson_24" or "garman_klass_24" or "rogers_satchell_24" or "yang_zhang_24" or "keltner_width" or "range_pct" or "tr_pct") return "volatility";
        if (id is "breadth_pos" or "dispersion" or "btc_ret_24" or "btc_rv_24" || id.StartsWith("cs_", StringComparison.Ordinal)) return "cross_section";
        if (id.Contains("_x_", StringComparison.Ordinal)) return "interaction";
        return "price_momentum";
    }

    private static string Availability(string id)
    {
        if (id.StartsWith("depth", StringComparison.Ordinal) || id is "depth_x_taker") return "BTCUSDT ETHUSDT BNBUSDT only";
        if (id.StartsWith("oi_", StringComparison.Ordinal) || id.StartsWith("funding", StringComparison.Ordinal) || id.StartsWith("basis", StringComparison.Ordinal) || id.StartsWith("taker", StringComparison.Ordinal) || id.StartsWith("price_", StringComparison.Ordinal) || id is "ret_x_oi" or "ret5_x_oi" or "ret5_x_funding" or "ret5_x_oi_x_funding" or "oi_x_taker" or "funding_x_basis" or "cs_rank_x_oi" or "cs_rank_x_funding")
        {
            return "10-coin Vision/funding/basis set; taker only if cache field 9 is real";
        }

        if (id is "pa_flag" or "pa_hs" or "pa_wedge" or "pa_triangle") return "existing chart engine, 10-coin set";
        if (id is "breadth_pos" or "dispersion") return "coins present on that hour, minimum 30";
        return "1h OHLCV cache";
    }

    private static string Describe(string id) => id.Replace('_', ' ');

    private static string Formula(string id) => id switch
    {
        "ret_1" or "ret_3" or "ret_5" or "ret_10" or "ret_20" or "ret_50" or "roc_10" => "close[t]/close[t-k]-1",
        "mom_accel" => "ret_5[t]-ret_5[t-5]",
        "oi_change_1" or "oi_change_3" or "oi_change_6" or "oi_change_12" or "oi_change_24" => "(oi[t]-oi[t-k])/oi[t-k], null if either print missing",
        "taker_imbalance" => "2*takerBuy/volume-1 when 0<takerBuy<=volume",
        "funding_rate" => "last settled funding at or before bar close",
        "basis" => "mark/index-1 on the same hour, else null",
        "depth_imbalance" => "last ±1% book imbalance inside the hour, else null",
        "bb_width" => "(upper-lower)/middle, 20 bar, 2 sd",
        "atr_pct" => "percentile of ATR(14) in the prior 100 closes including t",
        "vwap_dist" => "close/rolling24 vwap-1",
        "poc_dist" => "close/24-bar volume-mode close bin-1",
        _ => "see feature id; causal window ending at t"
    };

    private static string Need(string id)
    {
        if (id.StartsWith("oi_", StringComparison.Ordinal) || id.Contains("oi", StringComparison.Ordinal)) return "Vision sum_open_interest";
        if (id.StartsWith("funding", StringComparison.Ordinal)) return "settled funding";
        if (id.StartsWith("basis", StringComparison.Ordinal)) return "mark and index";
        if (id.StartsWith("taker", StringComparison.Ordinal)) return "kline taker buy";
        if (id.StartsWith("depth", StringComparison.Ordinal)) return "bookDepth ±1%";
        if (id.StartsWith("pa_", StringComparison.Ordinal)) return "OHLCV via existing price-action engine";
        return "OHLCV";
    }

    private static string LookbackOf(string id)
    {
        if (id.Contains("200", StringComparison.Ordinal)) return "200";
        if (id.Contains("100", StringComparison.Ordinal) || id.EndsWith("_pct", StringComparison.Ordinal)) return "100";
        if (id.Contains("24", StringComparison.Ordinal)) return "24";
        if (id.Contains("50", StringComparison.Ordinal)) return "50";
        if (id.Contains("20", StringComparison.Ordinal)) return "20";
        if (id.Contains("14", StringComparison.Ordinal) || id.Contains("15", StringComparison.Ordinal)) return "14-15";
        return "1-48 closed bars";
    }

    private static Safety ReadSafety(string root)
    {
        var path = Path.Combine(root, "src", "TradingPlatform.Api", "appsettings.json");
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var trading = doc.RootElement.GetProperty("Trading");
        var live = trading.GetProperty("LiveTradingEnabled").GetBoolean();
        var csr = trading.GetProperty("CrossSectionalReversal");
        var csrLive = csr.GetProperty("LiveEnabled").GetBoolean();
        var csrPaper = csr.GetProperty("PaperEnabled").GetBoolean();
        var csrOn = csr.GetProperty("Enabled").GetBoolean();
        var scalp = trading.GetProperty("Scalping");
        var scalpOn = scalp.GetProperty("Enabled").GetBoolean();
        var scalpLive = scalp.GetProperty("AllowLive").GetBoolean();
        var ok = !live && !csrLive && !csrPaper && !csrOn && !scalpOn && !scalpLive;
        var line = ok
            ? "LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE. LIVE_APPROVED = false. Trading:LiveTradingEnabled = false. Trading:CrossSectionalReversal:LiveEnabled = false. No Paper strategy was enabled. No strategy was promoted. No Binance order was submitted."
            : "SAFETY CHECK FAILED. Flags were not modified by this study.";
        return new Safety(ok, line);
    }

    private static string ManifestText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("extreme-move-manifest-v1");
        foreach (var id in FeatureIds)
        {
            sb.AppendLine($"{id}|{FamilyOf(id)}|{Formula(id)}|{LookbackOf(id)}|1h|{Need(id)}|closed-bar");
        }

        return sb.ToString();
    }

    private static string EventDefinitionText() =>
        "horizon=24h clock=1h up=futureHigh/close-1 down=futureLow/close-1 " +
        "thresholds=10,20,30,50,80,100,150 up and 10,20,30,50,80 down " +
        "separation=24h after max excursion bar direction=first 10% touch " +
        "IS<2025-09-23 VAL<2026-03-23 OOS after FAST<=1h MEDIUM<=4h SLOW>4h " +
        "BH=0.10 primary=T-1h x key thresholds no OOS selection";

    private static string Sha(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static Breadth BuildBreadth(List<Book> books)
    {
        var count = new Dictionary<long, int>();
        var pos = new Dictionary<long, int>();
        var sum = new Dictionary<long, double>();
        var sumSq = new Dictionary<long, double>();
        foreach (var book in books)
        {
            for (var i = 1; i < book.N; i++)
            {
                if (!book.GapOk(i, 1) || book.C[i - 1] <= 0)
                {
                    continue;
                }

                var r = book.C[i] / book.C[i - 1] - 1.0;
                var t = book.OpenMs[i];
                count[t] = count.GetValueOrDefault(t) + 1;
                if (r > 0)
                {
                    pos[t] = pos.GetValueOrDefault(t) + 1;
                }

                sum[t] = sum.GetValueOrDefault(t) + r;
                sumSq[t] = sumSq.GetValueOrDefault(t) + r * r;
            }
        }

        var disp = new Dictionary<long, float>(count.Count);
        foreach (var kv in count)
        {
            if (kv.Value < 30)
            {
                continue;
            }

            var mean = sum[kv.Key] / kv.Value;
            var v = sumSq[kv.Key] / kv.Value - mean * mean;
            disp[kv.Key] = (float)Math.Sqrt(Math.Max(0, v));
        }

        return new Breadth { Count = count, Pos = pos, Disp = disp };
    }

    private static async Task<MicroCache> LoadMicroAsync(string root, List<Book> books)
    {
        var cache = new MicroCache();
        var vision = Path.Combine(root, "artifacts", "strategy-research", "wave-4", "vision", "metrics");
        var history = Path.Combine(root, "artifacts", "data", "futures-history-v1");
        var depthRoot = Path.Combine(root, "artifacts", "strategy-research", "microstructure", "book-depth");
        foreach (var symbol in MicroSymbols)
        {
            if (books.All(b => b.Symbol != symbol))
            {
                continue;
            }

            var series = new MicroSeries();
            var zipDir = Path.Combine(vision, symbol);
            if (Directory.Exists(zipDir))
            {
                foreach (var zip in Directory.EnumerateFiles(zipDir, "*.zip"))
                {
                    foreach (var point in BinanceVisionClient.ReadZip(zip, symbol))
                    {
                        series.Oi.Add((point.CreateTime.ToUnixTimeMilliseconds(), (float)point.SumOpenInterest));
                    }
                }

                series.Oi.Sort((a, b) => a.Ms.CompareTo(b.Ms));
            }

            var fundingPath = Path.Combine(history, "funding", symbol + ".json");
            if (File.Exists(fundingPath))
            {
                foreach (var point in JsonSerializer.Deserialize<List<FundingPoint>>(await File.ReadAllTextAsync(fundingPath)) ?? [])
                {
                    series.Funding.Add((point.FundingTime.ToUnixTimeMilliseconds(), (float)point.FundingRate));
                }

                series.Funding.Sort((a, b) => a.Ms.CompareTo(b.Ms));
            }

            var basisPath = Path.Combine(history, "basis", symbol + "_1h.json");
            if (File.Exists(basisPath))
            {
                foreach (var point in JsonSerializer.Deserialize<List<BasisPoint>>(await File.ReadAllTextAsync(basisPath)) ?? [])
                {
                    series.Basis.Add((point.CloseTime.ToUnixTimeMilliseconds(), (float)point.NormalizedBasis));
                }

                series.Basis.Sort((a, b) => a.Ms.CompareTo(b.Ms));
            }

            var depthPath = Path.Combine(depthRoot, symbol + "-imbalance-1pct.csv");
            if (File.Exists(depthPath))
            {
                foreach (var line in File.ReadLines(depthPath))
                {
                    var comma = line.IndexOf(',');
                    if (comma <= 0)
                    {
                        continue;
                    }

                    if (!long.TryParse(line.AsSpan(0, comma), out var ms))
                    {
                        continue;
                    }

                    if (!float.TryParse(line.AsSpan(comma + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var imb))
                    {
                        continue;
                    }

                    var hour = ms - (ms % 3_600_000L) ;
                    if (series.Depth.Count > 0 && series.Depth[^1].Ms == hour)
                    {
                        series.Depth[^1] = (hour, imb);
                    }
                    else if (series.Depth.Count == 0 || hour > series.Depth[^1].Ms)
                    {
                        series.Depth.Add((hour, imb));
                    }
                }
            }

            cache.Symbols[symbol] = series;
            Console.WriteLine($"{symbol} oi={series.Oi.Count} funding={series.Funding.Count} basis={series.Basis.Count} depthHours={series.Depth.Count}");
        }

        return cache;
    }

    private static Book ReadBook(string path, string symbol)
    {
        var open = new List<long>(20000);
        var c = new List<float>(20000);
        var h = new List<float>(20000);
        var l = new List<float>(20000);
        var v = new List<float>(20000);
        var tb = new List<float>(20000);
        var bytes = File.ReadAllBytes(path);
        var reader = new Utf8JsonReader(bytes, isFinalBlock: true, state: default);
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
                        tv = (float)reader.GetDouble();
                        break;
                }
            }
        }

        var book = new Book
        {
            Symbol = symbol,
            OpenMs = open.ToArray(),
            C = c.ToArray(),
            H = h.ToArray(),
            L = l.ToArray(),
            V = v.ToArray(),
            Tb = tb.ToArray()
        };
        var good = 0;
        for (var i = 0; i < book.N; i++)
        {
            if (book.Tb[i] > 0 && book.V[i] > 0 && book.Tb[i] <= book.V[i])
            {
                good++;
            }
        }

        book.TakerOk = book.N > 0 && good > book.N * 0.8;
        return book;
    }

    private static float[] Ema(float[] x, int n)
    {
        var y = new float[x.Length];
        Array.Fill(y, float.NaN);
        var k = 2f / (n + 1);
        double seed = 0;
        var seen = 0;
        for (var i = 0; i < x.Length; i++)
        {
            if (float.IsNaN(x[i]))
            {
                continue;
            }

            seen++;
            if (seen < n)
            {
                seed += x[i];
                continue;
            }

            if (seen == n)
            {
                seed += x[i];
                y[i] = (float)(seed / n);
                continue;
            }

            y[i] = y[i - 1] + k * (x[i] - (float.IsNaN(y[i - 1]) ? x[i] : y[i - 1]));
            if (float.IsNaN(y[i - 1]))
            {
                var prev = i - 1;
                while (prev >= 0 && float.IsNaN(y[prev]))
                {
                    prev--;
                }

                y[i] = prev < 0 ? x[i] : y[prev] + k * (x[i] - y[prev]);
            }
        }

        return y;
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

    private static float[] TrueRange(Book b)
    {
        var y = new float[b.N];
        y[0] = b.H[0] - b.L[0];
        for (var i = 1; i < b.N; i++)
        {
            var hl = b.H[i] - b.L[i];
            var hc = Math.Abs(b.H[i] - b.C[i - 1]);
            var lc = Math.Abs(b.L[i] - b.C[i - 1]);
            y[i] = Math.Max(hl, Math.Max(hc, lc));
        }

        return y;
    }

    private static float[] Atr(Book b, int n)
    {
        var tr = TrueRange(b);
        var y = new float[b.N];
        Array.Fill(y, float.NaN);
        double seed = 0;
        for (var i = 0; i < b.N; i++)
        {
            if (i < n)
            {
                seed += tr[i];
                if (i == n - 1)
                {
                    y[i] = (float)(seed / n);
                }

                continue;
            }

            y[i] = (y[i - 1] * (n - 1) + tr[i]) / n;
        }

        return y;
    }

    private static (float Adx, float Plus, float Minus) Adx(Book b, float[] tr, int i, int n)
    {
        if (i < n + 2)
        {
            return (float.NaN, float.NaN, float.NaN);
        }

        double trSum = 0, p = 0, m = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            trSum += tr[k];
            var up = b.H[k] - b.H[k - 1];
            var down = b.L[k - 1] - b.L[k];
            if (up > down && up > 0)
            {
                p += up;
            }

            if (down > up && down > 0)
            {
                m += down;
            }
        }

        if (trSum <= 0)
        {
            return (float.NaN, float.NaN, float.NaN);
        }

        var plus = 100 * p / trSum;
        var minus = 100 * m / trSum;
        var adx = 100 * Math.Abs(plus - minus) / Math.Max(1e-9, plus + minus);
        return ((float)adx, (float)plus, (float)minus);
    }

    private static float Ret(Book b, int i, int k)
    {
        if (i - k < 0 || b.C[i - k] <= 0 || !b.GapOk(i, k))
        {
            return float.NaN;
        }

        return b.C[i] / b.C[i - k] - 1f;
    }

    private static float Dist(float price, float ema) => float.IsNaN(ema) || ema == 0 ? float.NaN : price / ema - 1f;

    private static float Slope(float[] ema, int i, int k)
    {
        if (i - k < 0 || float.IsNaN(ema[i]) || float.IsNaN(ema[i - k]) || ema[i - k] == 0)
        {
            return float.NaN;
        }

        return ema[i] / ema[i - k] - 1f;
    }

    private static float Stoch(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        var hi = float.MinValue;
        var lo = float.MaxValue;
        for (var k = i - n + 1; k <= i; k++)
        {
            hi = Math.Max(hi, b.H[k]);
            lo = Math.Min(lo, b.L[k]);
        }

        return hi <= lo ? float.NaN : 100f * (b.C[i] - lo) / (hi - lo);
    }

    private static float StochOf(float[] x, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        var hi = float.MinValue;
        var lo = float.MaxValue;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (float.IsNaN(x[k]))
            {
                return float.NaN;
            }

            hi = Math.Max(hi, x[k]);
            lo = Math.Min(lo, x[k]);
        }

        return hi <= lo ? float.NaN : 100f * (x[i] - lo) / (hi - lo);
    }

    private static float Cci(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            sum += (b.H[k] + b.L[k] + b.C[k]) / 3.0;
        }

        var mean = sum / n;
        double dev = 0;
        var tp = (b.H[i] + b.L[i] + b.C[i]) / 3.0;
        for (var k = i - n + 1; k <= i; k++)
        {
            dev += Math.Abs((b.H[k] + b.L[k] + b.C[k]) / 3.0 - mean);
        }

        dev /= n;
        return dev == 0 ? float.NaN : (float)((tp - mean) / (0.015 * dev));
    }

    private static float WillR(Book b, int i, int n)
    {
        var s = Stoch(b, i, n);
        return float.IsNaN(s) ? float.NaN : s - 100f;
    }

    private static float Mfi(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double pos = 0, neg = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            var tp = (b.H[k] + b.L[k] + b.C[k]) / 3.0;
            var prev = (b.H[k - 1] + b.L[k - 1] + b.C[k - 1]) / 3.0;
            var flow = tp * b.V[k];
            if (tp > prev)
            {
                pos += flow;
            }
            else if (tp < prev)
            {
                neg += flow;
            }
        }

        if (pos + neg == 0)
        {
            return float.NaN;
        }

        return (float)(100 - 100 / (1 + pos / Math.Max(neg, 1e-12)));
    }

    private static float Aroon(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        var hi = i;
        var lo = i;
        for (var k = i - n; k <= i; k++)
        {
            if (b.H[k] >= b.H[hi])
            {
                hi = k;
            }

            if (b.L[k] <= b.L[lo])
            {
                lo = k;
            }
        }

        var up = 100f * (n - (i - hi)) / n;
        var down = 100f * (n - (i - lo)) / n;
        return up - down;
    }

    private static float Trix(float[] c, int i, int n)
    {
        if (i < n * 3 + 2)
        {
            return float.NaN;
        }

        double e1 = c[i - n * 3], e2 = e1, e3 = e1, prev = e1;
        var k = 2.0 / (n + 1);
        for (var t = i - n * 3 + 1; t <= i; t++)
        {
            e1 = e1 + k * (c[t] - e1);
            e2 = e2 + k * (e1 - e2);
            prev = e3;
            e3 = e3 + k * (e2 - e3);
        }

        return Math.Abs(prev) < 1e-12 ? float.NaN : (float)((e3 - prev) / prev);
    }

    private static float RealizedVol(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        var count = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (b.C[k - 1] <= 0 || !b.GapOk(k, 1))
            {
                return float.NaN;
            }

            var r = Math.Log(b.C[k] / b.C[k - 1]);
            sum += r * r;
            count++;
        }

        return (float)Math.Sqrt(sum / count);
    }

    private static float Parkinson(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (b.L[k] <= 0)
            {
                return float.NaN;
            }

            var r = Math.Log(b.H[k] / b.L[k]);
            sum += r * r;
        }

        return (float)Math.Sqrt(sum / (n * 4 * Math.Log(2)));
    }

    private static float GarmanKlass(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (b.L[k] <= 0 || b.C[k - 1] <= 0)
            {
                return float.NaN;
            }

            var hl = Math.Log(b.H[k] / b.L[k]);
            var co = Math.Log(b.C[k] / b.C[k - 1]);
            sum += 0.5 * hl * hl - (2 * Math.Log(2) - 1) * co * co;
        }

        return (float)Math.Sqrt(Math.Max(0, sum / n));
    }

    private static float RogersSatchell(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (b.L[k] <= 0)
            {
                return float.NaN;
            }

            var hc = Math.Log(b.H[k] / b.C[k]);
            var ho = Math.Log(b.H[k] / Math.Max(b.C[k - 1], 1e-12f));
            var lc = Math.Log(b.L[k] / b.C[k]);
            var lo = Math.Log(b.L[k] / Math.Max(b.C[k - 1], 1e-12f));
            sum += hc * ho + lc * lo;
        }

        return (float)Math.Sqrt(Math.Max(0, sum / n));
    }

    private static float YangZhang(Book b, int i, int n)
    {
        if (i < n + 1)
        {
            return float.NaN;
        }

        double oc = 0, oo = 0, rs = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            if (b.C[k - 1] <= 0 || b.L[k] <= 0)
            {
                return float.NaN;
            }

            var o = b.C[k - 1];
            var u = Math.Log(b.C[k] / o);
            var v = Math.Log(o / b.C[k - 1]);
            oc += u * u;
            oo += v * v;
            var hc = Math.Log(b.H[k] / b.C[k]);
            var ho = Math.Log(b.H[k] / o);
            var lc = Math.Log(b.L[k] / b.C[k]);
            var lo = Math.Log(b.L[k] / o);
            rs += hc * ho + lc * lo;
        }

        var kcoef = 0.34;
        return (float)Math.Sqrt(Math.Max(0, oo / n + kcoef * oc / n + (1 - kcoef) * rs / n));
    }

    private static float RangePct(Book b, int i, int n)
    {
        var range = new float[b.N];
        for (var k = 0; k < b.N; k++)
        {
            range[k] = b.H[k] - b.L[k];
        }

        return Percentile(range, i, n);
    }

    private static (float Width, float Pct) Bollinger(float[] c, int i, int n)
    {
        if (i < n)
        {
            return (float.NaN, float.NaN);
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
        if (mean == 0)
        {
            return (float.NaN, float.NaN);
        }

        var upper = mean + 2 * sd;
        var lower = mean - 2 * sd;
        var width = (float)((upper - lower) / mean);
        var pct = sd == 0 ? float.NaN : (float)((c[i] - lower) / (upper - lower));
        return (width, pct);
    }

    private static float VolOfVol(float[] atr, int i, int n) => ZScore(atr, i, n);

    private static float Vwap(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double pv = 0, vol = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            var typical = (b.H[k] + b.L[k] + b.C[k]) / 3.0;
            pv += typical * b.V[k];
            vol += b.V[k];
        }

        return vol <= 0 ? float.NaN : (float)(pv / vol);
    }

    private static float PocDist(Book b, int i, int n)
    {
        if (i < n || b.C[i] <= 0)
        {
            return float.NaN;
        }

        var lo = float.MaxValue;
        var hi = float.MinValue;
        for (var k = i - n + 1; k <= i; k++)
        {
            lo = Math.Min(lo, b.L[k]);
            hi = Math.Max(hi, b.H[k]);
        }

        if (hi <= lo)
        {
            return float.NaN;
        }

        var bins = new double[12];
        var width = (hi - lo) / 12f;
        for (var k = i - n + 1; k <= i; k++)
        {
            var bin = Math.Clamp((int)((b.C[k] - lo) / width), 0, 11);
            bins[bin] += b.V[k];
        }

        var best = 0;
        for (var k = 1; k < bins.Length; k++)
        {
            if (bins[k] > bins[best])
            {
                best = k;
            }
        }

        var poc = lo + (best + 0.5f) * width;
        return b.C[i] / poc - 1f;
    }

    private static float Cmf(Book b, int i, int n)
    {
        if (i < n)
        {
            return float.NaN;
        }

        double mf = 0, vol = 0;
        for (var k = i - n + 1; k <= i; k++)
        {
            var range = b.H[k] - b.L[k];
            if (range <= 0)
            {
                continue;
            }

            var m = ((b.C[k] - b.L[k]) - (b.H[k] - b.C[k])) / range;
            mf += m * b.V[k];
            vol += b.V[k];
        }

        return vol <= 0 ? float.NaN : (float)(mf / vol);
    }

    private static float Percentile(float[] x, int i, int n)
    {
        var from = i - n + 1;
        if (from < 0 || float.IsNaN(x[i]))
        {
            return float.NaN;
        }

        var below = 0;
        var finite = 0;
        var v = x[i];
        for (var k = from; k <= i; k++)
        {
            if (float.IsNaN(x[k]))
            {
                continue;
            }

            finite++;
            if (x[k] < v)
            {
                below++;
            }
        }

        return finite < n / 2 ? float.NaN : below / (float)finite;
    }

    private static float PercentileFeature(float[,] m, int f, int i, int n)
    {
        var from = i - n + 1;
        if (from < 0 || float.IsNaN(m[f, i]))
        {
            return float.NaN;
        }

        var below = 0;
        var finite = 0;
        var v = m[f, i];
        for (var k = from; k <= i; k++)
        {
            if (float.IsNaN(m[f, k]))
            {
                continue;
            }

            finite++;
            if (m[f, k] < v)
            {
                below++;
            }
        }

        return finite < 20 ? float.NaN : below / (float)finite;
    }

    private static float ZFeature(float[,] m, int f, int i, int n)
    {
        var from = i - n + 1;
        if (from < 0)
        {
            return float.NaN;
        }

        double sum = 0;
        var count = 0;
        for (var k = from; k <= i; k++)
        {
            if (float.IsNaN(m[f, k]))
            {
                continue;
            }

            sum += m[f, k];
            count++;
        }

        if (count < 20 || float.IsNaN(m[f, i]))
        {
            return float.NaN;
        }

        var mean = sum / count;
        double var = 0;
        for (var k = from; k <= i; k++)
        {
            if (float.IsNaN(m[f, k]))
            {
                continue;
            }

            var d = m[f, k] - mean;
            var += d * d;
        }

        var sd = Math.Sqrt(var / count);
        return sd < 1e-12 ? 0 : (float)((m[f, i] - mean) / sd);
    }

    private static float ZScore(float[] x, int i, int n)
    {
        var from = i - n + 1;
        if (from < 0)
        {
            return float.NaN;
        }

        double sum = 0;
        var count = 0;
        for (var k = from; k <= i; k++)
        {
            if (float.IsNaN(x[k]))
            {
                continue;
            }

            sum += x[k];
            count++;
        }

        if (count < n / 2)
        {
            return float.NaN;
        }

        var mean = sum / count;
        double var = 0;
        for (var k = from; k <= i; k++)
        {
            if (float.IsNaN(x[k]))
            {
                continue;
            }

            var d = x[k] - mean;
            var += d * d;
        }

        var sd = Math.Sqrt(var / count);
        return sd < 1e-12 ? 0 : (float)((x[i] - mean) / sd);
    }

    private static float Mean(float[] x, int i, int n)
    {
        var from = i - n + 1;
        if (from < 0)
        {
            return float.NaN;
        }

        double sum = 0;
        for (var k = from; k <= i; k++)
        {
            sum += x[k];
        }

        return (float)(sum / n);
    }

    private static float MedianWindow(float[,] m, int f, int i, int n)
    {
        var from = i - n + 1;
        if (from < 0)
        {
            return float.NaN;
        }

        var vals = new List<float>();
        for (var k = from; k <= i; k++)
        {
            if (!float.IsNaN(m[f, k]))
            {
                vals.Add(m[f, k]);
            }
        }

        return vals.Count < 10 ? float.NaN : Median(vals);
    }

    private static float Chg(float now, float prev) =>
        float.IsNaN(now) || float.IsNaN(prev) || Math.Abs(prev) < 1e-12f ? float.NaN : now / prev - 1f;

    private static float AlignReturn(Book? btc, long hour, int bars)
    {
        if (btc is null)
        {
            return float.NaN;
        }

        var i = Array.BinarySearch(btc.OpenMs, hour);
        if (i < 0)
        {
            i = ~i - 1;
        }

        if (i < bars || i >= btc.N || btc.OpenMs[i] != hour)
        {
            return float.NaN;
        }

        return Ret(btc, i, bars);
    }

    private static float AlignRv(Book? btc, long hour)
    {
        if (btc is null)
        {
            return float.NaN;
        }

        var i = Array.BinarySearch(btc.OpenMs, hour);
        if (i < 0 || i >= btc.N)
        {
            return float.NaN;
        }

        return RealizedVol(btc, i, 24);
    }

    private static void Set(float[,] m, string id, int i, float value) => m[IndexOf(id), i] = value;

    private static int IndexOf(string id)
    {
        var i = Array.IndexOf(FeatureIds, id);
        if (i < 0)
        {
            throw new InvalidOperationException(id);
        }

        return i;
    }

    private static int IndexOfThreshold(string id)
    {
        for (var i = 0; i < Thresholds.Length; i++)
        {
            if (Thresholds[i].Id == id)
            {
                return i;
            }
        }

        throw new InvalidOperationException(id);
    }

    private static string SplitOf(DateTimeOffset t) =>
        t < IsEnd ? "IS" : t < ValEnd ? "VAL" : "OOS";

    private static int BlockOf(DateTimeOffset t) =>
        t < Block1 ? 0 : t < Block2 ? 1 : t < Block3 ? 2 : 3;

    private static string SpeedOf(int bars) => bars <= 1 ? "FAST" : bars <= 4 ? "MEDIUM" : "SLOW";

    private static float Median(List<float> values)
    {
        if (values.Count == 0)
        {
            return float.NaN;
        }

        var arr = values.ToArray();
        Array.Sort(arr);
        var mid = arr.Length / 2;
        return arr.Length % 2 == 1 ? arr[mid] : 0.5f * (arr[mid - 1] + arr[mid]);
    }

    private static double Auc(IReadOnlyList<double> scores, IReadOnlyList<int> y)
    {
        var order = Enumerable.Range(0, scores.Count).OrderBy(i => scores[i]).ToArray();
        double rankSum = 0;
        var pos = 0;
        var i = 0;
        while (i < order.Length)
        {
            var j = i;
            while (j + 1 < order.Length && scores[order[j + 1]] == scores[order[i]])
            {
                j++;
            }

            var avg = (i + 1 + j + 1) / 2.0;
            for (var k = i; k <= j; k++)
            {
                if (y[order[k]] == 1)
                {
                    rankSum += avg;
                    pos++;
                }
            }

            i = j + 1;
        }

        var neg = y.Count - pos;
        if (pos == 0 || neg == 0)
        {
            return double.NaN;
        }

        return (rankSum - pos * (pos + 1) / 2.0) / (pos * (double)neg);
    }

    private static double EqualAuc(List<float[]> z, List<int> y)
    {
        var scores = z.Select(r => r.Sum(v => (double)v)).ToArray();
        return Auc(scores, y.ToArray());
    }

    private static double RankAuc(List<float[]> raw, List<int> y)
    {
        var scores = new double[raw.Count];
        var cols = raw[0].Length;
        for (var j = 0; j < cols; j++)
        {
            var order = Enumerable.Range(0, raw.Count).OrderBy(i => float.IsNaN(raw[i][j]) ? float.MaxValue : raw[i][j]).ToArray();
            for (var rank = 0; rank < order.Length; rank++)
            {
                if (!float.IsNaN(raw[order[rank]][j]))
                {
                    scores[order[rank]] += rank;
                }
            }
        }

        return Auc(scores, y.ToArray());
    }

    private static double StumpAuc(List<float[]> z, List<int> y)
    {
        if (z.Count == 0)
        {
            return double.NaN;
        }

        var bestJ = 0;
        var bestGain = double.NegativeInfinity;
        var bestThr = 0.0;
        var bestDir = 1;
        for (var j = 0; j < z[0].Length; j++)
        {
            foreach (var thr in new[] { -0.5, 0.0, 0.5 })
            {
                foreach (var dir in new[] { 1, -1 })
                {
                    var correct = 0;
                    for (var i = 0; i < z.Count; i++)
                    {
                        var pred = dir * z[i][j] > thr ? 1 : 0;
                        if (pred == y[i])
                        {
                            correct++;
                        }
                    }

                    if (correct > bestGain)
                    {
                        bestGain = correct;
                        bestJ = j;
                        bestThr = thr;
                        bestDir = dir;
                    }
                }
            }
        }

        var scores = z.Select(r => bestDir * (double)r[bestJ] - bestThr).ToArray();
        return Auc(scores, y.ToArray());
    }

    private static double Dot(double[] w, float[] x)
    {
        double s = 0;
        for (var i = 0; i < w.Length; i++)
        {
            s += w[i] * x[i];
        }

        return s;
    }

    private static double Sigmoid(double x) => 1.0 / (1.0 + Math.Exp(-Math.Clamp(x, -30, 30)));

    private static bool[] BenjaminiHochberg(List<double> pvals, double q)
    {
        var n = pvals.Count;
        var order = Enumerable.Range(0, n).OrderBy(i => double.IsNaN(pvals[i]) ? 1 : pvals[i]).ToArray();
        var reject = new bool[n];
        var cutoff = -1;
        for (var rank = 1; rank <= n; rank++)
        {
            var p = pvals[order[rank - 1]];
            if (!double.IsNaN(p) && p <= q * rank / n)
            {
                cutoff = rank;
            }
        }

        for (var rank = 1; rank <= cutoff; rank++)
        {
            reject[order[rank - 1]] = true;
        }

        return reject;
    }

    private static string Fmt(double v) => double.IsNaN(v) ? "NA" : v.ToString("0.####", CultureInfo.InvariantCulture);

    private sealed class Book
    {
        public string Symbol { get; set; } = "";
        public long[] OpenMs { get; set; } = [];
        public float[] C { get; set; } = [];
        public float[] H { get; set; } = [];
        public float[] L { get; set; } = [];
        public float[] V { get; set; } = [];
        public float[] Tb { get; set; } = [];
        public bool TakerOk { get; set; }
        public int N => C.Length;

        public bool GapOk(int i, int bars)
        {
            if (i - bars < 0 || i >= N)
            {
                return false;
            }

            var expected = bars * 3_600_000L;
            var actual = OpenMs[i] - OpenMs[i - bars];
            return actual > 0 && actual < expected + 3_600_000L * Math.Max(2, bars / 5);
        }
    }

    private sealed class Episode
    {
        public string Symbol { get; set; } = "";
        public int Start { get; set; }
        public long StartMs { get; set; }
        public int Dir { get; set; }
        public float MaxExc { get; set; }
        public int MaxBar { get; set; }
        public int TimeToMax { get; set; }
        public bool[] Hit { get; set; } = [];
        public int[] TimeTo { get; set; } = [];
        public int PrimaryThreshold { get; set; }
        public string Split { get; set; } = "";
        public int Block { get; set; }
        public string Speed { get; set; } = "";
        public bool Micro { get; set; }
        public int VolBucket { get; set; }
        public int BtcRegime { get; set; }
        public int ControlFor { get; set; } = -1;
        public float[][] Feat { get; set; } = [];
    }

    private sealed class StatRow
    {
        public string Feature { get; set; } = "";
        public string Event { get; set; } = "";
        public string Direction { get; set; } = "";
        public int LeadHours { get; set; }
        public int Orient { get; set; } = 1;
        public StatSlice Is { get; set; } = StatSlice.None();
        public StatSlice Val { get; set; } = StatSlice.None();
        public StatSlice Oos { get; set; } = StatSlice.None();
        public StatSlice All { get; set; } = StatSlice.None();
        public StatSlice VolLow { get; set; } = StatSlice.None();
        public StatSlice VolHigh { get; set; } = StatSlice.None();
        public int Symbols { get; set; }
        public int SymbolsPos { get; set; }
        public int SymbolsNeg { get; set; }
        public double SymbolMean { get; set; }
        public double Best { get; set; }
        public double Worst { get; set; }
        public int BlockSame { get; set; }
        public int BlockKnown { get; set; }
        public int DuringNote { get; set; }

        public static StatRow Insufficient(string feature, string ev, int lead) => new()
        {
            Feature = feature,
            Event = ev,
            LeadHours = lead,
            Is = StatSlice.None()
        };
    }

    private struct StatSlice
    {
        public bool Missing;
        public int NEvent;
        public int NControl;
        public double Effect;
        public double StdEffect;
        public double Auc;
        public double Precision;
        public double Recall;
        public double Fpr;
        public double PValue;

        public static StatSlice None() => new() { Missing = true, Auc = double.NaN, Effect = double.NaN, PValue = double.NaN, Precision = double.NaN, Recall = double.NaN, Fpr = double.NaN, StdEffect = double.NaN };

        public static StatSlice Compute(List<float> events, List<float> controls, int orient)
        {
            var eMed = Median(events);
            var cMed = Median(controls);
            var effect = eMed - cMed;
            var pool = events.Concat(controls).Select(v => (double)v).ToArray();
            var mean = pool.Average();
            var sd = Math.Sqrt(pool.Select(v => (v - mean) * (v - mean)).Average());
            var scores = events.Select(v => orient * (double)v).Concat(controls.Select(v => orient * (double)v)).ToArray();
            var y = Enumerable.Repeat(1, events.Count).Concat(Enumerable.Repeat(0, controls.Count)).ToArray();
            var auc = Auc(scores, y);
            var orientedEvents = events.Select(v => orient * v).OrderBy(v => v).ToArray();
            var cut = orientedEvents.Length == 0 ? 0 : orientedEvents[(int)(orientedEvents.Length * 0.9)];
            var all = events.Select(v => (orient * v, 1)).Concat(controls.Select(v => (orient * v, 0))).ToList();
            var top = all.Where(x => x.Item1 >= cut).ToList();
            if (top.Count < 5)
            {
                top = all.OrderByDescending(x => x.Item1).Take(Math.Max(1, all.Count / 10)).ToList();
            }

            var tp = top.Count(x => x.Item2 == 1);
            var fp = top.Count - tp;
            var precision = top.Count == 0 ? double.NaN : tp / (double)top.Count;
            var recall = events.Count == 0 ? double.NaN : tp / (double)events.Count;
            var fpr = controls.Count == 0 ? double.NaN : fp / (double)controls.Count;
            var se = Math.Sqrt(Math.Max(1e-9, auc * (1 - auc) * (events.Count + controls.Count) / Math.Max(1.0, events.Count * (double)controls.Count)));
            var z = (auc - 0.5) / se;
            var p = 2 * (1 - NormalCdf(Math.Abs(z)));
            return new StatSlice
            {
                Missing = false,
                NEvent = events.Count,
                NControl = controls.Count,
                Effect = effect,
                StdEffect = sd < 1e-12 ? 0 : effect / sd,
                Auc = auc,
                Precision = precision,
                Recall = recall,
                Fpr = fpr,
                PValue = p
            };
        }
    }

    private static double NormalCdf(double x)
    {
        var t = 1.0 / (1.0 + 0.2316419 * Math.Abs(x));
        var d = 0.3989423 * Math.Exp(-x * x / 2);
        var p = d * t * (0.3193815 + t * (-0.3565638 + t * (1.781478 + t * (-1.821256 + t * 1.330274))));
        return x >= 0 ? 1 - p : p;
    }

    private sealed class Labeled
    {
        public StatRow Row { get; set; } = new();
        public string Label { get; set; } = "";
        public bool BhReject { get; set; }
    }

    private sealed class ModelReport
    {
        public List<string> Lines { get; } = [];
        public double[]? Weights { get; set; }
        public double Bias { get; set; }
        public double[]? Means { get; set; }
        public double[]? Sds { get; set; }
        public int[]? Idx { get; set; }
        public double LogisticIs { get; set; } = double.NaN;
        public double LogisticVal { get; set; } = double.NaN;
        public double LogisticOos { get; set; } = double.NaN;
    }

    private sealed class CostReport
    {
        public List<string> Lines { get; } = [];
        public double OosBase { get; set; } = double.NaN;
        public double Oos2x { get; set; } = double.NaN;
    }

    private sealed class ShortHorizon
    {
        public string Text { get; set; } = "";
    }

    private sealed class Breadth
    {
        public Dictionary<long, int> Count { get; set; } = [];
        public Dictionary<long, int> Pos { get; set; } = [];
        public Dictionary<long, float> Disp { get; set; } = [];
    }

    private sealed class MicroCache
    {
        public Dictionary<string, MicroSeries> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TryGet(string symbol, out MicroSeries? series)
        {
            if (Symbols.TryGetValue(symbol, out var found))
            {
                series = found;
                return true;
            }

            series = null;
            return false;
        }
    }

    private sealed class MicroSeries
    {
        public List<(long Ms, float Value)> Oi { get; } = [];
        public List<(long Ms, float Value)> Funding { get; } = [];
        public List<(long Ms, float Value)> Basis { get; } = [];
        public List<(long Ms, float Value)> Depth { get; } = [];

        public float OiAt(long closeMs, long openMs) => Inside(Oi, openMs, closeMs);

        public float OiLag(long closeMs, int hours)
        {
            var target = closeMs - hours * 3_600_000L;
            return LastAtOrBefore(Oi, target, 3_600_000L);
        }

        public float OiZ(long closeMs) => ZOf(Oi, closeMs, 100);
        public float OiPct(long closeMs) => PctOf(Oi, closeMs, 100);
        public float OiVol(long closeMs) => ZOf(Oi, closeMs, 24);
        public float FundingAt(long closeMs) => LastAtOrBefore(Funding, closeMs, 8 * 3_600_000L + 3_600_000L);
        public float FundingPct(long closeMs) => PctOf(Funding, closeMs, 90);
        public float FundingZ(long closeMs) => ZOf(Funding, closeMs, 90);
        public float FundingChange(long closeMs)
        {
            var now = FundingAt(closeMs);
            var prev = LastAtOrBefore(Funding, closeMs - 8 * 3_600_000L, 10 * 3_600_000L);
            return float.IsNaN(now) || float.IsNaN(prev) ? float.NaN : now - prev;
        }

        public float FundingAccel(long closeMs)
        {
            var a = FundingChange(closeMs);
            var b = FundingChange(closeMs - 8 * 3_600_000L);
            return float.IsNaN(a) || float.IsNaN(b) ? float.NaN : a - b;
        }

        public float CumFunding(long closeMs, int n)
        {
            double sum = 0;
            var count = 0;
            var cursor = closeMs;
            for (var i = 0; i < n; i++)
            {
                var v = LastAtOrBefore(Funding, cursor, 10 * 3_600_000L);
                if (float.IsNaN(v))
                {
                    break;
                }

                sum += v;
                count++;
                cursor -= 8 * 3_600_000L;
            }

            return count < 3 ? float.NaN : (float)sum;
        }

        public float FundingPersistence(long closeMs)
        {
            var now = FundingAt(closeMs);
            if (float.IsNaN(now) || now == 0)
            {
                return float.NaN;
            }

            var sign = Math.Sign(now);
            var count = 0;
            var cursor = closeMs;
            for (var i = 0; i < 9; i++)
            {
                var v = LastAtOrBefore(Funding, cursor, 10 * 3_600_000L);
                if (float.IsNaN(v) || Math.Sign(v) != sign)
                {
                    break;
                }

                count++;
                cursor -= 8 * 3_600_000L;
            }

            return count;
        }

        public float FundingExtreme(long closeMs)
        {
            var p = FundingPct(closeMs);
            return float.IsNaN(p) ? float.NaN : p >= 0.95f || p <= 0.05f ? 1 : 0;
        }

        public float FundingReversal(long closeMs)
        {
            var now = FundingAt(closeMs);
            var prev = LastAtOrBefore(Funding, closeMs - 8 * 3_600_000L, 10 * 3_600_000L);
            if (float.IsNaN(now) || float.IsNaN(prev) || now == 0 || prev == 0)
            {
                return float.NaN;
            }

            return Math.Sign(now) != Math.Sign(prev) ? 1 : 0;
        }

        public float BasisAt(long closeMs) => ExactHour(Basis, closeMs);
        public float BasisChange(long closeMs)
        {
            var now = BasisAt(closeMs);
            var prev = ExactHour(Basis, closeMs - 3_600_000L);
            return float.IsNaN(now) || float.IsNaN(prev) ? float.NaN : now - prev;
        }

        public float BasisAccel(long closeMs)
        {
            var a = BasisChange(closeMs);
            var b = BasisChange(closeMs - 3_600_000L);
            return float.IsNaN(a) || float.IsNaN(b) ? float.NaN : a - b;
        }

        public float BasisPct(long closeMs) => PctOf(Basis, closeMs, 100);
        public float BasisZ(long closeMs) => ZOf(Basis, closeMs, 100);
        public float DepthAt(long closeMs, long openMs) => Inside(Depth, openMs, closeMs + 3_600_000L);
        public float DepthChange(long closeMs)
        {
            var now = LastAtOrBefore(Depth, closeMs, 3_600_000L);
            var prev = LastAtOrBefore(Depth, closeMs - 3_600_000L, 3_600_000L);
            return float.IsNaN(now) || float.IsNaN(prev) ? float.NaN : now - prev;
        }

        public float DepthAccel(long closeMs)
        {
            var a = DepthChange(closeMs);
            var b = DepthChange(closeMs - 3_600_000L);
            return float.IsNaN(a) || float.IsNaN(b) ? float.NaN : a - b;
        }

        public float DepthPct(long closeMs) => PctOf(Depth, closeMs, 100);
        public float DepthZ(long closeMs) => ZOf(Depth, closeMs, 48);

        private static float Inside(List<(long Ms, float Value)> series, long open, long close)
        {
            var v = float.NaN;
            var i = Lower(series, open);
            for (; i < series.Count && series[i].Ms <= close; i++)
            {
                if (series[i].Ms >= open)
                {
                    v = series[i].Value;
                }
            }

            return v;
        }

        private static float ExactHour(List<(long Ms, float Value)> series, long closeMs)
        {
            var i = Lower(series, closeMs - 3_600_000L);
            float v = float.NaN;
            for (; i < series.Count && series[i].Ms <= closeMs; i++)
            {
                if (Math.Abs(series[i].Ms - closeMs) < 120_000 || Math.Abs(series[i].Ms - (closeMs - 3_599_999)) < 120_000)
                {
                    v = series[i].Value;
                }
            }

            return v;
        }

        private static float LastAtOrBefore(List<(long Ms, float Value)> series, long ms, long maxAge)
        {
            var i = Lower(series, ms + 1) - 1;
            if (i < 0)
            {
                return float.NaN;
            }

            if (ms - series[i].Ms > maxAge)
            {
                return float.NaN;
            }

            return series[i].Value;
        }

        private static float ZOf(List<(long Ms, float Value)> series, long ms, int n)
        {
            var i = Lower(series, ms + 1) - 1;
            if (i < n)
            {
                return float.NaN;
            }

            double sum = 0;
            for (var k = i - n + 1; k <= i; k++)
            {
                sum += series[k].Value;
            }

            var mean = sum / n;
            double var = 0;
            for (var k = i - n + 1; k <= i; k++)
            {
                var d = series[k].Value - mean;
                var += d * d;
            }

            var sd = Math.Sqrt(var / n);
            return sd < 1e-12 ? 0 : (float)((series[i].Value - mean) / sd);
        }

        private static float PctOf(List<(long Ms, float Value)> series, long ms, int n)
        {
            var i = Lower(series, ms + 1) - 1;
            if (i < n)
            {
                return float.NaN;
            }

            var v = series[i].Value;
            var below = 0;
            for (var k = i - n + 1; k <= i; k++)
            {
                if (series[k].Value < v)
                {
                    below++;
                }
            }

            return below / (float)n;
        }

        private static int Lower(List<(long Ms, float Value)> series, long ms)
        {
            var lo = 0;
            var hi = series.Count;
            while (lo < hi)
            {
                var mid = (lo + hi) / 2;
                if (series[mid].Ms < ms)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid;
                }
            }

            return lo;
        }
    }

    private sealed record Safety(bool Ok, string Line);

    private sealed record StudyContext(
        string FeatureHash,
        string EventHash,
        string ExperimentHash,
        int Symbols,
        int Skipped,
        List<Episode> Episodes,
        List<Episode> Controls,
        int[] Nested,
        int[] Independent,
        List<StatRow> Rows,
        List<Labeled> Labels,
        ModelReport Models,
        CostReport Costs,
        ShortHorizon Short,
        string Lookahead,
        string SelfCheck,
        int TakerSymbols,
        MicroCache Micro,
        Safety Safety);
}
