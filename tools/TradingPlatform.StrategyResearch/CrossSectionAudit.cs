using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TradingPlatform.Backtesting.Validation;
using TradingPlatform.Research;

namespace TradingPlatform.StrategyResearch;

internal static class CrossSectionAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string root, string cacheDir)
    {
        var outDir = Path.Combine(root, "artifacts", "strategy-research", "cross-section");
        Directory.CreateDirectory(outDir);
        var coverage = MicrostructureCoverage(root);
        var hash = WriteManifest(outDir, coverage);
        Console.WriteLine($"CROSS SECTION MANIFEST {hash}");
        var panel = await LoadPanelAsync(cacheDir);
        Console.WriteLine($"Cross section panel symbols={panel.Symbols.Length} bars={panel.Times} from={panel.From:u} to={panel.To:u}.");
        var (insEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(panel.Times);
        var regime = BuildRegime(panel);
        var universe = UniverseCounts(panel);
        var results = new List<FeatureResult>();
        var feature = new float[panel.Length];
        var scratch = new float[panel.Length];
        foreach (var name in CrossSectionCatalog.Features)
        {
            Console.WriteLine($"Cross section scoring {name}.");
            Fill(panel, name, feature, scratch);
            var scored = Score(name, panel, feature, insEnd, valEnd, regime, universe);
            results.Add(scored);
            Console.WriteLine($"{name} {scored.Label} 4h IS n={scored.Decile[2, 0].Count} meanBps={(scored.Decile[2, 0].Mean * 10000d).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        WriteReport(root, hash, panel, coverage, universe, results);
        Console.WriteLine("CROSS SECTION RESEARCH COMPLETE");
        Console.WriteLine(string.Join(", ", results.Select(row => row.Name + "=" + row.Label)));
        return 0;
    }

    private static string WriteManifest(string dir, IReadOnlyList<CoverageNote> coverage)
    {
        var body = JsonSerializer.Serialize(new
        {
            CrossSectionCatalog.Version,
            CrossSectionCatalog.RepeatableRule,
            CrossSectionCatalog.Features,
            CrossSectionCatalog.MicrostructureNotScored,
            Horizons = CrossSectionCatalog.Horizons.Select(row => row.Name).ToArray(),
            CrossSectionCatalog.MinimumSymbols,
            CrossSectionCatalog.MinimumWindow,
            CrossSectionCatalog.MinimumBlock,
            CrossSectionCatalog.MinimumAbsSpread,
            CrossSectionCatalog.OneWayCost,
            CrossSectionCatalog.RoundTripPerLeg,
            CrossSectionCatalog.ExcludedBases,
            Clock = "BTCUSDT 15m grid. A symbol is present at a timestamp only when its own closed bar exists. Ranking uses only symbols present at that timestamp.",
            Costs = "Model B taker fee 0.04% plus slippage 0.02% per fill. A replaced long or short name pays one round trip. Net spread uses non-overlapping holds.",
            Microstructure = coverage
        }, JsonOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(dir, "feature-manifest.json"), JsonSerializer.Serialize(new { Sha256 = hash, Body = JsonDocument.Parse(body).RootElement }, JsonOptions));
        return hash;
    }

    private static List<CoverageNote> MicrostructureCoverage(string root)
    {
        int Count(string path, string pattern) => Directory.Exists(path) ? Directory.GetFiles(path, pattern).Length : 0;
        var history = Path.Combine(root, "artifacts", "data", "futures-history-v1");
        var micro = Path.Combine(root, "artifacts", "strategy-research", "microstructure");
        return
        [
            new("oi", Count(Path.Combine(history, "oi"), "*_15m.json"), "Vision metrics are on the 10-coin research set. REST open interest is 29 days. Below the 30-name decile minimum."),
            new("funding", Count(Path.Combine(history, "funding"), "*.json"), "Settled funding is stored for the 10-coin set only."),
            new("basis", Count(Path.Combine(history, "basis"), "*_15m.json"), "Mark and index basis are stored for the 10-coin set only."),
            new("taker", Count(Path.Combine(micro, "taker-klines"), "*_15m.json"), "Re-downloaded taker-buy covers BTC, ETH, and BNB. The universe cache stores historical taker-buy as zero."),
            new("depth", Count(Path.Combine(micro, "book-depth"), "*-imbalance-1pct.csv"), "±1% depth was ingested for BTC, ETH, and BNB only.")
        ];
    }

    private static async Task<Panel> LoadPanelAsync(string cacheDir)
    {
        var files = Directory.GetFiles(cacheDir, "*_15m.json")
            .Select(path => (Path: path, Symbol: Path.GetFileNameWithoutExtension(path).Replace("_15m", "", StringComparison.Ordinal)))
            .Where(row => CrossSectionMath.AcceptedName(row.Symbol))
            .OrderBy(row => row.Symbol, StringComparer.Ordinal)
            .ToList();
        var btc = files.First(row => row.Symbol == "BTCUSDT");
        var btcBars = await ReadBarsAsync(btc.Path);
        var times = btcBars.Select(bar => bar.Open).Distinct().OrderBy(value => value).ToArray();
        var indexOf = new Dictionary<long, int>(times.Length);
        for (var i = 0; i < times.Length; i++)
        {
            indexOf[times[i]] = i;
        }

        var panel = new Panel(files.Count, times.Length);
        for (var s = 0; s < files.Count; s++)
        {
            panel.Symbols[s] = files[s].Symbol;
            var bars = files[s].Symbol == "BTCUSDT" ? btcBars : await ReadBarsAsync(files[s].Path);
            foreach (var bar in bars)
            {
                if (!indexOf.TryGetValue(bar.Open, out var t))
                {
                    continue;
                }

                var index = (t * panel.Symbols.Length) + s;
                panel.High[index] = bar.High;
                panel.Low[index] = bar.Low;
                panel.Close[index] = bar.Close;
                panel.Volume[index] = bar.Volume;
            }

            if ((s + 1) % 50 == 0)
            {
                Console.WriteLine($"Cross section loaded {s + 1}/{files.Count}.");
            }
        }

        panel.From = DateTimeOffset.FromUnixTimeMilliseconds(times[0]);
        panel.To = DateTimeOffset.FromUnixTimeMilliseconds(times[^1]);
        return panel;
    }

    private static Task<List<Bar>> ReadBarsAsync(string path)
    {
        var rows = new List<Bar>(80000);
        using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("OpenTime", out var openEl) || openEl.GetString() is not { } text)
            {
                continue;
            }

            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var open))
            {
                continue;
            }

            var close = Num(item, "Close");
            if (close <= 0f)
            {
                continue;
            }

            rows.Add(new Bar(open.ToUnixTimeMilliseconds(), Num(item, "High"), Num(item, "Low"), close, Num(item, "Volume")));
        }

        return Task.FromResult(rows);
    }

    private static float Num(JsonElement item, string name)
    {
        if (!item.TryGetProperty(name, out var value))
        {
            return float.NaN;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => (float)value.GetDouble(),
            JsonValueKind.String when float.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => float.NaN
        };
    }

    private static sbyte[] BuildRegime(Panel panel)
    {
        var btc = Array.IndexOf(panel.Symbols, "BTCUSDT");
        var close = new float[panel.Times];
        var vol = new float[panel.Times];
        for (var t = 0; t < panel.Times; t++)
        {
            close[t] = panel.Close[(t * panel.Stride) + btc];
        }

        CrossSectionMath.FillRealizedVol(close, 1, 0, panel.Times, vol);
        var regime = new sbyte[panel.Times];
        var window = new List<float>(CrossSectionCatalog.RegimeLookback);
        for (var t = 0; t < panel.Times; t++)
        {
            if (float.IsNaN(vol[t]))
            {
                continue;
            }

            var pos = window.BinarySearch(vol[t]);
            window.Insert(pos >= 0 ? pos : ~pos, vol[t]);
            if (window.Count > CrossSectionCatalog.RegimeLookback)
            {
                var old = vol[t - CrossSectionCatalog.RegimeLookback];
                if (!float.IsNaN(old))
                {
                    var remove = window.BinarySearch(old);
                    if (remove >= 0)
                    {
                        window.RemoveAt(remove);
                    }
                }
            }

            if (window.Count < CrossSectionCatalog.RegimeLookback)
            {
                continue;
            }

            regime[t] = vol[t] >= window[window.Count / 2] ? (sbyte)2 : (sbyte)1;
        }

        return regime;
    }

    private static int[] UniverseCounts(Panel panel)
    {
        var counts = new int[panel.Times];
        for (var t = 0; t < panel.Times; t++)
        {
            var n = 0;
            var row = t * panel.Stride;
            for (var s = 0; s < panel.Stride; s++)
            {
                if (panel.Close[row + s] > 0f)
                {
                    n++;
                }
            }

            counts[t] = n;
        }

        return counts;
    }

    private static void Fill(Panel panel, string name, float[] feature, float[] scratch)
    {
        Array.Fill(feature, float.NaN);
        for (var s = 0; s < panel.Stride; s++)
        {
            switch (name)
            {
                case "return_15m":
                    CrossSectionMath.FillReturn(panel.Close, panel.Stride, s, panel.Times, 1, feature);
                    break;
                case "return_1h":
                    CrossSectionMath.FillReturn(panel.Close, panel.Stride, s, panel.Times, 4, feature);
                    break;
                case "return_4h":
                    CrossSectionMath.FillReturn(panel.Close, panel.Stride, s, panel.Times, 16, feature);
                    break;
                case "return_12h":
                    CrossSectionMath.FillReturn(panel.Close, panel.Stride, s, panel.Times, 48, feature);
                    break;
                case "return_24h":
                    CrossSectionMath.FillReturn(panel.Close, panel.Stride, s, panel.Times, 96, feature);
                    break;
                case "return_1h_over_atr":
                    CrossSectionMath.FillAtrRatio(panel.Close, panel.High, panel.Low, panel.Stride, s, panel.Times, feature);
                    break;
                case "relative_volume":
                    CrossSectionMath.FillRelativeVolume(panel.Volume, panel.Stride, s, panel.Times, feature);
                    break;
                case "volume_acceleration":
                    CrossSectionMath.FillRelativeVolume(panel.Volume, panel.Stride, s, panel.Times, scratch);
                    CrossSectionMath.FillAcceleration(scratch, panel.Stride, s, panel.Times, feature);
                    break;
            }
        }

        if (name is "return_4h_over_vol" or "return_24h_over_vol")
        {
            Array.Fill(scratch, float.NaN);
            var lookback = name == "return_4h_over_vol" ? 16 : 96;
            for (var s = 0; s < panel.Stride; s++)
            {
                CrossSectionMath.FillReturn(panel.Close, panel.Stride, s, panel.Times, lookback, feature);
                CrossSectionMath.FillRealizedVol(panel.Close, panel.Stride, s, panel.Times, scratch);
            }

            CrossSectionMath.DivideBy(feature, scratch, feature.Length);
        }
    }

    private static FeatureResult Score(string name, Panel panel, float[] feature, int insEnd, int valEnd, sbyte[] regime, int[] universe)
    {
        var horizons = CrossSectionCatalog.Horizons;
        var result = new FeatureResult(name, horizons.Length);
        var buffer = new Obs[panel.Stride];
        var topCounts = new int[panel.Stride];
        var bottomCounts = new int[panel.Stride];
        var oosRanks = 0;
        var comparer = new ObsComparer();
        for (var t = CrossSectionCatalog.HistoryBars; t < panel.Times; t++)
        {
            var n = Pack(feature, panel, t, buffer);
            if (n < CrossSectionCatalog.MinimumSymbols)
            {
                result.Skipped++;
                continue;
            }

            Array.Sort(buffer, 0, n, comparer);
            var phase = t < insEnd ? 0 : t < valEnd ? 1 : 2;
            var block = Math.Clamp((t * 4 / panel.Times) + 1, 1, 4) - 1;
            if (phase == 2)
            {
                var rankTail = Math.Max(1, n / 10);
                for (var i = 0; i < rankTail; i++)
                {
                    bottomCounts[buffer[i].Symbol]++;
                    topCounts[buffer[n - 1 - i].Symbol]++;
                }

                oosRanks++;
            }

            for (var h = 0; h < horizons.Length; h++)
            {
                if (!TryTails(panel, buffer, n, t, horizons[h].Bars, out var decileTop, out var decileBottom, out var quintileTop, out var quintileBottom))
                {
                    continue;
                }

                result.Decile[h, phase].AddSpread(decileTop, decileBottom);
                result.DecileBlocks[h, block].AddSpread(decileTop, decileBottom);
                result.Quintile[h, phase].AddSpread(quintileTop, quintileBottom);
                if (regime[t] == 1)
                {
                    result.LowVol[h].AddSpread(decileTop, decileBottom);
                }
                else if (regime[t] == 2)
                {
                    result.HighVol[h].AddSpread(decileTop, decileBottom);
                }
            }
        }

        ScoreRebalance(panel, feature, result, insEnd, valEnd);
        result.Distinct = panel.Symbols.Where((_, index) => topCounts[index] > 0 || bottomCounts[index] > 0).Count();
        var maxTop = topCounts.Length == 0 ? 0 : topCounts.Max();
        result.MaxShare = oosRanks == 0 ? 1 : maxTop / (double)oosRanks;
        var topIndex = topCounts.Select((count, index) => (count, index)).OrderByDescending(row => row.count).First().index;
        result.TopName = panel.Symbols[topIndex];
        result.OosRanks = oosRanks;
        result.MedianUniverse = Median(universe.Where(count => count > 0).Select(count => (double)count).ToList());
        result.Label = LabelOf(result);
        return result;
    }

    private static void ScoreRebalance(Panel panel, float[] feature, FeatureResult result, int insEnd, int valEnd)
    {
        var horizons = CrossSectionCatalog.Horizons;
        var buffer = new Obs[panel.Stride];
        var comparer = new ObsComparer();
        for (var h = 0; h < horizons.Length; h++)
        {
            HashSet<int>? priorLong = null;
            HashSet<int>? priorShort = null;
            var step = horizons[h].Bars;
            for (var t = CrossSectionCatalog.HistoryBars; t + step < panel.Times; t += step)
            {
                var n = Pack(feature, panel, t, buffer);
                if (n < CrossSectionCatalog.MinimumSymbols)
                {
                    continue;
                }

                Array.Sort(buffer, 0, n, comparer);
                if (!TryBaskets(panel, buffer, n, t, step, 10, out var longs, out var shorts, out var gross))
                {
                    continue;
                }

                var turnLong = Turnover(priorLong, longs);
                var turnShort = Turnover(priorShort, shorts);
                var cost = (turnLong + turnShort) * CrossSectionCatalog.RoundTripPerLeg;
                var phase = t < insEnd ? 0 : t < valEnd ? 1 : 2;
                result.Rebalance[h, phase].AddRebalance(gross, cost);
                priorLong = longs;
                priorShort = shorts;
            }
        }
    }

    private static int Pack(float[] feature, Panel panel, int t, Obs[] buffer)
    {
        var n = 0;
        var row = t * panel.Stride;
        for (var s = 0; s < panel.Stride; s++)
        {
            var value = feature[row + s];
            if (!float.IsNaN(value))
            {
                buffer[n++] = new Obs(value, s);
            }
        }

        return n;
    }

    private static bool TryTails(Panel panel, Obs[] sorted, int n, int t, int horizon, out double decileTop, out double decileBottom, out double quintileTop, out double quintileBottom)
    {
        decileTop = decileBottom = quintileTop = quintileBottom = double.NaN;
        var eligible = 0;
        for (var i = 0; i < n; i++)
        {
            if (!float.IsNaN(Forward(panel, t, sorted[i].Symbol, horizon)))
            {
                eligible++;
            }
        }

        if (eligible < CrossSectionCatalog.MinimumSymbols)
        {
            return false;
        }

        if (!MeanTail(panel, sorted, n, t, horizon, Math.Max(1, eligible / 10), true, out decileTop) || !MeanTail(panel, sorted, n, t, horizon, Math.Max(1, eligible / 10), false, out decileBottom))
        {
            return false;
        }

        return MeanTail(panel, sorted, n, t, horizon, Math.Max(1, eligible / 5), true, out quintileTop)
            && MeanTail(panel, sorted, n, t, horizon, Math.Max(1, eligible / 5), false, out quintileBottom);
    }

    private static bool TryBaskets(Panel panel, Obs[] sorted, int n, int t, int horizon, int fraction, out HashSet<int> longs, out HashSet<int> shorts, out double gross)
    {
        longs = [];
        shorts = [];
        gross = double.NaN;
        var eligible = 0;
        for (var i = 0; i < n; i++)
        {
            if (!float.IsNaN(Forward(panel, t, sorted[i].Symbol, horizon)))
            {
                eligible++;
            }
        }

        var tail = Math.Max(1, eligible / fraction);
        if (eligible < CrossSectionCatalog.MinimumSymbols || !Collect(panel, sorted, n, t, horizon, tail, false, shorts, out var bottom) || !Collect(panel, sorted, n, t, horizon, tail, true, longs, out var top))
        {
            return false;
        }

        gross = top - bottom;
        return true;
    }

    private static bool MeanTail(Panel panel, Obs[] sorted, int n, int t, int horizon, int tail, bool top, out double mean)
    {
        mean = double.NaN;
        double sum = 0;
        var got = 0;
        if (top)
        {
            for (var i = n - 1; i >= 0 && got < tail; i--)
            {
                var forward = Forward(panel, t, sorted[i].Symbol, horizon);
                if (float.IsNaN(forward))
                {
                    continue;
                }

                sum += forward;
                got++;
            }
        }
        else
        {
            for (var i = 0; i < n && got < tail; i++)
            {
                var forward = Forward(panel, t, sorted[i].Symbol, horizon);
                if (float.IsNaN(forward))
                {
                    continue;
                }

                sum += forward;
                got++;
            }
        }

        if (got < tail)
        {
            return false;
        }

        mean = sum / got;
        return true;
    }

    private static bool Collect(Panel panel, Obs[] sorted, int n, int t, int horizon, int tail, bool top, HashSet<int> names, out double mean)
    {
        mean = double.NaN;
        double sum = 0;
        var got = 0;
        var start = top ? n - 1 : 0;
        var direction = top ? -1 : 1;
        for (var i = start; i >= 0 && i < n && got < tail; i += direction)
        {
            var forward = Forward(panel, t, sorted[i].Symbol, horizon);
            if (float.IsNaN(forward))
            {
                continue;
            }

            names.Add(sorted[i].Symbol);
            sum += forward;
            got++;
        }

        if (got < tail)
        {
            return false;
        }

        mean = sum / got;
        return true;
    }

    private static float Forward(Panel panel, int t, int symbol, int horizon)
    {
        if (t + horizon >= panel.Times)
        {
            return float.NaN;
        }

        var now = panel.Close[(t * panel.Stride) + symbol];
        var next = panel.Close[((t + horizon) * panel.Stride) + symbol];
        return now > 0f && next > 0f ? (next / now) - 1f : float.NaN;
    }

    private static double Turnover(HashSet<int>? prior, HashSet<int> current)
    {
        if (prior is null || current.Count == 0)
        {
            return 1;
        }

        var entered = current.Count(symbol => !prior.Contains(symbol));
        var exited = prior.Count(symbol => !current.Contains(symbol));
        return (entered + exited) / 2d / current.Count;
    }

    private static string LabelOf(FeatureResult result)
    {
        var count = result.Decile.GetLength(0);
        var ins = new int[count];
        var val = new int[count];
        var oos = new int[count];
        var blocks = new int[count];
        var high = new int[count];
        var low = new int[count];
        for (var h = 0; h < count; h++)
        {
            ins[h] = SignOf(result.Decile[h, 0], CrossSectionCatalog.MinimumWindow);
            val[h] = SignOf(result.Decile[h, 1], CrossSectionCatalog.MinimumWindow);
            oos[h] = SignOf(result.Decile[h, 2], CrossSectionCatalog.MinimumWindow);
            high[h] = SignOf(result.HighVol[h], CrossSectionCatalog.MinimumRegime);
            low[h] = SignOf(result.LowVol[h], CrossSectionCatalog.MinimumRegime);
            var reference = ins[h] != 0 ? ins[h] : oos[h];
            blocks[h] = reference == 0 ? 0 : Enumerable.Range(0, 4).Count(block => SignOf(result.DecileBlocks[h, block], CrossSectionCatalog.MinimumBlock) == reference);
        }

        return CrossSectionMath.Classify(ins, val, oos, blocks, high, low, result.Distinct, result.MaxShare);
    }

    private static int SignOf(Bag bag, int minimum) => CrossSectionMath.PassSign(bag.Count, bag.Mean, bag.Median, minimum);

    private static void WriteReport(string root, string hash, Panel panel, IReadOnlyList<CoverageNote> coverage, int[] universe, IReadOnlyList<FeatureResult> results)
    {
        var ready = universe.Count(count => count >= CrossSectionCatalog.MinimumSymbols);
        var sb = new StringBuilder();
        sb.AppendLine("# Cross-sectional alpha research");
        sb.AppendLine();
        sb.AppendLine($"Feature manifest SHA-256 `{hash}`.");
        sb.AppendLine();
        sb.AppendLine("This is a research portfolio. It does not create a strategy and it does not call any relationship an edge.");
        sb.AppendLine();
        sb.AppendLine("## 1. Universe construction");
        sb.AppendLine();
        sb.AppendLine("The clock is the BTCUSDT 15-minute grid in the existing USD-M USDT perpetual cache. A symbol joins a timestamp only when that symbol has its own closed bar there. Names enter after 96 of their own bars, so a new listing is not ranked on its first day. BTCDOMUSDT and stablecoin bases are excluded before any return is measured. Today's exchange listing was not projected onto earlier dates beyond the bars that exist.");
        sb.AppendLine();
        sb.AppendLine($"Symbols loaded: {panel.Symbols.Length}. Grid: {panel.From:u} to {panel.To:u}, {panel.Times} bars. Timestamps with at least {CrossSectionCatalog.MinimumSymbols} priced names: {ready}.");
        sb.AppendLine();
        sb.AppendLine("## 2. Data coverage");
        sb.AppendLine();
        sb.AppendLine($"Median priced names per bar: {Median(universe.Select(count => (double)count).ToList()):0}.");
        sb.AppendLine();
        sb.AppendLine("| Series | Local files | Note |");
        sb.AppendLine("| --- | ---: | --- |");
        foreach (var note in coverage)
        {
            sb.AppendLine($"| {note.Name} | {note.Files} | {note.Note} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 3. Feature definitions");
        sb.AppendLine();
        sb.AppendLine("Returns are close-to-close on the 15-minute grid: 1, 4, 16, 48, and 96 bars. `return_1h_over_atr` divides the 1-hour return by ATR(14) / price. The volatility-adjusted 4-hour and 24-hour returns divide by the prior 96-bar realized volatility. Relative volume is the current bar divided by the mean of the previous 20 bars. Volume acceleration is that ratio minus its value four bars earlier. A missing input is left missing.");
        sb.AppendLine();
        sb.AppendLine("## 4. Ranking methodology");
        sb.AppendLine();
        sb.AppendLine(CrossSectionCatalog.RepeatableRule);
        sb.AppendLine();
        sb.AppendLine("Deciles and quintiles use only names with a finite feature and a finite forward return at that horizon. Ties break by symbol name. A cross-sectional z-score has the same order as the raw feature, so it does not create a second basket.");
        sb.AppendLine();
        sb.AppendLine("## 5. Future return definitions");
        sb.AppendLine();
        sb.AppendLine("The forward return is the close `h` bars later divided by the close at the signal, minus one. Horizons are 15m, 1h, 4h, 12h, and 24h. They were fixed before aggregation. The absolute number is the basket's own average return. The cross-sectional spread is the top basket minus the bottom basket.");
        sb.AppendLine();
        sb.AppendLine("## 6. Top versus bottom results");
        sb.AppendLine();
        sb.AppendLine("Decile spread in basis points. Positive means the high-feature basket beat the low-feature basket.");
        sb.AppendLine();
        Table(sb, results, static row => row.Decile);
        sb.AppendLine();
        sb.AppendLine("Quintile spread in basis points. This table does not choose the label.");
        sb.AppendLine();
        Table(sb, results, static row => row.Quintile);
        sb.AppendLine();
        sb.AppendLine("## 7. Market-neutral spread results");
        sb.AppendLine();
        sb.AppendLine("Equal dollar long the top decile and short the bottom decile. The spread above is that portfolio's gross return. Non-overlapping holds, used for costs, are in section 11.");
        sb.AppendLine();
        sb.AppendLine("## 8. IS, validation, OOS");
        sb.AppendLine();
        sb.AppendLine("The phase split is the existing 60/20/20 chronological split of the 15-minute grid. The table in section 6 is the phase result.");
        sb.AppendLine();
        sb.AppendLine("Absolute basket returns at the 4-hour horizon, in basis points. This is the directional comparison.");
        sb.AppendLine();
        sb.AppendLine("| Feature | IS top | IS bottom | VAL top | VAL bottom | OOS top | OOS bottom |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            var h = HorizonIndex("4h");
            sb.AppendLine($"| {result.Name} | {Bps(result.Decile[h, 0].TopMean)} | {Bps(result.Decile[h, 0].BottomMean)} | {Bps(result.Decile[h, 1].TopMean)} | {Bps(result.Decile[h, 1].BottomMean)} | {Bps(result.Decile[h, 2].TopMean)} | {Bps(result.Decile[h, 2].BottomMean)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 9. Chronological blocks");
        sb.AppendLine();
        sb.AppendLine("Mean 4-hour decile spread by quarter of the grid, in basis points.");
        sb.AppendLine();
        sb.AppendLine("| Feature | Block 1 | Block 2 | Block 3 | Block 4 |");
        sb.AppendLine("| --- | ---: | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            var h = HorizonIndex("4h");
            sb.AppendLine($"| {result.Name} | {Bps(result.DecileBlocks[h, 0].Mean)} | {Bps(result.DecileBlocks[h, 1].Mean)} | {Bps(result.DecileBlocks[h, 2].Mean)} | {Bps(result.DecileBlocks[h, 3].Mean)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 10. Symbol participation");
        sb.AppendLine();
        sb.AppendLine("| Feature | OOS ranks | Distinct names in tails | Max top-decile share | Most frequent top name |");
        sb.AppendLine("| --- | ---: | ---: | ---: | --- |");
        foreach (var result in results)
        {
            sb.AppendLine($"| {result.Name} | {result.OosRanks} | {result.Distinct} | {result.MaxShare.ToString("0.0%", CultureInfo.InvariantCulture)} | {result.TopName} |");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Turnover and cost impact");
        sb.AppendLine();
        sb.AppendLine("Non-overlapping rebalance. Gross and net  are basis points per hold. Turnover is the average replaced fraction across both legs. One fully replaced leg costs 12 basis points round trip.");
        sb.AppendLine();
        sb.AppendLine("| Feature | Horizon | OOS holds | OOS gross | OOS turnover | OOS net |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            for (var h = 0; h < CrossSectionCatalog.Horizons.Length; h++)
            {
                var bag = result.Rebalance[h, 2];
                sb.AppendLine($"| {result.Name} | {CrossSectionCatalog.Horizons[h].Name} | {bag.Count} | {Bps(bag.Mean)} | {bag.Turnover.ToString("0.00", CultureInfo.InvariantCulture)} | {Bps(bag.Net)} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 12. Microstructure comparison");
        sb.AppendLine();
        sb.AppendLine("Open interest, funding, taker imbalance, basis, and depth were not ranked. Each local series covers fewer than 30 names, so a decile would be one or two contracts. Those values were not filled in from the coins that do have history, and they were not invented for the rest of the universe.");
        sb.AppendLine();
        sb.AppendLine("## 13. Repeatability classification");
        sb.AppendLine();
        foreach (var label in new[] { "REPEATABLE", "UNIVERSE_SPECIFIC", "OOS_ONLY", "UNSTABLE", "NO_EVIDENCE" })
        {
            var names = results.Where(row => row.Label == label).Select(row => row.Name).ToList();
            sb.AppendLine($"- {label}: {(names.Count == 0 ? "none" : string.Join(", ", names))}.");
        }

        sb.AppendLine();
        sb.AppendLine("## 14. Failure modes");
        sb.AppendLine();
        sb.AppendLine("A positive spread can be long and short both rising, or both falling. Section 8 separates those cases. A spread that changes sign by block, or that appears only out of sample, is not a stable ranking. A spread smaller than the pre-registered round-trip cost does not survive implementation even when the sign is stable.");
        sb.AppendLine();
        sb.AppendLine("## 15. Data limitations");
        sb.AppendLine();
        sb.AppendLine("The cache is the current USD-M USDT perpetual file set. Contracts that were delisted before that cache was built are absent. That is a survivorship limit, and it is not repaired by backfilling. A name with no bar at the exit is left out of that basket rather than given a zero return. The BTC grid drops a timestamp when BTC itself has no bar. Microstructure deciles are not identified.");
        sb.AppendLine();
        sb.AppendLine("## 16. Final conclusion");
        sb.AppendLine();
        var repeatable = results.Where(row => row.Label == "REPEATABLE").Select(row => row.Name).ToList();
        sb.AppendLine(repeatable.Count == 0
            ? "No pre-registered cross-sectional feature met the repeatable bar on the decile spread."
            : "Features that met the pre-registered bar: " + string.Join(", ", repeatable) + ". The label is not an edge.");
        sb.AppendLine();
        sb.AppendLine("LIVE = OFF. PAPER = OFF. VALIDATED_FOR_PAPER = NONE.");
        File.WriteAllText(Path.Combine(root, "docs", "CROSS_SECTIONAL_ALPHA_RESEARCH.md"), sb.ToString());
    }

    private static void Table(StringBuilder sb, IReadOnlyList<FeatureResult> results, Func<FeatureResult, Bag[,]> select)
    {
        sb.AppendLine("| Feature | Horizon | IS n | IS mean | IS median | IS hit | VAL mean | OOS n | OOS mean | OOS median | OOS hit | OOS vol |");
        sb.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var result in results)
        {
            var bags = select(result);
            for (var h = 0; h < CrossSectionCatalog.Horizons.Length; h++)
            {
                var ins = bags[h, 0];
                var val = bags[h, 1];
                var oos = bags[h, 2];
                sb.AppendLine($"| {result.Name} | {CrossSectionCatalog.Horizons[h].Name} | {ins.Count} | {Bps(ins.Mean)} | {Bps(ins.Median)} | {Hit(ins)} | {Bps(val.Mean)} | {oos.Count} | {Bps(oos.Mean)} | {Bps(oos.Median)} | {Hit(oos)} | {Bps(oos.Volatility)} |");
            }
        }
    }

    private static int HorizonIndex(string name)
    {
        for (var i = 0; i < CrossSectionCatalog.Horizons.Length; i++)
        {
            if (CrossSectionCatalog.Horizons[i].Name == name)
            {
                return i;
            }
        }

        return 0;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    private static string Bps(double value) => double.IsNaN(value) ? "n/a" : (value * 10000d).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Hit(Bag bag) => bag.Count == 0 ? "n/a" : bag.HitRate.ToString("0.0%", CultureInfo.InvariantCulture);

    private readonly struct Obs(float value, int symbol)
    {
        public float Value { get; } = value;
        public int Symbol { get; } = symbol;
    }

    private sealed class ObsComparer : IComparer<Obs>
    {
        public int Compare(Obs x, Obs y)
        {
            var value = x.Value.CompareTo(y.Value);
            return value != 0 ? value : x.Symbol.CompareTo(y.Symbol);
        }
    }

    private sealed class Bag
    {
        private readonly List<double> _spreads = [];
        private double _top;
        private double _bottom;
        private double _cost;
        private double _turn;

        public int Count => _spreads.Count;

        public double Mean => Count == 0 ? double.NaN : _spreads.Average();

        public double TopMean => Count == 0 ? double.NaN : _top / Count;

        public double BottomMean => Count == 0 ? double.NaN : _bottom / Count;

        public double Median
        {
            get
            {
                if (Count == 0)
                {
                    return double.NaN;
                }

                var ordered = _spreads.OrderBy(value => value).ToArray();
                return ordered[ordered.Length / 2];
            }
        }

        public double HitRate => Count == 0 ? double.NaN : _spreads.Count(value => value > 0d) / (double)Count;

        public double Volatility
        {
            get
            {
                if (Count < 2)
                {
                    return double.NaN;
                }

                var mean = Mean;
                return Math.Sqrt(_spreads.Sum(value => (value - mean) * (value - mean)) / (Count - 1));
            }
        }

        public double Turnover => Count == 0 ? double.NaN : _turn / Count;

        public double Net => Count == 0 ? double.NaN : Mean - (_cost / Count);

        public void AddSpread(double top, double bottom)
        {
            _spreads.Add(top - bottom);
            _top += top;
            _bottom += bottom;
        }

        public void AddRebalance(double gross, double cost)
        {
            _spreads.Add(gross);
            _cost += cost;
            _turn += cost / CrossSectionCatalog.RoundTripPerLeg / 2d;
        }
    }

    private sealed class FeatureResult
    {
        public FeatureResult(string name, int horizons)
        {
            Name = name;
            Decile = New(horizons, 3);
            Quintile = New(horizons, 3);
            DecileBlocks = New(horizons, 4);
            HighVol = new Bag[horizons];
            LowVol = new Bag[horizons];
            Rebalance = New(horizons, 3);
            for (var h = 0; h < horizons; h++)
            {
                HighVol[h] = new Bag();
                LowVol[h] = new Bag();
            }
        }

        public string Name { get; }
        public string Label { get; set; } = "";
        public Bag[,] Decile { get; }
        public Bag[,] Quintile { get; }
        public Bag[,] DecileBlocks { get; }
        public Bag[] HighVol { get; }
        public Bag[] LowVol { get; }
        public Bag[,] Rebalance { get; }
        public int Skipped { get; set; }
        public int Distinct { get; set; }
        public double MaxShare { get; set; }
        public string TopName { get; set; } = "";
        public int OosRanks { get; set; }
        public double MedianUniverse { get; set; }

        private static Bag[,] New(int rows, int cols)
        {
            var bags = new Bag[rows, cols];
            for (var r = 0; r < rows; r++)
            {
                for (var c = 0; c < cols; c++)
                {
                    bags[r, c] = new Bag();
                }
            }

            return bags;
        }
    }

    private sealed class Panel
    {
        public Panel(int symbols, int times)
        {
            Symbols = new string[symbols];
            Times = times;
            var length = symbols * times;
            High = new float[length];
            Low = new float[length];
            Close = new float[length];
            Volume = new float[length];
            Array.Fill(High, float.NaN);
            Array.Fill(Low, float.NaN);
            Array.Fill(Close, float.NaN);
            Array.Fill(Volume, float.NaN);
        }

        public string[] Symbols { get; }
        public int Times { get; }
        public int Stride => Symbols.Length;
        public int Length => Symbols.Length * Times;
        public float[] High { get; }
        public float[] Low { get; }
        public float[] Close { get; }
        public float[] Volume { get; }
        public DateTimeOffset From { get; set; }
        public DateTimeOffset To { get; set; }
    }

    private readonly record struct Bar(long Open, float High, float Low, float Close, float Volume);

    private readonly record struct CoverageNote(string Name, int Files, string Note);
}
