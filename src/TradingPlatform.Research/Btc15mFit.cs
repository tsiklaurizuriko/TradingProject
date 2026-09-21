using System.Collections.Concurrent;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed record Btc15mCandidate(
    string Recipe,
    string Rules,
    string Side,
    double SlPct,
    double TpPct,
    int MaxHoldBars,
    int Complexity,
    int Trades,
    double Net,
    double FinalEquity,
    double Pf,
    double MaxDdPct,
    double WinRate,
    double Expectancy,
    double Fees,
    double Slippage,
    int LongTrades,
    int ShortTrades,
    double LongPnl,
    double ShortPnl,
    double Year1Net,
    double Year2Net,
    int MonthWins,
    int MonthLosses,
    double Score);

public sealed record Btc15mTrade(
    DateTimeOffset Opened,
    DateTimeOffset Closed,
    int Side,
    double Entry,
    double Exit,
    double Qty,
    double Gross,
    double Fees,
    double Slip,
    double Net,
    string Reason,
    int HoldBars);

public sealed record Btc15mFitResult(
    DateTimeOffset DataStart,
    DateTimeOffset DataEnd,
    int Bars,
    int CombinationsTested,
    int RecipesTested,
    IReadOnlyList<Btc15mCandidate> AllFeasible,
    IReadOnlyList<Btc15mCandidate> Top20,
    IReadOnlyList<Btc15mCandidate> Closest10,
    Btc15mCandidate? Selected,
    IReadOnlyList<Btc15mTrade> Trades,
    IReadOnlyList<(DateTimeOffset T, double Equity)> Equity,
    IReadOnlyDictionary<string, double> Baselines,
    IReadOnlyList<string> Notes,
    string Classification);

public static class Btc15mFit
{
    public const double Fee = 0.0004;
    public const double Slip = 0.0002;
    public const double StartEquity = 1000;
    public const double RiskPct = 0.005;
    public const double LevCap = 3;
    public const double DailyHalt = 0.03;
    public const int Warmup = 120;
    public const int MinTrades = 2000;
    public const double MinNet = 100;

    public static readonly double[] SlGrid = [0.0025, 0.005, 0.0075, 0.01, 0.015, 0.02, 0.025, 0.03, 0.04];
    public static readonly double[] TpGrid = [0.0025, 0.005, 0.0075, 0.01, 0.015, 0.02, 0.025, 0.03, 0.04, 0.05, 0.06];
    public static readonly int[] HoldGrid = [8, 16, 32, 48, 96, 192];

    public static Btc15mFitResult Run(IReadOnlyList<MarketCandle> candles)
    {
        var notes = new List<string>
        {
            "BTCUSDT 15m historically fitted strategy. RESEARCH ONLY. Not validated alpha. LIVE off. Registry unchanged.",
            "Model B: signal on closed 15m, fill next open, SL before TP on the same bar.",
            "Sizing: Isolated LOW $1,000 start, 0.5% of current equity, 3x cap, daily 3% halt, one position.",
            $"Costs: taker 0.04% per side + 0.02% slip in fill prices. Funding not applied (not in this kline replay)."
        };

        var n = candles.Count;
        var open = new double[n];
        var high = new double[n];
        var low = new double[n];
        var close = new double[n];
        var vol = new double[n];
        var times = new DateTimeOffset[n];
        for (var i = 0; i < n; i++)
        {
            open[i] = (double)candles[i].Open;
            high[i] = (double)candles[i].High;
            low[i] = (double)candles[i].Low;
            close[i] = (double)candles[i].Close;
            vol[i] = (double)candles[i].Volume;
            times[i] = candles[i].OpenTime;
        }

        var dataStart = times[0];
        var dataEnd = candles[^1].CloseTime;
        notes.Add($"Dataset {dataStart:yyyy-MM-dd HH:mm} UTC → {dataEnd:yyyy-MM-dd HH:mm} UTC. Bars={n}.");
        var mid = dataStart + (dataEnd - dataStart) / 2;

        var cache = new CausalIndicatorCache(candles);
        var recipes = BuildRecipes(cache, close, high, low, vol, n);
        notes.Add($"Entry recipes: {recipes.Count}. Sides: long/short/both. SL {SlGrid.Length} × TP {TpGrid.Length} × hold {HoldGrid.Length}.");
        var combos = recipes.Count * 3 * SlGrid.Length * TpGrid.Length * HoldGrid.Length;
        notes.Add($"Declared combinations: {combos}.");

        var bag = new ConcurrentBag<Btc15mCandidate>();
        Parallel.ForEach(recipes, recipe =>
        {
            foreach (var side in new[] { "LONG", "SHORT", "BOTH" })
            {
                var mask = side == "LONG" ? 1 : side == "SHORT" ? 2 : 3;
                foreach (var sl in SlGrid)
                {
                    foreach (var tp in TpGrid)
                    {
                        foreach (var hold in HoldGrid)
                        {
                            var sim = Simulate(open, high, low, close, times, recipe.Signal, mask, sl, tp, hold, mid, log: false);
                            bag.Add(ToCandidate(recipe, side, sl, tp, hold, sim));
                        }
                    }
                }
            }
        });

        var all = bag.ToList();
        var feasible = all.Where(c => c.Trades >= MinTrades && c.Net >= MinNet && c.FinalEquity >= StartEquity + MinNet)
            .OrderByDescending(c => c.Score)
            .ToList();
        notes.Add($"Combinations with ≥{MinTrades} trades: {all.Count(c => c.Trades >= MinTrades)}.");
        notes.Add($"Combinations with ≥${MinNet:0} net: {all.Count(c => c.Net >= MinNet)}.");
        notes.Add($"Intersection (feasible): {feasible.Count}.");
        var rankedForTop = feasible.Count > 0 ? feasible : all.OrderByDescending(c => c.Score).ToList();
        var top20 = rankedForTop.Take(20).ToList();
        var closest = Closest(all);
        Btc15mCandidate? selected = feasible.FirstOrDefault();
        List<Btc15mTrade> trades = [];
        List<(DateTimeOffset, double)> equity = [];
        var baselines = new Dictionary<string, double>();

        if (selected is not null)
        {
            var recipe = recipes.First(r => r.Id == selected.Recipe);
            var mask = selected.Side == "LONG" ? 1 : selected.Side == "SHORT" ? 2 : 3;
            var logged = Simulate(open, high, low, close, times, recipe.Signal, mask, selected.SlPct, selected.TpPct, selected.MaxHoldBars, mid, log: true);
            selected = ToCandidate(recipe, selected.Side, selected.SlPct, selected.TpPct, selected.MaxHoldBars, logged);
            trades = logged.Trades;
            equity = logged.Equity;
            baselines = Baselines(open, high, low, close, times, recipe.Signal, mask, selected, mid, trades.Count);
            notes.Add($"Selected {selected.Recipe} {selected.Side} SL {selected.SlPct:P2} TP {selected.TpPct:P2} hold {selected.MaxHoldBars} bars.");
        }
        else
        {
            notes.Add("NO STRATEGY FOUND UNDER CONSTRAINTS.");
        }

        return new Btc15mFitResult(
            dataStart,
            dataEnd,
            n,
            combos,
            recipes.Count,
            feasible,
            top20,
            closest,
            selected,
            trades,
            equity,
            baselines,
            notes,
            selected is null ? "NO STRATEGY FOUND UNDER CONSTRAINTS" : "HISTORICALLY FITTED STRATEGY");
    }

    private sealed record Recipe(string Id, int Complexity, string Rules, sbyte[] Signal);

    private static List<Recipe> BuildRecipes(
        CausalIndicatorCache cache,
        double[] close,
        double[] high,
        double[] low,
        double[] vol,
        int n)
    {
        var ema5 = D(cache.Ema(5));
        var ema8 = D(cache.Ema(8));
        var ema12 = D(cache.Ema(12));
        var ema13 = D(cache.Ema(13));
        var ema20 = D(cache.Ema(20));
        var ema21 = D(cache.Ema(21));
        var ema26 = D(cache.Ema(26));
        var ema50 = D(cache.Ema(50));
        var rsi7 = D(cache.Rsi(7));
        var rsi14 = D(cache.Rsi(14));
        var adx = D(cache.Adx(14));
        var relVol = D(cache.RelativeVolume(20));
        var z20 = D(cache.CloseZScore(20));
        var (macd, macdSig, _) = cache.Macd(12, 26, 9);
        var macdD = D(macd);
        var macdS = D(macdSig);
        var bb = cache.Bollinger(20, 2m);
        var bbU = D(bb.Upper);
        var bbL = D(bb.Lower);
        var kc = cache.Keltner(20, 1.5m);
        var kcU = D(kc.Upper);
        var kcL = D(kc.Lower);
        var dc10 = cache.Donchian(10);
        var dc20 = cache.Donchian(20);
        var dc10h = D(dc10.High);
        var dc10l = D(dc10.Low);
        var dc20h = D(dc20.High);
        var dc20l = D(dc20.Low);
        var stoch = Stoch(high, low, close, 14);
        var roc = Roc(close, 10);
        var sma20 = Sma(close, 20);

        sbyte[] Sig(Func<int, int> f)
        {
            var a = new sbyte[n];
            for (var i = Warmup; i < n; i++)
            {
                a[i] = (sbyte)f(i);
            }

            return a;
        }

        int Cross(double[] fast, double[] slow, int i)
        {
            if (double.IsNaN(fast[i]) || double.IsNaN(slow[i]) || double.IsNaN(fast[i - 1]) || double.IsNaN(slow[i - 1]))
            {
                return 0;
            }

            return fast[i] > slow[i] && fast[i - 1] <= slow[i - 1] ? 1
                : fast[i] < slow[i] && fast[i - 1] >= slow[i - 1] ? -1 : 0;
        }

        return
        [
            new("ema_cross_8_21", 2, "Long when EMA8 crosses above EMA21; short on the opposite cross.",
                Sig(i => Cross(ema8, ema21, i))),
            new("ema_cross_12_26", 2, "Long when EMA12 crosses above EMA26; short on the opposite cross.",
                Sig(i => Cross(ema12, ema26, i))),
            new("ema_cross_5_13", 2, "Long when EMA5 crosses above EMA13; short on the opposite cross.",
                Sig(i => Cross(ema5, ema13, i))),
            new("rsi14_mr_30_70", 1, "Long when RSI14 closes below 30; short when RSI14 closes above 70.",
                Sig(i => rsi14[i] < 30 && rsi14[i - 1] >= 30 ? 1 : rsi14[i] > 70 && rsi14[i - 1] <= 70 ? -1 : 0)),
            new("rsi7_mr_25_75", 1, "Long when RSI7 closes below 25; short when RSI7 closes above 75.",
                Sig(i => rsi7[i] < 25 && rsi7[i - 1] >= 25 ? 1 : rsi7[i] > 75 && rsi7[i - 1] <= 75 ? -1 : 0)),
            new("rsi14_mom_50", 1, "Long when RSI14 crosses above 50; short when it crosses below 50.",
                Sig(i => rsi14[i] > 50 && rsi14[i - 1] <= 50 ? 1 : rsi14[i] < 50 && rsi14[i - 1] >= 50 ? -1 : 0)),
            new("bb20_2_fade", 2, "Long when close crosses back above the lower Bollinger (20,2); short when close crosses back below the upper band.",
                Sig(i => close[i] > bbL[i] && close[i - 1] <= bbL[i - 1] ? 1 : close[i] < bbU[i] && close[i - 1] >= bbU[i - 1] ? -1 : 0)),
            new("bb20_2_break", 2, "Long when close crosses above the upper Bollinger (20,2); short when close crosses below the lower band.",
                Sig(i => close[i] > bbU[i] && close[i - 1] <= bbU[i - 1] ? 1 : close[i] < bbL[i] && close[i - 1] >= bbL[i - 1] ? -1 : 0)),
            new("macd_cross", 2, "Long when MACD line crosses above its signal; short on the opposite cross.",
                Sig(i => Cross(macdD, macdS, i))),
            new("donchian_20_break", 1, "Long when close first breaks above the prior 20-bar Donchian high; short on first break of the prior 20-bar low.",
                Sig(i => close[i] > dc20h[i] && close[i - 1] <= dc20h[i - 1] ? 1 : close[i] < dc20l[i] && close[i - 1] >= dc20l[i - 1] ? -1 : 0)),
            new("donchian_10_break", 1, "Long when close first breaks above the prior 10-bar Donchian high; short on first break of the prior 10-bar low.",
                Sig(i => close[i] > dc10h[i] && close[i - 1] <= dc10h[i - 1] ? 1 : close[i] < dc10l[i] && close[i - 1] >= dc10l[i - 1] ? -1 : 0)),
            new("ema21_rsi_pullback", 3, "Long when EMA8>EMA21 and RSI14 crosses up through 40; short when EMA8<EMA21 and RSI14 crosses down through 60.",
                Sig(i => ema8[i] > ema21[i] && rsi14[i] > 40 && rsi14[i - 1] <= 40 ? 1
                    : ema8[i] < ema21[i] && rsi14[i] < 60 && rsi14[i - 1] >= 60 ? -1 : 0)),
            new("ema50_adx_trend", 3, "Long when close crosses above EMA50 while ADX14>20; short when close crosses below EMA50 while ADX14>20.",
                Sig(i => adx[i] > 20 && close[i] > ema50[i] && close[i - 1] <= ema50[i - 1] ? 1
                    : adx[i] > 20 && close[i] < ema50[i] && close[i - 1] >= ema50[i - 1] ? -1 : 0)),
            new("vol_spike_ema_trend", 3, "Long when relative volume>1.5 and close>EMA21; short when relative volume>1.5 and close<EMA21 (first bar of the spike).",
                Sig(i => relVol[i] > 1.5 && relVol[i - 1] <= 1.5 && close[i] > ema21[i] ? 1
                    : relVol[i] > 1.5 && relVol[i - 1] <= 1.5 && close[i] < ema21[i] ? -1 : 0)),
            new("roc10_zero", 1, "Long when 10-bar ROC crosses above 0; short when it crosses below 0.",
                Sig(i => roc[i] > 0 && roc[i - 1] <= 0 ? 1 : roc[i] < 0 && roc[i - 1] >= 0 ? -1 : 0)),
            new("stoch14_mr", 2, "Long when 14-bar stochastic crosses up through 20; short when it crosses down through 80.",
                Sig(i => stoch[i] > 20 && stoch[i - 1] <= 20 ? 1 : stoch[i] < 80 && stoch[i - 1] >= 80 ? -1 : 0)),
            new("zscore20_fade", 1, "Long when 20-bar close z-score crosses up through -1.5; short when it crosses down through +1.5.",
                Sig(i => z20[i] > -1.5 && z20[i - 1] <= -1.5 ? 1 : z20[i] < 1.5 && z20[i - 1] >= 1.5 ? -1 : 0)),
            new("keltner_fade", 2, "Long when close re-enters Keltner (20,1.5) from below; short when it re-enters from above.",
                Sig(i => close[i] > kcL[i] && close[i - 1] <= kcL[i - 1] ? 1 : close[i] < kcU[i] && close[i - 1] >= kcU[i - 1] ? -1 : 0)),
            new("macd_ema20", 3, "Long when MACD crosses above signal and close>EMA20; short when MACD crosses below signal and close<EMA20.",
                Sig(i => macdD[i] > macdS[i] && macdD[i - 1] <= macdS[i - 1] && close[i] > ema20[i] ? 1
                    : macdD[i] < macdS[i] && macdD[i - 1] >= macdS[i - 1] && close[i] < ema20[i] ? -1 : 0)),
            new("bb_rsi_fade", 3, "Long when close is below lower Bollinger and RSI14<35; short when close is above upper Bollinger and RSI14>65 (once per excursion).",
                Sig(i => close[i] < bbL[i] && rsi14[i] < 35 && !(close[i - 1] < bbL[i - 1] && rsi14[i - 1] < 35) ? 1
                    : close[i] > bbU[i] && rsi14[i] > 65 && !(close[i - 1] > bbU[i - 1] && rsi14[i - 1] > 65) ? -1 : 0)),
            new("sma20_cross", 1, "Long when close crosses above SMA20; short when close crosses below SMA20.",
                Sig(i => close[i] > sma20[i] && close[i - 1] <= sma20[i - 1] ? 1 : close[i] < sma20[i] && close[i - 1] >= sma20[i - 1] ? -1 : 0))
        ];
    }

    private static Sim Simulate(
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        DateTimeOffset[] times,
        sbyte[] signal,
        int mask,
        double slPct,
        double tpPct,
        int maxHold,
        DateTimeOffset mid,
        bool log)
    {
        var n = open.Length;
        var equity = StartEquity;
        var peak = equity;
        var maxDd = 0.0;
        var day = times[Warmup].UtcDateTime.Date;
        var dayStart = equity;
        var dayPnl = 0.0;
        var halted = false;
        var pos = 0;
        var fill = -1;
        var entry = 0.0;
        var midEntry = 0.0;
        var stop = 0.0;
        var take = 0.0;
        var qty = 0.0;
        var wins = 0.0;
        var losses = 0.0;
        var winN = 0;
        var longN = 0;
        var shortN = 0;
        var longP = 0.0;
        var shortP = 0.0;
        var fees = 0.0;
        var slips = 0.0;
        var y1 = 0.0;
        var y2 = 0.0;
        var monthNet = new Dictionary<string, double>();
        var trades = log ? new List<Btc15mTrade>() : null;
        var eq = log ? new List<(DateTimeOffset, double)>() : null;
        var nTrades = 0;

        void CloseTrade(int i, double rawExit, string reason)
        {
            var side = pos;
            var midExit = rawExit;
            var exitPx = rawExit * (1.0 - side * Slip);
            var gross = side * (midExit - midEntry) * qty;
            var fee = qty * entry * Fee + qty * exitPx * Fee;
            var slip = Math.Abs(entry - midEntry) * qty + Math.Abs(exitPx - midExit) * qty;
            var net = side * (exitPx - entry) * qty - fee;
            equity += net;
            dayPnl += net;
            fees += fee;
            slips += slip;
            nTrades++;
            if (net > 0)
            {
                wins += net;
                winN++;
            }
            else if (net < 0)
            {
                losses += net;
            }

            if (side > 0)
            {
                longN++;
                longP += net;
            }
            else
            {
                shortN++;
                shortP += net;
            }

            if (times[fill] < mid)
            {
                y1 += net;
            }
            else
            {
                y2 += net;
            }

            var mk = times[i].ToString("yyyy-MM");
            monthNet[mk] = monthNet.GetValueOrDefault(mk) + net;
            if (equity > peak)
            {
                peak = equity;
            }

            maxDd = Math.Max(maxDd, peak - equity);
            if (dayPnl <= -DailyHalt * dayStart)
            {
                halted = true;
            }

            trades?.Add(new Btc15mTrade(times[fill], times[i], side, entry, exitPx, qty, gross, fee, slip, net, reason, i - fill + 1));
            pos = 0;
        }

        for (var i = Warmup + 1; i < n; i++)
        {
            var d = times[i].UtcDateTime.Date;
            if (d != day)
            {
                day = d;
                dayStart = equity;
                dayPnl = 0;
                halted = false;
                eq?.Add((times[i], equity));
            }

            if (pos != 0)
            {
                var hitSl = pos > 0 ? low[i] <= stop : high[i] >= stop;
                var hitTp = pos > 0 ? high[i] >= take : low[i] <= take;
                if (hitSl)
                {
                    CloseTrade(i, stop, "SL");
                }
                else if (hitTp)
                {
                    CloseTrade(i, take, "TP");
                }
                else if (i - fill + 1 >= maxHold || i == n - 1)
                {
                    CloseTrade(i, close[i], i == n - 1 ? "EOD" : "TIME");
                }

                continue;
            }

            if (halted || i == n - 1)
            {
                continue;
            }

            var sig = signal[i - 1];
            if (sig == 0 || (sig > 0 && (mask & 1) == 0) || (sig < 0 && (mask & 2) == 0))
            {
                continue;
            }

            if (open[i] <= 0)
            {
                continue;
            }

            pos = sig > 0 ? 1 : -1;
            fill = i;
            midEntry = open[i];
            entry = midEntry * (1.0 + pos * Slip);
            stop = entry * (1.0 - pos * slPct);
            take = entry * (1.0 + pos * tpPct);
            var risk = equity * RiskPct;
            var stopDist = Math.Abs(entry - stop);
            if (stopDist <= 0 || equity <= 0)
            {
                pos = 0;
                continue;
            }

            qty = risk / stopDist;
            var notional = qty * entry;
            var maxNotional = equity * LevCap;
            if (notional > maxNotional)
            {
                qty = maxNotional / entry;
            }

            var fillHitSl = pos > 0 ? low[i] <= stop : high[i] >= stop;
            var fillHitTp = pos > 0 ? high[i] >= take : low[i] <= take;
            if (fillHitSl)
            {
                CloseTrade(i, stop, "SL");
            }
            else if (fillHitTp)
            {
                CloseTrade(i, take, "TP");
            }
            else if (maxHold <= 1)
            {
                CloseTrade(i, close[i], "TIME");
            }
        }

        var pf = Math.Abs(losses) < 1e-12 ? (wins > 0 ? double.PositiveInfinity : double.NaN) : wins / Math.Abs(losses);
        var monthW = monthNet.Count(kv => kv.Value > 0);
        var monthL = monthNet.Count(kv => kv.Value < 0);
        return new Sim(
            nTrades,
            equity - StartEquity,
            equity,
            pf,
            peak > 0 ? maxDd / StartEquity : 0,
            nTrades == 0 ? double.NaN : winN / (double)nTrades,
            nTrades == 0 ? 0 : (equity - StartEquity) / nTrades,
            fees,
            slips,
            longN,
            shortN,
            longP,
            shortP,
            y1,
            y2,
            monthW,
            monthL,
            trades ?? [],
            eq ?? []);
    }

    private readonly record struct Sim(
        int TradeCount,
        double Net,
        double FinalEquity,
        double Pf,
        double MaxDdPct,
        double WinRate,
        double Expectancy,
        double Fees,
        double Slippage,
        int LongTrades,
        int ShortTrades,
        double LongPnl,
        double ShortPnl,
        double Year1Net,
        double Year2Net,
        int MonthWins,
        int MonthLosses,
        List<Btc15mTrade> Trades,
        List<(DateTimeOffset, double)> Equity);

    private static Btc15mCandidate ToCandidate(Recipe recipe, string side, double sl, double tp, int hold, Sim s)
    {
        var ddPct = s.MaxDdPct * 100;
        var monthRate = s.MonthWins + s.MonthLosses == 0 ? 0 : s.MonthWins / (double)(s.MonthWins + s.MonthLosses);
        var yGap = Math.Max(Math.Abs(s.Year1Net), Math.Abs(s.Year2Net)) < 1e-9
            ? 0
            : Math.Abs(s.Year1Net - s.Year2Net) / Math.Max(1, Math.Max(Math.Abs(s.Year1Net), Math.Abs(s.Year2Net)));
        var pf = double.IsNaN(s.Pf) || double.IsInfinity(s.Pf) ? 0 : s.Pf;
        var score = s.Net
            + 80 * (pf - 1)
            - 8 * ddPct
            + 0.02 * Math.Min(s.TradeCount, 5000)
            + 40 * monthRate
            - 12 * recipe.Complexity
            - 30 * yGap
            + (s.TradeCount >= MinTrades && s.Net >= MinNet ? 500 : 0);
        return new Btc15mCandidate(
            recipe.Id, recipe.Rules, side, sl, tp, hold, recipe.Complexity, s.TradeCount, s.Net, s.FinalEquity, s.Pf, ddPct,
            s.WinRate, s.Expectancy, s.Fees, s.Slippage, s.LongTrades, s.ShortTrades, s.LongPnl, s.ShortPnl,
            s.Year1Net, s.Year2Net, s.MonthWins, s.MonthLosses, score);
    }

    private static List<Btc15mCandidate> Closest(List<Btc15mCandidate> all)
    {
        var byNet = all.Where(c => c.Trades >= MinTrades).OrderByDescending(c => c.Net).Take(5);
        var byN = all.Where(c => c.Net >= MinNet).OrderByDescending(c => c.Trades).Take(5);
        var rest = all.OrderByDescending(c => c.Score).Take(10);
        return byNet.Concat(byN).Concat(rest).DistinctBy(c => (c.Recipe, c.Side, c.SlPct, c.TpPct, c.MaxHoldBars)).Take(10).ToList();
    }

    private static Dictionary<string, double> Baselines(
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        DateTimeOffset[] times,
        sbyte[] signal,
        int mask,
        Btc15mCandidate selected,
        DateTimeOffset mid,
        int nTrades)
    {
        var bhQty = StartEquity / open[Warmup + 1];
        var bhExit = close[^1];
        var bhFee = StartEquity * Fee + bhQty * bhExit * Fee;
        var bhNet = (bhExit - open[Warmup + 1]) * bhQty - bhFee;
        var rng = new Random(42);
        var randTimes = SimulateRandom(open, high, low, close, times, nTrades, selected.SlPct, selected.TpPct, selected.MaxHoldBars, mid, rng, dirFromSignal: false, signal, mask);
        rng = new Random(43);
        var randDir = SimulateRandom(open, high, low, close, times, nTrades, selected.SlPct, selected.TpPct, selected.MaxHoldBars, mid, rng, dirFromSignal: true, signal, mask);
        return new Dictionary<string, double>
        {
            ["buy_hold_1x_net"] = bhNet,
            ["buy_hold_1x_equity"] = StartEquity + bhNet,
            ["random_entry_net"] = randTimes.Net,
            ["random_entry_pf"] = randTimes.Pf,
            ["random_entry_trades"] = randTimes.TradeCount,
            ["random_direction_net"] = randDir.Net,
            ["random_direction_pf"] = randDir.Pf
        };
    }

    private static Sim SimulateRandom(
        double[] open,
        double[] high,
        double[] low,
        double[] close,
        DateTimeOffset[] times,
        int target,
        double sl,
        double tp,
        int hold,
        DateTimeOffset mid,
        Random rng,
        bool dirFromSignal,
        sbyte[] signal,
        int mask)
    {
        var fake = new sbyte[open.Length];
        var pool = new List<int>();
        for (var i = Warmup; i < open.Length - 2; i++)
        {
            pool.Add(i);
        }

        for (var i = pool.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        var placed = 0;
        foreach (var i in pool)
        {
            if (placed >= target * 3)
            {
                break;
            }

            int dir;
            if (dirFromSignal)
            {
                var s = signal[i];
                if (s == 0)
                {
                    continue;
                }

                dir = rng.Next(2) == 0 ? 1 : -1;
            }
            else
            {
                dir = rng.Next(2) == 0 ? 1 : -1;
            }

            fake[i] = (sbyte)dir;
            placed++;
        }

        var useMask = dirFromSignal ? mask : 3;
        return Simulate(open, high, low, close, times, fake, useMask, sl, tp, hold, mid, log: false);
    }

    private static double[] D(IReadOnlyList<decimal?> src)
    {
        var a = new double[src.Count];
        for (var i = 0; i < src.Count; i++)
        {
            a[i] = src[i] is { } v ? (double)v : double.NaN;
        }

        return a;
    }

    private static double[] Sma(double[] close, int period)
    {
        var a = new double[close.Length];
        Array.Fill(a, double.NaN);
        double s = 0;
        for (var i = 0; i < close.Length; i++)
        {
            s += close[i];
            if (i >= period)
            {
                s -= close[i - period];
            }

            if (i >= period - 1)
            {
                a[i] = s / period;
            }
        }

        return a;
    }

    private static double[] Roc(double[] close, int period)
    {
        var a = new double[close.Length];
        Array.Fill(a, double.NaN);
        for (var i = period; i < close.Length; i++)
        {
            if (close[i - period] != 0)
            {
                a[i] = close[i] / close[i - period] - 1.0;
            }
        }

        return a;
    }

    private static double[] Stoch(double[] high, double[] low, double[] close, int period)
    {
        var a = new double[close.Length];
        Array.Fill(a, double.NaN);
        for (var i = period; i < close.Length; i++)
        {
            var hh = double.NegativeInfinity;
            var ll = double.PositiveInfinity;
            for (var j = i - period + 1; j <= i; j++)
            {
                hh = Math.Max(hh, high[j]);
                ll = Math.Min(ll, low[j]);
            }

            var den = hh - ll;
            a[i] = Math.Abs(den) < 1e-12 ? 50 : 100.0 * (close[i] - ll) / den;
        }

        return a;
    }
}
