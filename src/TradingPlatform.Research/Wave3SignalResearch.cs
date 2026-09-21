using System.Globalization;
using TradingPlatform.Backtesting.Validation;

namespace TradingPlatform.Research;

public sealed record Wave3Hypothesis(
    string Id,
    string Mechanism,
    string Direction,
    string DataUsed);

public sealed record Wave3BucketRow(
    string HypothesisId,
    string Phase,
    int Horizon,
    int Bucket,
    int Observations,
    double MeanFwd,
    double MedianFwd,
    double HitRate,
    double MeanExecFwd);

public sealed record Wave3IcRow(
    string HypothesisId,
    string Phase,
    int Horizon,
    int Observations,
    double MeanCsIc,
    double MeanTsIc,
    double TopTercileMean,
    double BottomTercileMean,
    double Spread,
    double LongMean,
    double ShortMean,
    double HitTop,
    double HitBottom,
    double MeanExecSpread);

public sealed record Wave3RegimeRow(
    string HypothesisId,
    string Regime,
    string Phase,
    int Horizon,
    int Observations,
    double MeanCsIc,
    double Spread);

public sealed record Wave3FundingRow(
    string HypothesisId,
    string Phase,
    int HorizonHours,
    int Observations,
    double Ic,
    double HighFundingMeanFwd,
    double LowFundingMeanFwd);

public sealed record Wave3SignalResult(
    IReadOnlyList<Wave3Hypothesis> Hypotheses,
    IReadOnlyList<Wave3IcRow> Ic,
    IReadOnlyList<Wave3BucketRow> Buckets,
    IReadOnlyList<Wave3RegimeRow> Regimes,
    IReadOnlyList<Wave3FundingRow> Funding,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string> DataAudit);

public static class Wave3SignalResearch
{
    public static readonly int[] Horizons = [1, 2, 4, 8, 12, 24];
    public const int Warmup = 200;
    public const int BetaLookback = 168;
    public const int VolLookback = 20;
    public const int AtrPeriod = 14;
    public const double RoundTripCost = 0.0012d;
    public const string Market = "BTCUSDT";

    public static IReadOnlyList<Wave3Hypothesis> Hypotheses { get; } =
    [
        new("H1_REL_1H_CONT", "1h return minus BTC 1h return; stronger relative coins continue.", "continuation", "OHLCV+BTC"),
        new("H2_REL_4H_CONT", "4h return minus BTC; relative strength continuation.", "continuation", "OHLCV+BTC"),
        new("H3_REL_12H_CONT", "12h return minus BTC; relative strength continuation.", "continuation", "OHLCV+BTC"),
        new("H4_REL_24H_CONT", "24h return minus BTC; relative strength continuation.", "continuation", "OHLCV+BTC"),
        new("H5_REL_24H_REV", "24h relative strength mean-reverts over the next day.", "reversal", "OHLCV+BTC"),
        new("H6_REL_24H_VOLADJ_CONT", "24h relative return / ATR%. Vol-adjusted residual momentum.", "continuation", "OHLCV+BTC"),
        new("H7_RESID_24H_CONT", "24h return minus beta*BTC, beta from past 168 hourly returns.", "continuation", "OHLCV+BTC"),
        new("H8_RESID_24H_REV", "Residual 24h mean-reverts.", "reversal", "OHLCV+BTC"),
        new("H9_VOL_SHOCK_XS", "Volume / SMA20 ranked cross-sectionally; high shock continues with the 24h relative sign.", "continuation", "OHLCV volume"),
        new("H10_TAKER_IMB", "Taker buy imbalance (2*buy/vol-1) continuation.", "continuation", "TakerBuyVolume")
    ];

    public static Wave3SignalResult Evaluate(
        Wave3Panel panel,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>>? funding = null)
    {
        var btc = panel.IndexOf(Market);
        if (btc < 0)
        {
            throw new InvalidOperationException("BTCUSDT is required as the market benchmark.");
        }

        var n = panel.Length;
        var notes = new List<string>
        {
            "Stage 1 signal research only. No new Isolated strategy was promoted. LIVE=OFF.",
            "Ranks and beta at t use only bars with OpenTime <= t. Forward returns use t+h, which is labeling, not a feature.",
            "10-coin panel is an inner join on OpenTime. Missing coins at a timestamp drop that row.",
            "Terciles are used instead of deciles because n=10. A 10% decile would be one coin and is not reported as a decile result.",
            $"Round-trip cost hurdle {RoundTripCost.ToString("P2", CultureInfo.InvariantCulture)} (0.04%*2 commission + 0.02%*2 slippage)."
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

        var audit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OHLCV"] = "AVAILABLE (research kline cache /fapi/v1/klines)",
            ["BTC_OHLCV"] = "AVAILABLE (BTCUSDT in panel)",
            ["SYNC_OHLCV"] = "AVAILABLE (inner-join panel on OpenTime)",
            ["VOLUME"] = "AVAILABLE",
            ["TAKER"] = takerOk < 1000
                ? $"DATA_UNAVAILABLE for the 2-year window (TakerBuyVolume>0 on {takerOk}/{takerBars} bar-coins in this cache; not backfilled)"
                : "AVAILABLE when TakerBuyVolume>0 and <=Volume; else bar skipped",
            ["FUNDING"] = funding is { Count: > 0 } ? "AVAILABLE (settled fundingTime <= bar close)" : "NOT_LOADED",
            ["OI"] = "DATA_UNAVAILABLE for 2-year window (public hist ~30d)",
            ["MARK"] = "AVAILABLE on disk for prior 10-coin ingest; not required for XS OHLCV signals",
            ["INDEX"] = "AVAILABLE on disk for prior 10-coin ingest; not required for XS OHLCV signals",
            ["LIQUIDATION"] = "DATA_UNAVAILABLE",
            ["PREDICTED_FUNDING"] = "DATA_UNAVAILABLE (premiumIndex snapshot is not historical)",
            ["XS_RANKS"] = "DERIVED at t from the aligned panel; not a stored universe snapshot"
        };

        var coinRets = Enumerable.Range(0, panel.Width).Select(s => Wave3Math.HourlyReturns(panel, s)).ToArray();
        var btcRets = coinRets[btc];
        var (isEnd, valEnd) = StrategyValidation.ChronologicalSplitIndices(n);

        var signals = new Dictionary<string, double[,]>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in Hypotheses)
        {
            signals[h.Id] = new double[n, panel.Width];
            for (var t = 0; t < n; t++)
            {
                for (var s = 0; s < panel.Width; s++)
                {
                    signals[h.Id][t, s] = double.NaN;
                }
            }
        }

        for (var t = Warmup; t < n; t++)
        {
            var rel1 = new double[panel.Width];
            var rel4 = new double[panel.Width];
            var rel12 = new double[panel.Width];
            var rel24 = new double[panel.Width];
            var volAdj = new double[panel.Width];
            var resid = new double[panel.Width];
            var volShock = new double[panel.Width];
            var taker = new double[panel.Width];
            var btc1 = Wave3Math.LookbackReturn(panel, t, btc, 1);
            var btc4 = Wave3Math.LookbackReturn(panel, t, btc, 4);
            var btc12 = Wave3Math.LookbackReturn(panel, t, btc, 12);
            var btc24 = Wave3Math.LookbackReturn(panel, t, btc, 24);
            for (var s = 0; s < panel.Width; s++)
            {
                rel1[s] = Diff(Wave3Math.LookbackReturn(panel, t, s, 1), btc1);
                rel4[s] = Diff(Wave3Math.LookbackReturn(panel, t, s, 4), btc4);
                rel12[s] = Diff(Wave3Math.LookbackReturn(panel, t, s, 12), btc12);
                rel24[s] = Diff(Wave3Math.LookbackReturn(panel, t, s, 24), btc24);
                var atr = Wave3Math.AtrPercent(panel, t, s, AtrPeriod);
                volAdj[s] = double.IsNaN(rel24[s]) || double.IsNaN(atr) || atr < 1e-8 ? double.NaN : rel24[s] / atr;
                var beta = Wave3Math.Beta(coinRets[s], btcRets, t - BetaLookback, t - 1);
                resid[s] = double.IsNaN(beta) || double.IsNaN(btc24)
                    ? double.NaN
                    : Wave3Math.LookbackReturn(panel, t, s, 24) - beta * btc24;
                volShock[s] = Wave3Math.VolumeRatio(panel, t, s, VolLookback);
                taker[s] = Wave3Math.TakerImbalance(panel.Bars[t, s]);
            }

            var volRank = Wave3Math.PercentileRanks(volShock);
            for (var s = 0; s < panel.Width; s++)
            {
                signals["H1_REL_1H_CONT"][t, s] = rel1[s];
                signals["H2_REL_4H_CONT"][t, s] = rel4[s];
                signals["H3_REL_12H_CONT"][t, s] = rel12[s];
                signals["H4_REL_24H_CONT"][t, s] = rel24[s];
                signals["H5_REL_24H_REV"][t, s] = double.IsNaN(rel24[s]) ? double.NaN : -rel24[s];
                signals["H6_REL_24H_VOLADJ_CONT"][t, s] = volAdj[s];
                signals["H7_RESID_24H_CONT"][t, s] = resid[s];
                signals["H8_RESID_24H_REV"][t, s] = double.IsNaN(resid[s]) ? double.NaN : -resid[s];
                signals["H9_VOL_SHOCK_XS"][t, s] = double.IsNaN(volRank[s]) || double.IsNaN(rel24[s])
                    ? double.NaN
                    : volRank[s] * Math.Sign(rel24[s]);
                signals["H10_TAKER_IMB"][t, s] = taker[s];
            }
        }

        var icRows = new List<Wave3IcRow>();
        var bucketRows = new List<Wave3BucketRow>();
        var regimeRows = new List<Wave3RegimeRow>();
        var btcAtrPct = BtcAtrPercentile(panel, btc);
        var btcTrend = BtcTrend(panel, btc);

        foreach (var hyp in Hypotheses)
        {
            var sig = signals[hyp.Id];
            foreach (var horizon in Horizons)
            {
                foreach (var (phase, from, to) in Phases(isEnd, valEnd, n))
                {
                    var cs = new List<double>();
                    var top = new List<double>();
                    var bot = new List<double>();
                    var topExec = new List<double>();
                    var botExec = new List<double>();
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

                    for (var t = Math.Max(Warmup, from); t < to; t++)
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
                    icRows.Add(new Wave3IcRow(
                        hyp.Id,
                        phase,
                        horizon,
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
                        Wave3Math.Mean(topExec) - Wave3Math.Mean(botExec)));

                    for (var b = 0; b < 3; b++)
                    {
                        bucketRows.Add(new Wave3BucketRow(
                            hyp.Id,
                            phase,
                            horizon,
                            b,
                            bucketObs[b].Count,
                            Wave3Math.Mean(bucketObs[b]),
                            Wave3Math.Median(bucketObs[b]),
                            Wave3Math.HitRate(bucketObs[b]),
                            Wave3Math.Mean(bucketExec[b])));
                    }

                    if (horizon is 4 or 24)
                    {
                        regimeRows.Add(new Wave3RegimeRow(hyp.Id, "BTC_TREND_UP", phase, horizon, icUp.Count, Wave3Math.Mean(icUp), Wave3Math.Mean(spUp)));
                        regimeRows.Add(new Wave3RegimeRow(hyp.Id, "BTC_TREND_DOWN", phase, horizon, icDn.Count, Wave3Math.Mean(icDn), Wave3Math.Mean(spDn)));
                        regimeRows.Add(new Wave3RegimeRow(hyp.Id, "BTC_VOL_LOW", phase, horizon, icLowVol.Count, Wave3Math.Mean(icLowVol), Wave3Math.Mean(spLow)));
                        regimeRows.Add(new Wave3RegimeRow(hyp.Id, "BTC_VOL_HIGH", phase, horizon, icHighVol.Count, Wave3Math.Mean(icHighVol), Wave3Math.Mean(spHigh)));
                    }
                }
            }
        }

        var fundingRows = EvaluateFunding(panel, btc, funding, isEnd, valEnd, notes, audit);
        return new Wave3SignalResult(Hypotheses, icRows, bucketRows, regimeRows, fundingRows, notes, audit);
    }

    public static string Classify(Wave3Hypothesis hyp, IReadOnlyList<Wave3IcRow> rows)
    {
        Wave3IcRow? Pick(string phase, int horizon) =>
            rows.FirstOrDefault(r => r.HypothesisId == hyp.Id && r.Phase == phase && r.Horizon == horizon);

        var is4 = Pick("IS", 4);
        var val4 = Pick("VALIDATION", 4);
        var oos4 = Pick("OOS", 4);
        var is24 = Pick("IS", 24);
        var val24 = Pick("VALIDATION", 24);
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

        var costOk = (is4 is not null && is4.MeanExecSpread > RoundTripCost)
            || (is24 is not null && is24.MeanExecSpread > RoundTripCost);
        return costOk ? "PROMISING" : "FRAGILE";
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

    private static IEnumerable<(string Phase, int From, int To)> Phases(int isEnd, int valEnd, int n)
    {
        yield return ("IS", 0, isEnd);
        yield return ("VALIDATION", isEnd, valEnd);
        yield return ("OOS", valEnd, n);
    }

    private static double Diff(double a, double b) =>
        double.IsNaN(a) || double.IsNaN(b) ? double.NaN : a - b;

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

    private static List<Wave3FundingRow> EvaluateFunding(
        Wave3Panel panel,
        int btc,
        IReadOnlyDictionary<string, IReadOnlyList<FundingPoint>>? funding,
        int isEnd,
        int valEnd,
        List<string> notes,
        Dictionary<string, string> audit)
    {
        var rows = new List<Wave3FundingRow>();
        if (funding is null || funding.Count == 0)
        {
            notes.Add("Funding series was not loaded; H_FUNDING not evaluated.");
            return rows;
        }

        var min = funding.Values.Min(v => v.Count);
        var span = funding.Values
            .Where(v => v.Count >= 2)
            .Select(v => (v[^1].FundingTime - v[0].FundingTime).TotalDays)
            .DefaultIfEmpty(0)
            .Min();
        if (span < 400)
        {
            audit["FUNDING"] = $"INSUFFICIENT for 2-year claim (min span {span:0}d, min n={min}). Not treated as 2-year evidence.";
            notes.Add($"Funding span {span:0} days. Wave-3 funding IC is labeled sample-limited.");
        }
        else
        {
            audit["FUNDING"] = $"AVAILABLE settled rates, min n={min}, span {span:0}d. Predicted next rate not used.";
        }

        var pointers = panel.Symbols.ToDictionary(s => s, _ => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var (phase, from, to) in Phases(isEnd, valEnd, panel.Length))
        {
            var xs = new List<double>();
            var y8 = new List<double>();
            var y24 = new List<double>();
            var high8 = new List<double>();
            var low8 = new List<double>();
            for (var t = Math.Max(Warmup, from); t < to; t++)
            {
                for (var s = 0; s < panel.Width; s++)
                {
                    var symbol = panel.Symbols[s];
                    if (!funding.TryGetValue(symbol, out var series) || series.Count == 0)
                    {
                        continue;
                    }

                    var close = panel.Bars[t, s].CloseTime;
                    var pidx = pointers[symbol];
                    while (pidx + 1 < series.Count && series[pidx + 1].FundingTime <= close)
                    {
                        pidx++;
                    }

                    pointers[symbol] = pidx;
                    if (pidx >= series.Count || series[pidx].FundingTime > close)
                    {
                        continue;
                    }

                    var rate = (double)series[pidx].FundingRate;
                    xs.Add(rate);
                    var f8 = Wave3Math.ForwardCloseReturn(panel, t, s, 8);
                    var f24 = Wave3Math.ForwardCloseReturn(panel, t, s, 24);
                    y8.Add(f8);
                    y24.Add(f24);
                    if (rate > 0.0003)
                    {
                        high8.Add(f8);
                    }

                    if (rate < -0.0003)
                    {
                        low8.Add(f8);
                    }
                }
            }

            rows.Add(new Wave3FundingRow("H_FUND_EXTREME", phase, 8, xs.Count, Wave3Math.Spearman(xs, y8), Wave3Math.Mean(high8), Wave3Math.Mean(low8)));
            rows.Add(new Wave3FundingRow("H_FUND_EXTREME", phase, 24, xs.Count, Wave3Math.Spearman(xs, y24), double.NaN, double.NaN));
        }

        _ = btc;
        return rows;
    }
}
