using System.Globalization;
using TradingPlatform.Backtesting.Validation;

namespace TradingPlatform.Research;

public sealed record Wave4MaeRow(
    string HypothesisId,
    string Phase,
    int Horizon,
    double LongMae,
    double LongMfe,
    double ShortMae,
    double ShortMfe);

public sealed record Wave4IncrementalRow(
    string HypothesisId,
    string Phase,
    double Ic24,
    double Spread24,
    double ExecSpread24,
    double IcVsBaseline);

public sealed record Wave4CoverageNote(
    string Dataset,
    string Source,
    string Status,
    string Coverage);

public sealed record Wave4SignalResult(
    IReadOnlyList<Wave3Hypothesis> Hypotheses,
    IReadOnlyList<Wave3IcRow> Ic,
    IReadOnlyList<Wave3BucketRow> Buckets,
    IReadOnlyList<Wave3RegimeRow> Regimes,
    IReadOnlyList<Wave4MaeRow> Path,
    IReadOnlyList<Wave4IncrementalRow> Incremental,
    IReadOnlyList<Wave3IcRow> UniverseIc,
    IReadOnlyList<Wave4CoverageNote> Coverage,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string> DataAudit);

public static class Wave4SignalResearch
{
    public static readonly int[] Horizons = [1, 2, 4, 8, 12, 24];
    public const int Warmup = 200;
    public const int VolLookback = 20;
    public const int AtrPeriod = 14;
    public const int OiShockLookback = 24;
    public const double RoundTripCost = 0.0012d;
    public const string Market = "BTCUSDT";

    public static IReadOnlyList<Wave3Hypothesis> Hypotheses { get; } =
    [
        new("W4_BASE_REL24", "24h return minus BTC; OHLCV-only baseline for incremental tests.", "continuation", "OHLCV+BTC"),
        new("W4_T1_IMB_CONT", "Taker buy imbalance (2*buy/vol-1) continues.", "continuation", "TakerBuyVolume"),
        new("W4_T2_IMB_REV", "Taker buy imbalance mean-reverts.", "reversal", "TakerBuyVolume"),
        new("W4_T3_DIMB", "Change in taker imbalance (acceleration) continues.", "continuation", "TakerBuyVolume"),
        new("W4_T4_FLOW_DIV", "Price up while takers sell (and vice versa) then reverses: -ret1 * imbalance.", "reversal", "OHLCV+Taker"),
        new("W4_T5_IMB_VOL", "Imbalance scaled by volume shock (vol/SMA20).", "continuation", "Taker+volume"),
        new("W4_T6_IMB_REL", "Imbalance scaled by 24h relative-to-BTC return.", "continuation", "Taker+OHLCV"),
        new("W4_OI_D1", "1h open-interest change continues.", "continuation", "Vision OI"),
        new("W4_OI_D24", "24h open-interest change continues.", "continuation", "Vision OI"),
        new("W4_OI_SHOCK", "OI change / trailing 24h std continues.", "continuation", "Vision OI"),
        new("W4_OI_CONFIRM", "Price and OI move together (ret24 * dOI24).", "continuation", "OHLCV+OI"),
        new("W4_M_LS_REV", "Account long/short ratio crowding reverses: -LS.", "reversal", "Vision LS ratio"),
        new("W4_M_TOP_LS_REV", "Top-trader long/short crowding reverses.", "reversal", "Vision top LS"),
        new("W4_M_TAKER_LS", "Vision 5m taker long/short volume ratio continues.", "continuation", "Vision taker LS"),
        new("W4_FO_CROWD_REV", "Positive funding with rising OI reverses: -funding * max(dOI24,0).", "reversal", "Funding+OI"),
        new("W4_TO_IMB_OI", "Taker imbalance times 1h OI change.", "continuation", "Taker+OI")
    ];

    public static Wave4SignalResult Evaluate(
        Wave3Panel panel,
        double[,]? oi,
        double[,]? oiValue,
        double[,]? lsRatio,
        double[,]? topLs,
        double[,]? metricsTakerLs,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>>? funding,
        IReadOnlyList<Wave3IcRow>? universeIc = null)
    {
        var btc = panel.IndexOf(Market);
        if (btc < 0)
        {
            throw new InvalidOperationException("BTCUSDT is required as the market benchmark.");
        }

        var n = panel.Length;
        var notes = new List<string>
        {
            "Wave-4 Stage 1: information-value of newly acquired series. No Isolated strategy promoted. LIVE=OFF.",
            "Ranks, OI, taker, and funding at t use only observations with timestamp <= candle close.",
            "Forward returns are labels, not features. 10-coin inner join on OpenTime. Terciles, not deciles.",
            $"Round-trip cost hurdle {RoundTripCost.ToString("P2", CultureInfo.InvariantCulture)}."
        };

        var takerOk = 0;
        var takerBars = 0;
        for (var t = Warmup; t < n; t++)
        {
            for (var s = 0; s < panel.Width; s++)
            {
                takerBars++;
                if (!double.IsNaN(Wave3Math.TakerImbalance(panel.Bars[t, s])))
                {
                    takerOk++;
                }
            }
        }

        notes.Add($"Taker imbalance valid on {takerOk}/{takerBars} bar-coins after warmup.");

        var oiOk = CountFinite(oi, Warmup);
        var lsOk = CountFinite(lsRatio, Warmup);
        notes.Add($"OI aligned finite after warmup: {oiOk}. LS ratio finite: {lsOk}.");

        var audit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OHLCV"] = "AVAILABLE (Wave-4 versioned kline cache /fapi/v1/klines)",
            ["BTC_OHLCV"] = "AVAILABLE",
            ["SYNC_OHLCV"] = "AVAILABLE (inner-join panel on OpenTime)",
            ["VOLUME"] = "AVAILABLE",
            ["TAKER"] = takerOk < n * panel.Width / 4
                ? $"DATA_UNAVAILABLE (valid {takerOk}/{takerBars})"
                : $"AVAILABLE kline field 9, valid {takerOk}/{takerBars}",
            ["OI"] = oiOk < n
                ? "DATA_UNAVAILABLE for this window"
                : $"AVAILABLE Binance Vision daily/metrics 5m last-observation <= close, finite {oiOk}",
            ["OI_VALUE"] = CountFinite(oiValue, Warmup) < n ? "NOT_USED" : "AVAILABLE sum_open_interest_value",
            ["LS_RATIO"] = lsOk < n ? "DATA_UNAVAILABLE" : "AVAILABLE Vision count_long_short_ratio",
            ["TOP_LS"] = CountFinite(topLs, Warmup) < n ? "DATA_UNAVAILABLE" : "AVAILABLE Vision sum_toptrader_long_short_ratio",
            ["METRICS_TAKER_LS"] = CountFinite(metricsTakerLs, Warmup) < n ? "DATA_UNAVAILABLE" : "AVAILABLE Vision sum_taker_long_short_vol_ratio",
            ["FUNDING"] = funding is { Count: > 0 } ? "AVAILABLE settled fundingTime <= close" : "NOT_LOADED",
            ["LIQUIDATION"] = "DATA_UNAVAILABLE (USD-M Vision liquidationSnapshot prefix empty; Binance stopped publishing)",
            ["DEPTH"] = "ARCHIVE_AVAILABLE_NOT_INGESTED (Vision daily/bookDepth from 2023; ~0.5MB/coin/day; not used this wave)",
            ["PREDICTED_FUNDING"] = "DATA_UNAVAILABLE",
            ["AGGTRADE"] = "ARCHIVE_AVAILABLE_NOT_INGESTED (Vision daily/aggTrades; redundant with kline taker for 1h)",
            ["XS_RANKS"] = "DERIVED at t from the aligned panel"
        };

        var oiD1 = oi is null ? NullGrid(n, panel.Width) : BinanceVisionClient.PctChange(oi, 1);
        var oiD24 = oi is null ? NullGrid(n, panel.Width) : BinanceVisionClient.PctChange(oi, 24);
        var oiShock = Shock(oiD1);
        var fundingGrid = AlignFunding(panel, funding);
        var (isEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(n);

        var signals = NewSignals(n, panel.Width);
        for (var t = Warmup; t < n; t++)
        {
            var rel24 = new double[panel.Width];
            var imb = new double[panel.Width];
            var dImb = new double[panel.Width];
            var volShock = new double[panel.Width];
            var btc24 = Wave3Math.LookbackReturn(panel, t, btc, 24);
            for (var s = 0; s < panel.Width; s++)
            {
                rel24[s] = Diff(Wave3Math.LookbackReturn(panel, t, s, 24), btc24);
                imb[s] = Wave3Math.TakerImbalance(panel.Bars[t, s]);
                var prevImb = t > 0 ? Wave3Math.TakerImbalance(panel.Bars[t - 1, s]) : double.NaN;
                dImb[s] = Diff(imb[s], prevImb);
                volShock[s] = Wave3Math.VolumeRatio(panel, t, s, VolLookback);
                var ret1 = Wave3Math.LookbackReturn(panel, t, s, 1);
                signals["W4_BASE_REL24"][t, s] = rel24[s];
                signals["W4_T1_IMB_CONT"][t, s] = imb[s];
                signals["W4_T2_IMB_REV"][t, s] = double.IsNaN(imb[s]) ? double.NaN : -imb[s];
                signals["W4_T3_DIMB"][t, s] = dImb[s];
                signals["W4_T4_FLOW_DIV"][t, s] = Mul(ret1, imb[s], negate: true);
                signals["W4_T5_IMB_VOL"][t, s] = Mul(imb[s], volShock[s]);
                signals["W4_T6_IMB_REL"][t, s] = Mul(imb[s], rel24[s]);
                signals["W4_OI_D1"][t, s] = oiD1[t, s];
                signals["W4_OI_D24"][t, s] = oiD24[t, s];
                signals["W4_OI_SHOCK"][t, s] = oiShock[t, s];
                signals["W4_OI_CONFIRM"][t, s] = Mul(Wave3Math.LookbackReturn(panel, t, s, 24), oiD24[t, s]);
                signals["W4_M_LS_REV"][t, s] = lsRatio is null ? double.NaN : Neg(lsRatio[t, s]);
                signals["W4_M_TOP_LS_REV"][t, s] = topLs is null ? double.NaN : Neg(topLs[t, s]);
                signals["W4_M_TAKER_LS"][t, s] = metricsTakerLs is null ? double.NaN : metricsTakerLs[t, s];
                var crowd = fundingGrid[t, s];
                var d24 = oiD24[t, s];
                signals["W4_FO_CROWD_REV"][t, s] =
                    double.IsNaN(crowd) || double.IsNaN(d24) || d24 <= 0 ? double.NaN : -crowd * d24;
                signals["W4_TO_IMB_OI"][t, s] = Mul(imb[s], oiD1[t, s]);
            }
        }

        var icRows = new List<Wave3IcRow>();
        var bucketRows = new List<Wave3BucketRow>();
        var regimeRows = new List<Wave3RegimeRow>();
        var pathRows = new List<Wave4MaeRow>();
        var btcAtrPct = BtcAtrPercentile(panel, btc);
        var btcTrend = BtcTrend(panel, btc);

        foreach (var hyp in Hypotheses)
        {
            var sig = signals[hyp.Id];
            foreach (var horizon in Horizons)
            {
                foreach (var (phase, from, to) in Phases(isEnd, valEnd, n))
                {
                    var acc = Accumulate(panel, sig, horizon, Math.Max(Warmup, from), to, btcTrend, btcAtrPct);
                    icRows.Add(acc.Ic with { HypothesisId = hyp.Id, Phase = phase, Horizon = horizon });
                    foreach (var b in acc.Buckets)
                    {
                        bucketRows.Add(b with { HypothesisId = hyp.Id, Phase = phase, Horizon = horizon });
                    }

                    if (horizon is 4 or 24)
                    {
                        foreach (var r in acc.Regimes)
                        {
                            regimeRows.Add(r with { HypothesisId = hyp.Id, Phase = phase, Horizon = horizon });
                        }
                    }

                    if (horizon == 24)
                    {
                        pathRows.Add(new Wave4MaeRow(hyp.Id, phase, horizon, acc.LongMae, acc.LongMfe, acc.ShortMae, acc.ShortMfe));
                    }
                }
            }
        }

        var incremental = new List<Wave4IncrementalRow>();
        foreach (var phase in new[] { "IS", "VALIDATION", "OOS" })
        {
            var baseline = icRows.FirstOrDefault(r => r.HypothesisId == "W4_BASE_REL24" && r.Phase == phase && r.Horizon == 24);
            var baseIc = baseline?.MeanCsIc ?? double.NaN;
            foreach (var hyp in Hypotheses)
            {
                var row = icRows.FirstOrDefault(r => r.HypothesisId == hyp.Id && r.Phase == phase && r.Horizon == 24);
                if (row is null)
                {
                    continue;
                }

                incremental.Add(new Wave4IncrementalRow(
                    hyp.Id,
                    phase,
                    row.MeanCsIc,
                    row.Spread,
                    row.MeanExecSpread,
                    Diff(row.MeanCsIc, baseIc)));
            }
        }

        _ = oiValue;
        return new Wave4SignalResult(
            Hypotheses,
            icRows,
            bucketRows,
            regimeRows,
            pathRows,
            incremental,
            universeIc ?? [],
            [],
            notes,
            audit);
    }

    public static string Classify(Wave3Hypothesis hyp, IReadOnlyList<Wave3IcRow> rows)
    {
        Wave3IcRow? Pick(string phase, int horizon) =>
            rows.FirstOrDefault(r => r.HypothesisId == hyp.Id && r.Phase == phase && r.Horizon == horizon);

        var is4 = Pick("IS", 4);
        var val4 = Pick("VALIDATION", 4);
        var is24 = Pick("IS", 24);
        var val24 = Pick("VALIDATION", 24);
        var oos4 = Pick("OOS", 4);
        var oos24 = Pick("OOS", 24);
        var isOk = Passes(is4) || Passes(is24);
        var valOk = Confirms(is4, val4) || Confirms(is24, val24);
        if (!isOk || !valOk)
        {
            return "REJECTED";
        }

        var oosOk = Confirms(is4, oos4) || Confirms(is24, oos24);
        if (!oosOk)
        {
            return "FRAGILE";
        }

        var isCost = ClearsCost(is4) || ClearsCost(is24);
        var oosCost = ClearsCost(oos4) || ClearsCost(oos24);
        return isCost && oosCost ? "PROMISING" : "FRAGILE";
    }

    private sealed record Acc(
        Wave3IcRow Ic,
        IReadOnlyList<Wave3BucketRow> Buckets,
        IReadOnlyList<Wave3RegimeRow> Regimes,
        double LongMae,
        double LongMfe,
        double ShortMae,
        double ShortMfe);

    private static Acc Accumulate(
        Wave3Panel panel,
        double[,] sig,
        int horizon,
        int from,
        int to,
        double[] btcTrend,
        double[] btcAtrPct)
    {
        var n = panel.Length;
        var cs = new List<double>();
        var top = new List<double>();
        var bot = new List<double>();
        var topExec = new List<double>();
        var botExec = new List<double>();
        var longMae = new List<double>();
        var longMfe = new List<double>();
        var shortMae = new List<double>();
        var shortMfe = new List<double>();
        var tsX = Enumerable.Range(0, panel.Width).Select(_ => new List<double>()).ToArray();
        var tsY = Enumerable.Range(0, panel.Width).Select(_ => new List<double>()).ToArray();
        var bucketObs = new List<double>[3];
        var bucketExec = new List<double>[3];
        for (var b = 0; b < 3; b++)
        {
            bucketObs[b] = [];
            bucketExec[b] = [];
        }

        var icUp = new List<double>();
        var icDn = new List<double>();
        var icLowVol = new List<double>();
        var icHighVol = new List<double>();
        var spUp = new List<double>();
        var spDn = new List<double>();
        var spLow = new List<double>();
        var spHigh = new List<double>();

        for (var t = from; t < to; t++)
        {
            if (t + horizon >= n)
            {
                break;
            }

            var x = new double[panel.Width];
            var y = new double[panel.Width];
            var ye = new double[panel.Width];
            for (var s = 0; s < panel.Width; s++)
            {
                x[s] = sig[t, s];
                y[s] = Wave3Math.ForwardCloseReturn(panel, t, s, horizon);
                ye[s] = Wave3Math.ForwardExecReturn(panel, t, s, horizon);
                if (!double.IsNaN(x[s]) && !double.IsNaN(y[s]))
                {
                    tsX[s].Add(x[s]);
                    tsY[s].Add(y[s]);
                }
            }

            var ic = Wave3Math.Spearman(x, y);
            if (!double.IsNaN(ic))
            {
                cs.Add(ic);
            }

            var ranks = Wave3Math.PercentileRanks(x);
            var tercileY = new List<double>[3];
            var tercileE = new List<double>[3];
            for (var b = 0; b < 3; b++)
            {
                tercileY[b] = [];
                tercileE[b] = [];
            }

            for (var s = 0; s < panel.Width; s++)
            {
                if (double.IsNaN(ranks[s]) || double.IsNaN(y[s]))
                {
                    continue;
                }

                var bucket = ranks[s] < 1.0 / 3.0 ? 0 : ranks[s] < 2.0 / 3.0 ? 1 : 2;
                tercileY[bucket].Add(y[s]);
                bucketObs[bucket].Add(y[s]);
                if (!double.IsNaN(ye[s]))
                {
                    tercileE[bucket].Add(ye[s]);
                    bucketExec[bucket].Add(ye[s]);
                }

                var (mae, mfe) = PathRange(panel, t, s, horizon);
                if (bucket == 2)
                {
                    longMae.Add(mae);
                    longMfe.Add(mfe);
                }
                else if (bucket == 0)
                {
                    shortMae.Add(mae);
                    shortMfe.Add(mfe);
                }
            }

            var topM = Wave3Math.Mean(tercileY[2]);
            var botM = Wave3Math.Mean(tercileY[0]);
            if (!double.IsNaN(topM))
            {
                top.Add(topM);
            }

            if (!double.IsNaN(botM))
            {
                bot.Add(botM);
            }

            var topE = Wave3Math.Mean(tercileE[2]);
            var botE = Wave3Math.Mean(tercileE[0]);
            if (!double.IsNaN(topE))
            {
                topExec.Add(topE);
            }

            if (!double.IsNaN(botE))
            {
                botExec.Add(botE);
            }

            var spread = !double.IsNaN(topM) && !double.IsNaN(botM) ? topM - botM : double.NaN;
            if (!double.IsNaN(ic))
            {
                if (btcTrend[t] > 0)
                {
                    icUp.Add(ic);
                }
                else if (btcTrend[t] < 0)
                {
                    icDn.Add(ic);
                }

                if (btcAtrPct[t] < 0.4)
                {
                    icLowVol.Add(ic);
                }
                else if (btcAtrPct[t] > 0.6)
                {
                    icHighVol.Add(ic);
                }
            }

            if (!double.IsNaN(spread))
            {
                if (btcTrend[t] > 0)
                {
                    spUp.Add(spread);
                }
                else if (btcTrend[t] < 0)
                {
                    spDn.Add(spread);
                }

                if (btcAtrPct[t] < 0.4)
                {
                    spLow.Add(spread);
                }
                else if (btcAtrPct[t] > 0.6)
                {
                    spHigh.Add(spread);
                }
            }
        }

        var tsIcs = new List<double>();
        for (var s = 0; s < panel.Width; s++)
        {
            var v = Wave3Math.Spearman(tsX[s], tsY[s]);
            if (!double.IsNaN(v))
            {
                tsIcs.Add(v);
            }
        }

        var topMean = Wave3Math.Mean(top);
        var botMean = Wave3Math.Mean(bot);
        var icRow = new Wave3IcRow(
            "",
            "",
            0,
            cs.Count,
            Wave3Math.Mean(cs),
            Wave3Math.Mean(tsIcs),
            topMean,
            botMean,
            topMean - botMean,
            topMean,
            double.IsNaN(botMean) ? double.NaN : -botMean,
            Wave3Math.HitRate(top),
            Wave3Math.HitRate(bot.Select(v => -v)),
            Wave3Math.Mean(topExec) - Wave3Math.Mean(botExec));

        var buckets = new List<Wave3BucketRow>();
        for (var b = 0; b < 3; b++)
        {
            buckets.Add(new Wave3BucketRow(
                "",
                "",
                0,
                b,
                bucketObs[b].Count,
                Wave3Math.Mean(bucketObs[b]),
                Wave3Math.Median(bucketObs[b]),
                Wave3Math.HitRate(bucketObs[b]),
                Wave3Math.Mean(bucketExec[b])));
        }

        var regimes = new List<Wave3RegimeRow>
        {
            new("", "BTC_TREND_UP", "", 0, icUp.Count, Wave3Math.Mean(icUp), Wave3Math.Mean(spUp)),
            new("", "BTC_TREND_DOWN", "", 0, icDn.Count, Wave3Math.Mean(icDn), Wave3Math.Mean(spDn)),
            new("", "BTC_VOL_LOW", "", 0, icLowVol.Count, Wave3Math.Mean(icLowVol), Wave3Math.Mean(spLow)),
            new("", "BTC_VOL_HIGH", "", 0, icHighVol.Count, Wave3Math.Mean(icHighVol), Wave3Math.Mean(spHigh))
        };

        return new Acc(
            icRow,
            buckets,
            regimes,
            Wave3Math.Mean(longMae),
            Wave3Math.Mean(longMfe),
            Wave3Math.Mean(shortMae),
            Wave3Math.Mean(shortMfe));
    }

    private static (double Mae, double Mfe) PathRange(Wave3Panel panel, int t, int symbolIndex, int horizon)
    {
        var start = panel.Bars[t, symbolIndex].Close;
        if (start <= 0m)
        {
            return (double.NaN, double.NaN);
        }

        decimal min = start;
        decimal max = start;
        var last = Math.Min(panel.Length - 1, t + horizon);
        for (var i = t + 1; i <= last; i++)
        {
            var bar = panel.Bars[i, symbolIndex];
            if (bar.Low < min)
            {
                min = bar.Low;
            }

            if (bar.High > max)
            {
                max = bar.High;
            }
        }

        return ((double)((min - start) / start), (double)((max - start) / start));
    }

    private static Dictionary<string, double[,]> NewSignals(int n, int w)
    {
        var signals = new Dictionary<string, double[,]>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in Hypotheses)
        {
            var grid = new double[n, w];
            for (var t = 0; t < n; t++)
            {
                for (var s = 0; s < w; s++)
                {
                    grid[t, s] = double.NaN;
                }
            }

            signals[h.Id] = grid;
        }

        return signals;
    }

    private static double[,] AlignFunding(Wave3Panel panel, IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>>? funding)
    {
        var grid = NullGrid(panel.Length, panel.Width);
        if (funding is null)
        {
            return grid;
        }

        for (var s = 0; s < panel.Width; s++)
        {
            if (!funding.TryGetValue(panel.Symbols[s], out var series) || series.Count == 0)
            {
                continue;
            }

            var p = 0;
            for (var t = 0; t < panel.Length; t++)
            {
                var close = panel.Bars[t, s].CloseTime;
                while (p + 1 < series.Count && series[p + 1].FundingTime <= close)
                {
                    p++;
                }

                if (p < series.Count && series[p].FundingTime <= close)
                {
                    grid[t, s] = (double)series[p].FundingRate;
                }
            }
        }

        return grid;
    }

    private static double[,] Shock(double[,] d)
    {
        var n = d.GetLength(0);
        var w = d.GetLength(1);
        var s = new double[n, w];
        for (var t = 0; t < n; t++)
        {
            for (var c = 0; c < w; c++)
            {
                if (t < OiShockLookback)
                {
                    s[t, c] = double.NaN;
                    continue;
                }

                var mean = 0d;
                var count = 0;
                for (var i = t - OiShockLookback + 1; i <= t; i++)
                {
                    if (double.IsNaN(d[i, c]))
                    {
                        continue;
                    }

                    mean += d[i, c];
                    count++;
                }

                if (count < 8 || double.IsNaN(d[t, c]))
                {
                    s[t, c] = double.NaN;
                    continue;
                }

                mean /= count;
                var var = 0d;
                for (var i = t - OiShockLookback + 1; i <= t; i++)
                {
                    if (double.IsNaN(d[i, c]))
                    {
                        continue;
                    }

                    var z = d[i, c] - mean;
                    var += z * z;
                }

                var std = Math.Sqrt(var / count);
                s[t, c] = std < 1e-12 ? double.NaN : d[t, c] / std;
            }
        }

        return s;
    }

    private static double[,] NullGrid(int n, int w)
    {
        var g = new double[n, w];
        for (var t = 0; t < n; t++)
        {
            for (var s = 0; s < w; s++)
            {
                g[t, s] = double.NaN;
            }
        }

        return g;
    }

    private static int CountFinite(double[,]? grid, int warmup)
    {
        if (grid is null)
        {
            return 0;
        }

        var n = 0;
        for (var t = warmup; t < grid.GetLength(0); t++)
        {
            for (var s = 0; s < grid.GetLength(1); s++)
            {
                if (!double.IsNaN(grid[t, s]))
                {
                    n++;
                }
            }
        }

        return n;
    }

    private static bool Passes(Wave3IcRow? row) =>
        row is not null
        && row.Observations >= 200
        && !double.IsNaN(row.MeanCsIc)
        && row.MeanCsIc > 0.02
        && !double.IsNaN(row.Spread)
        && row.Spread > 0;

    private static bool Confirms(Wave3IcRow? a, Wave3IcRow? b) =>
        a is not null && b is not null && !double.IsNaN(a.MeanCsIc) && !double.IsNaN(b.MeanCsIc) && Math.Sign(a.MeanCsIc) == Math.Sign(b.MeanCsIc) && b.MeanCsIc > 0;

    private static bool ClearsCost(Wave3IcRow? row) =>
        row is not null && !double.IsNaN(row.MeanExecSpread) && row.MeanExecSpread > RoundTripCost;

    private static IEnumerable<(string Phase, int From, int To)> Phases(int isEnd, int valEnd, int n)
    {
        yield return ("IS", 0, isEnd);
        yield return ("VALIDATION", isEnd, valEnd);
        yield return ("OOS", valEnd, n);
    }

    private static double Diff(double a, double b) =>
        double.IsNaN(a) || double.IsNaN(b) ? double.NaN : a - b;

    private static double Neg(double a) => double.IsNaN(a) ? double.NaN : -a;

    private static double Mul(double a, double b, bool negate = false)
    {
        if (double.IsNaN(a) || double.IsNaN(b))
        {
            return double.NaN;
        }

        var v = a * b;
        return negate ? -v : v;
    }

    private static double[] BtcAtrPercentile(Wave3Panel panel, int btc)
    {
        var n = panel.Length;
        var atr = new double[n];
        var pct = new double[n];
        for (var t = 0; t < n; t++)
        {
            atr[t] = Wave3Math.AtrPercent(panel, t, btc, AtrPeriod);
            pct[t] = double.NaN;
        }

        const int look = 50;
        for (var t = Warmup; t < n; t++)
        {
            if (double.IsNaN(atr[t]))
            {
                continue;
            }

            var lo = Math.Max(0, t - look + 1);
            var count = 0;
            var below = 0;
            for (var i = lo; i <= t; i++)
            {
                if (double.IsNaN(atr[i]))
                {
                    continue;
                }

                count++;
                if (atr[i] <= atr[t])
                {
                    below++;
                }
            }

            pct[t] = count == 0 ? double.NaN : below / (double)count;
        }

        return pct;
    }

    private static double[] BtcTrend(Wave3Panel panel, int btc)
    {
        var n = panel.Length;
        var ema20 = Ema(panel, btc, 20);
        var ema50 = Ema(panel, btc, 50);
        var d = new double[n];
        for (var t = 0; t < n; t++)
        {
            d[t] = double.IsNaN(ema20[t]) || double.IsNaN(ema50[t]) ? double.NaN : Math.Sign(ema20[t] - ema50[t]);
        }

        return d;
    }

    private static double[] Ema(Wave3Panel panel, int symbolIndex, int period)
    {
        var n = panel.Length;
        var e = new double[n];
        var k = 2.0 / (period + 1);
        double? prev = null;
        for (var t = 0; t < n; t++)
        {
            var close = (double)panel.Bars[t, symbolIndex].Close;
            if (prev is null)
            {
                prev = close;
                e[t] = t + 1 >= period ? close : double.NaN;
                continue;
            }

            prev = close * k + prev.Value * (1 - k);
            e[t] = t + 1 >= period ? prev.Value : double.NaN;
        }

        return e;
    }
}
