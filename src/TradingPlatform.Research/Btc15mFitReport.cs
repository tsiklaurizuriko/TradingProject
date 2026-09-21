using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public static class Btc15mFitReport
{
    public static string Render(Btc15mFitResult r, IReadOnlyList<MarketCandle>? candles = null)
    {
        var prevCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
        var sb = new StringBuilder();
        var s = r.Selected;
        var days = Math.Max(1, (r.DataEnd - r.DataStart).TotalDays);
        sb.AppendLine("# BTCUSDT 15m historically fitted strategy");
        sb.AppendLine();
        sb.AppendLine("RESEARCH ONLY. Intentionally fitted on the historical BTCUSDT 15m sample. **Not validated alpha. Not paper-validated. Not live-ready.** Isolated LOW Risk Engine, LIVE, and the 43-strategy registry were not changed.");
        sb.AppendLine();

        sb.AppendLine("## 1. Executive Summary");
        sb.AppendLine();
        sb.AppendLine($"**{r.Classification}**");
        sb.AppendLine();
        if (s is null)
        {
            sb.AppendLine("No legitimate combination reached ≥2,000 completed trades **and** ≥$100 net profit after fees and slippage on Isolated LOW $1,000 / 0.5% / 3x.");
            sb.AppendLine();
            sb.AppendLine("The two constraints conflict on this sample: combinations that completed ≥2,000 trades all finished net-negative after costs; combinations that finished ≥$100 net did so with far fewer than 2,000 trades. The gap was not closed by searching LONG, SHORT, and BOTH, nor by the SL/TP/hold grid.");
        }
        else
        {
            sb.AppendLine($"One BTCUSDT 15m strategy was selected on the **full** historical window, then frozen. It is a fit, not a proof.");
            sb.AppendLine();
            sb.AppendLine($"- Recipe: `{s.Recipe}` {s.Side}");
            sb.AppendLine($"- SL {PctN(s.SlPct)} / TP {PctN(s.TpPct)} / time-exit {s.MaxHoldBars} bars ({s.MaxHoldBars * 15} minutes)");
            sb.AppendLine($"- Trades {s.Trades:N0} ({s.Trades / days:0.00}/day)");
            sb.AppendLine($"- Final equity **${s.FinalEquity:0.00}** (net **${s.Net:0.00}**, {s.Net / 10:0.00}%)");
            sb.AppendLine($"- PF {Num(s.Pf)}, max DD {s.MaxDdPct:0.00}% of start, WR {Pct(s.WinRate)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 2. Exact Historical Dataset");
        sb.AppendLine();
        sb.AppendLine("| Field | Value |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| Symbol | BTCUSDT USDⓈ-M perpetual |");
        sb.AppendLine("| Timeframe | 15m only |");
        sb.AppendLine($"| First open | {r.DataStart:yyyy-MM-dd HH:mm} UTC |");
        sb.AppendLine($"| Last close | {r.DataEnd:yyyy-MM-dd HH:mm} UTC |");
        sb.AppendLine($"| Closed bars | {r.Bars:N0} |");
        sb.AppendLine($"| Span | {days:0.00} days |");
        sb.AppendLine("| Source | `artifacts/strategy-validation-cache/BTCUSDT_15m.json` (Binance klines, closed bars) |");
        sb.AppendLine();

        sb.AppendLine("## 3. Search Space");
        sb.AppendLine();
        sb.AppendLine("Pre-registered conventional features only (EMA, SMA, RSI, MACD, Bollinger, Keltner, Donchian, ADX, ATR/relative volume, ROC, stochastic, z-score). Max 5 decision components. No neural nets.");
        sb.AppendLine();
        sb.AppendLine($"- Entry recipes: **{r.RecipesTested}**");
        sb.AppendLine("- Sides: LONG / SHORT / BOTH");
        sb.AppendLine($"- SL grid: {string.Join(", ", Btc15mFit.SlGrid.Select(v => PctN(v)))}");
        sb.AppendLine($"- TP grid: {string.Join(", ", Btc15mFit.TpGrid.Select(v => PctN(v)))}");
        sb.AppendLine($"- Time exits (15m bars): {string.Join(", ", Btc15mFit.HoldGrid)}");
        sb.AppendLine("- R:R pairs are included as SL/TP combinations (e.g. 1% / 2% = 1:2).");
        sb.AppendLine();

        sb.AppendLine("## 4. Number of Candidates Tested");
        sb.AppendLine();
        sb.AppendLine($"**{r.CombinationsTested:N0}** full Isolated-book backtests (recipe × side × SL × TP × hold).");
        sb.AppendLine($"Feasible (≥2,000 trades and ≥$100 net): **{r.AllFeasible.Count}**.");
        foreach (var n in r.Notes)
        {
            if (n.StartsWith("Combinations with", StringComparison.Ordinal) || n.StartsWith("Intersection", StringComparison.Ordinal))
            {
                sb.AppendLine(n);
            }
        }
        sb.AppendLine();

        sb.AppendLine("## 5. Optimization Method");
        sb.AppendLine();
        sb.AppendLine("Cartesian grid on the **entire** 2-year sample (intentional fit). Score among feasible names: net $ + 80×(PF−1) − 8×DD% + 0.02×trades + 40×monthly win rate − 12×complexity − 30×|Y1−Y2| gap. Tie-break prefers simpler recipes. Parameters were **not** retuned on Year 1 / Year 2 / quarters.");
        sb.AppendLine();
        sb.AppendLine("Execution: closed-bar signal, next 15m open fill, SL before TP, taker 0.04%/side, 0.02% slip in prices, Isolated one position, 0.5% of current equity, 3x cap, UTC daily 3% halt. Funding not in this kline replay.");
        sb.AppendLine();

        sb.AppendLine("## 6. Final Strategy Rules");
        sb.AppendLine();
        if (s is null)
        {
            sb.AppendLine("None selected.");
        }
        else
        {
            sb.AppendLine(s.Rules);
            sb.AppendLine();
            sb.AppendLine($"Only **{s.Side}** signals are taken." + (s.Side == "BOTH" ? "" : " The other side is ignored."));
            sb.AppendLine();
            sb.AppendLine("After a fill, exit at the first of: stop, target, max hold, or last bar. Same-bar stop and target → stop.");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine("on each closed 15m bar i:");
            sb.AppendLine("  if flat and not daily-halted:");
            sb.AppendLine($"    sig = {s.Recipe}(bars[0..i])   // causal");
            sb.AppendLine($"    if sig matches {s.Side}:");
            sb.AppendLine("      next bar open: enter with Isolated LOW size");
            sb.AppendLine($"      SL = entry × (1 ∓ {PctN(s.SlPct)})");
            sb.AppendLine($"      TP = entry × (1 ± {PctN(s.TpPct)})");
            sb.AppendLine($"      flatten after {s.MaxHoldBars} bars if still open");
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("## 7. Exact Parameters");
        sb.AppendLine();
        if (s is null)
        {
            sb.AppendLine("n/a");
        }
        else
        {
            sb.AppendLine("| Parameter | Value |");
            sb.AppendLine("|---|---|");
            sb.AppendLine($"| Recipe | `{s.Recipe}` |");
            sb.AppendLine("| Symbol | BTCUSDT |");
            sb.AppendLine("| Timeframe | 15m |");
            sb.AppendLine($"| Side | {s.Side} |");
            sb.AppendLine($"| SL | {PctN(s.SlPct)} of slipped entry |");
            sb.AppendLine($"| TP | {PctN(s.TpPct)} of slipped entry |");
            sb.AppendLine($"| Time exit | {s.MaxHoldBars} bars ({s.MaxHoldBars * 15} min) |");
            sb.AppendLine("| Risk | 0.5% of current equity |");
            sb.AppendLine("| Leverage cap | 3x Isolated |");
            sb.AppendLine("| Daily halt | 3% of day-start equity |");
            sb.AppendLine("| Positions | 1 |");
            sb.AppendLine("| Fee | 0.04% taker each side |");
            sb.AppendLine("| Slippage | 0.02% in fill price each side |");
            sb.AppendLine($"| Complexity | {s.Complexity} features |");
        }

        sb.AppendLine();
        sb.AppendLine("## 8. Final 2-Year Backtest");
        sb.AppendLine();
        if (s is not null)
        {
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine("| Start equity | $1,000.00 |");
            sb.AppendLine($"| Final equity | ${s.FinalEquity:0.00} |");
            sb.AppendLine($"| Net profit | ${s.Net:0.00} |");
            sb.AppendLine($"| Net return | {s.Net / 10:0.00}% |");
            sb.AppendLine($"| Profit factor | {Num(s.Pf)} |");
            sb.AppendLine($"| Maximum drawdown | ${s.MaxDdPct / 100 * 1000:0.00} ({s.MaxDdPct:0.00}% of start) |");
            sb.AppendLine($"| Expectancy / trade | ${s.Expectancy:0.0000} |");
            sb.AppendLine($"| Trades | {s.Trades} |");
            sb.AppendLine($"| Win rate | {Pct(s.WinRate)} |");
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 9. Equity Curve Statistics");
        sb.AppendLine();
        if (r.Equity.Count > 2)
        {
            var rets = new List<double>();
            for (var i = 1; i < r.Equity.Count; i++)
            {
                var prev = r.Equity[i - 1].Equity;
                if (prev > 0)
                {
                    rets.Add(r.Equity[i].Equity / prev - 1.0);
                }
            }

            var mean = rets.Average();
            var sd = Math.Sqrt(rets.Sum(x => (x - mean) * (x - mean)) / Math.Max(1, rets.Count - 1));
            var sharpe = sd < 1e-12 ? double.NaN : mean / sd * Math.Sqrt(365);
            sb.AppendLine($"Daily equity points: {r.Equity.Count}. Approx Sharpe (√365, no RF): {Num(sharpe)}.");
        }
        else
        {
            sb.AppendLine("No logged equity curve (no selected strategy).");
        }

        sb.AppendLine();
        sb.AppendLine("## 10. Drawdown");
        sb.AppendLine();
        if (s is not null)
        {
            sb.AppendLine($"Max drawdown from $1,000 peak-to-trough dollars: **${s.MaxDdPct / 100 * 1000:0.00}** ({s.MaxDdPct:0.00}% of start). Peak tracking uses running equity high.");
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 11. Trade Statistics");
        sb.AppendLine();
        if (r.Trades.Count > 0)
        {
            var t = r.Trades;
            var wins = t.Where(x => x.Net > 0).ToList();
            var losses = t.Where(x => x.Net < 0).ToList();
            var (cw, cl) = Streaks(t);
            sb.AppendLine("| Metric | Value |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine($"| Trades | {t.Count} |");
            sb.AppendLine($"| Trades/day | {t.Count / days:0.00} |");
            sb.AppendLine($"| Trades/week | {t.Count / days * 7:0.00} |");
            sb.AppendLine($"| Trades/month | {t.Count / days * 30.44:0.00} |");
            sb.AppendLine($"| Average winner | ${(wins.Count == 0 ? 0 : wins.Average(x => x.Net)):0.0000} |");
            sb.AppendLine($"| Average loser | ${(losses.Count == 0 ? 0 : losses.Average(x => x.Net)):0.0000} |");
            sb.AppendLine($"| Largest winner | ${t.Max(x => x.Net):0.0000} |");
            sb.AppendLine($"| Largest loser | ${t.Min(x => x.Net):0.0000} |");
            sb.AppendLine($"| Max consecutive wins | {cw} |");
            sb.AppendLine($"| Max consecutive losses | {cl} |");
            sb.AppendLine($"| Average hold (bars) | {t.Average(x => x.HoldBars):0.00} |");
            sb.AppendLine($"| Average hold (minutes) | {t.Average(x => x.HoldBars) * 15:0.0} |");
            if (s is not null && s.SlPct > 0)
            {
                var rMults = t.Select(x => x.Net / Math.Max(1e-12, x.Qty * x.Entry * s.SlPct)).ToList();
                sb.AppendLine($"| Expectancy (R, SL distance) | {rMults.Average():0.0000} |");
                sb.AppendLine($"| Total R | {rMults.Sum():0.00} |");
            }
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 12. Cost Breakdown");
        sb.AppendLine();
        if (s is not null)
        {
            var gross = r.Trades.Sum(x => x.Gross);
            sb.AppendLine("| Item | $ |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine($"| Gross P&L (mid) | {gross:0.00} |");
            sb.AppendLine($"| Fees | {s.Fees:0.00} |");
            sb.AppendLine($"| Slippage (price impact) | {s.Slippage:0.00} |");
            sb.AppendLine("| Funding | DATA_UNAVAILABLE in this kline replay |");
            sb.AppendLine($"| Net P&L | {s.Net:0.00} |");
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 13. Monthly Results");
        sb.AppendLine();
        MonthTable(sb, r.Trades, "yyyy-MM");

        sb.AppendLine("## 14. Quarterly Results");
        sb.AppendLine();
        QuarterTable(sb, r.Trades);

        sb.AppendLine("## 15. Year 1 vs Year 2");
        sb.AppendLine();
        if (s is not null)
        {
            var split = r.DataStart + (r.DataEnd - r.DataStart) / 2;
            sb.AppendLine($"Split at {split:yyyy-MM-dd} (midpoint of the fitted window). Same frozen parameters.");
            sb.AppendLine();
            sb.AppendLine("| Period | Net $ |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine($"| Year 1 | {s.Year1Net:0.00} |");
            sb.AppendLine($"| Year 2 | {s.Year2Net:0.00} |");
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 16. Long vs Short");
        sb.AppendLine();
        if (s is not null)
        {
            var lp = PfSide(r.Trades, 1);
            var sp = PfSide(r.Trades, -1);
            sb.AppendLine("| Side | Trades | Net $ | PF |");
            sb.AppendLine("|---|---:|---:|---:|");
            sb.AppendLine($"| LONG | {s.LongTrades} | {s.LongPnl:0.00} | {Num(lp)} |");
            sb.AppendLine($"| SHORT | {s.ShortTrades} | {s.ShortPnl:0.00} | {Num(sp)} |");
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 17. Regime Breakdown");
        sb.AppendLine();
        sb.AppendLine("Causal at the signal bar (fill−1): BTC ATR% 14 tercile over the last 50 closed bars; trend = EMA20 vs EMA50. Descriptive only.");
        sb.AppendLine();
        if (candles is not null && r.Trades.Count > 0)
        {
            sb.Append(RegimeTable(r.Trades, candles));
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
            sb.AppendLine();
        }

        sb.AppendLine("## 18. Random Baselines");
        sb.AppendLine();
        if (s is not null && r.Baselines.Count > 0)
        {
            sb.AppendLine("| Baseline | Value |");
            sb.AppendLine("|---|---:|");
            sb.AppendLine($"| Selected net | ${s.Net:0.00} |");
            sb.AppendLine($"| Buy & hold 1x (same fees, $1,000 notional) net | ${r.Baselines["buy_hold_1x_net"]:0.00} |");
            sb.AppendLine($"| Random entry timestamps, same SL/TP/hold, ~same trade count, net | ${r.Baselines["random_entry_net"]:0.00} (PF {Num(r.Baselines["random_entry_pf"])}, n={r.Baselines["random_entry_trades"]:0}) |");
            sb.AppendLine($"| Signal times, random LONG/SHORT, net | ${r.Baselines["random_direction_net"]:0.00} (PF {Num(r.Baselines["random_direction_pf"])}) |");
        }
        else
        {
            sb.AppendLine("n/a — no strategy selected.");
        }

        sb.AppendLine();
        sb.AppendLine("## 19. Overfitting Risk");
        sb.AppendLine();
        sb.AppendLine($"This search tested **{r.CombinationsTested:N0}** combinations on the **same** sample used to pick the winner. That is an in-sample fit. Multiple-testing is severe. Year/quarter tables are descriptive, not a confirmation set. Forward behavior is the only honest test, and it has not been measured yet.");
        sb.AppendLine();
        sb.AppendLine("Best 20 by score (feasible first, else overall):");
        sb.AppendLine();
        sb.AppendLine("| Recipe | Side | SL | TP | Hold | n | Net $ | PF | DD% | Score |");
        sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var c in r.Top20)
        {
            sb.AppendLine($"| {c.Recipe} | {c.Side} | {PctN(c.SlPct)} | {PctN(c.TpPct)} | {c.MaxHoldBars} | {c.Trades} | {c.Net:0.00} | {Num(c.Pf)} | {c.MaxDdPct:0.00} | {c.Score:0.0} |");
        }

        if (s is null)
        {
            sb.AppendLine();
            sb.AppendLine("Closest 10 (constraint failures):");
            sb.AppendLine();
            sb.AppendLine("| Recipe | Side | SL | TP | Hold | n | Net $ | Why short |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|---|");
            foreach (var c in r.Closest10)
            {
                var why = c.Trades < Btc15mFit.MinTrades && c.Net < Btc15mFit.MinNet
                    ? $"trades {c.Trades} < 2000 and net ${c.Net:0.00} < $100"
                    : c.Trades < Btc15mFit.MinTrades
                        ? $"trades {c.Trades} < 2000"
                        : c.Net < Btc15mFit.MinNet
                            ? $"net ${c.Net:0.00} < $100"
                            : "other";
                sb.AppendLine($"| {c.Recipe} | {c.Side} | {PctN(c.SlPct)} | {PctN(c.TpPct)} | {c.MaxHoldBars} | {c.Trades} | {c.Net:0.00} | {why} |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## 20. Frozen Strategy Specification");
        sb.AppendLine();
        if (s is null)
        {
            sb.AppendLine("No trading rules were frozen. `docs/btc-2y-15m-fitted-strategy-frozen.json` records the **NO STRATEGY FOUND UNDER CONSTRAINTS** classification, the dataset bounds, Isolated LOW sizing, Model B execution, and costs. There is nothing to forward-test as a fitted entry/exit spec.");
        }
        else
        {
            sb.AppendLine("After this document and `docs/btc-2y-15m-fitted-strategy-frozen.json` are written, **do not change** indicators, parameters, SL, TP, timeframe, entry, exit, or sizing. Classification: **HISTORICALLY FITTED STRATEGY.**");
        }
        sb.AppendLine();
        sb.AppendLine("## Forward-test questions");
        sb.AppendLine();
        sb.AppendLine($"1. Did you find ONE BTCUSDT 15m strategy satisfying ≥2,000 trades and ≥$100 net after costs? **{(s is null ? "NO" : "YES")}**");
        sb.AppendLine($"2. Exact rules: {(s is null ? "n/a" : s.Rules)}");
        sb.AppendLine($"3. Exact parameters: {(s is null ? "n/a" : $"{s.Recipe} {s.Side} SL {PctN(s.SlPct)} TP {PctN(s.TpPct)} hold {s.MaxHoldBars}")}");
        sb.AppendLine($"4. Final equity from $1,000: {(s is null ? "n/a" : $"${s.FinalEquity:0.00}")}");
        sb.AppendLine($"5. Maximum drawdown: {(s is null ? "n/a" : $"{s.MaxDdPct:0.00}% of start")}");
        sb.AppendLine($"6. Profit Factor: {(s is null ? "n/a" : Num(s.Pf))}");
        sb.AppendLine($"7. Trades: {(s is null ? "n/a" : s.Trades.ToString())}");
        sb.AppendLine($"8. Fees / slippage: {(s is null ? "n/a" : $"${s.Fees:0.00} / ${s.Slippage:0.00}")}");
        sb.AppendLine($"9. Year 1 vs Year 2: {(s is null ? "n/a" : $"${s.Year1Net:0.00} vs ${s.Year2Net:0.00}")}");
        sb.AppendLine($"10. vs random entry: {(s is null || r.Baselines.Count == 0 ? "n/a" : $"selected ${s.Net:0.00} vs random ${r.Baselines["random_entry_net"]:0.00}")}");
        sb.AppendLine($"11. Candidates tested: {r.CombinationsTested:N0}");
        sb.AppendLine("12. Frozen config: `docs/btc-2y-15m-fitted-strategy-frozen.json`");
        sb.AppendLine();
        foreach (var n in r.Notes)
        {
            sb.AppendLine($"- {n}");
        }

        return sb.ToString();
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }
    }

    public static string FrozenJson(Btc15mFitResult r)
    {
        var s = r.Selected;
        var obj = new
        {
            classification = r.Classification,
            createdUtc = DateTimeOffset.UtcNow,
            symbol = "BTCUSDT",
            timeframe = "15m",
            market = "Binance USD-M Isolated",
            live = "OFF",
            validatedForPaper = false,
            dataStartUtc = r.DataStart,
            dataEndUtc = r.DataEnd,
            recipe = s?.Recipe,
            rules = s?.Rules,
            side = s?.Side,
            indicators = s?.Recipe,
            parameters = s is null ? null : new
            {
                slPct = s.SlPct,
                tpPct = s.TpPct,
                maxHoldBars = s.MaxHoldBars,
                maxHoldMinutes = s.MaxHoldBars * 15
            },
            longRules = s is null ? null : s.Side is "LONG" or "BOTH" ? s.Rules : "disabled",
            shortRules = s is null ? null : s.Side is "SHORT" or "BOTH" ? s.Rules : "disabled",
            sl = s?.SlPct,
            tp = s?.TpPct,
            timeExitBars = s?.MaxHoldBars,
            positionSizing = new
            {
                startEquity = 1000,
                riskPercent = 0.5,
                leverageCap = 3,
                dailyHaltPercent = 3,
                maxPositions = 1,
                isolated = true
            },
            execution = "Model B: closed 15m signal, next 15m open, SL before TP",
            fees = new { takerPercentPerSide = 0.04, model = "percent of notional" },
            slippage = new { percentPerSide = 0.02, model = "fill price adjustment" },
            funding = "NOT_APPLIED"
        };
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        });
    }

    public static string RegimeTable(IReadOnlyList<Btc15mTrade> trades, IReadOnlyList<MarketCandle> candles)
    {
        if (trades.Count == 0)
        {
            return "No trades.";
        }

        var cache = new CausalIndicatorCache(candles);
        var atr = cache.AtrPercent(14);
        var ema20 = cache.Ema(20);
        var ema50 = cache.Ema(50);
        var idx = new Dictionary<DateTimeOffset, int>(candles.Count);
        for (var i = 0; i < candles.Count; i++)
        {
            idx[candles[i].OpenTime] = i;
        }

        var buckets = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        void Add(string k, double v)
        {
            if (!buckets.TryGetValue(k, out var list))
            {
                list = [];
                buckets[k] = list;
            }

            list.Add(v);
        }

        var window = new Queue<double>();
        foreach (var tr in trades)
        {
            if (!idx.TryGetValue(tr.Opened, out var fill) || fill < 1)
            {
                continue;
            }

            var i = fill - 1;
            var ap = atr[i] is { } a ? (double)a : double.NaN;
            window.Clear();
            for (var j = Math.Max(0, i - 49); j <= i; j++)
            {
                if (atr[j] is { } x)
                {
                    window.Enqueue((double)x);
                }
            }

            var vol = "VOL_MID";
            if (!double.IsNaN(ap) && window.Count >= 10)
            {
                var arr = window.ToArray();
                Array.Sort(arr);
                var rank = Array.BinarySearch(arr, ap);
                if (rank < 0)
                {
                    rank = ~rank;
                }

                var p = rank / (double)Math.Max(1, arr.Length - 1);
                vol = p < 1.0 / 3.0 ? "VOL_LOW" : p > 2.0 / 3.0 ? "VOL_HIGH" : "VOL_MID";
            }

            var trend = "SIDEWAYS";
            if (ema20[i] is { } f && ema50[i] is { } sl)
            {
                trend = f > sl ? "BULL_EMA" : f < sl ? "BEAR_EMA" : "SIDEWAYS";
            }

            Add(vol, tr.Net);
            Add(trend, tr.Net);
        }

        var sb = new StringBuilder();
        sb.AppendLine("| Regime | n | Net $ |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var kv in buckets.OrderBy(x => x.Key))
        {
            sb.AppendLine($"| {kv.Key} | {kv.Value.Count} | {kv.Value.Sum():0.00} |");
        }

        return sb.ToString();
    }

    private static void MonthTable(StringBuilder sb, IReadOnlyList<Btc15mTrade> trades, string fmt)
    {
        if (trades.Count == 0)
        {
            sb.AppendLine("n/a");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Month | n | Net $ |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var g in trades.GroupBy(t => t.Closed.ToString(fmt)).OrderBy(x => x.Key))
        {
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Sum(x => x.Net):0.00} |");
        }

        var pos = trades.GroupBy(t => t.Closed.ToString(fmt)).Count(g => g.Sum(x => x.Net) > 0);
        var neg = trades.GroupBy(t => t.Closed.ToString(fmt)).Count(g => g.Sum(x => x.Net) < 0);
        sb.AppendLine();
        sb.AppendLine($"Profitable months {pos}, losing months {neg}.");
        sb.AppendLine();
    }

    private static void QuarterTable(StringBuilder sb, IReadOnlyList<Btc15mTrade> trades)
    {
        if (trades.Count == 0)
        {
            sb.AppendLine("n/a");
            sb.AppendLine();
            return;
        }

        sb.AppendLine("| Quarter | n | Net $ |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var g in trades.GroupBy(t => $"{t.Closed.Year}-Q{(t.Closed.Month - 1) / 3 + 1}").OrderBy(x => x.Key))
        {
            sb.AppendLine($"| {g.Key} | {g.Count()} | {g.Sum(x => x.Net):0.00} |");
        }

        sb.AppendLine();
    }

    private static (int cw, int cl) Streaks(IReadOnlyList<Btc15mTrade> t)
    {
        var cw = 0;
        var cl = 0;
        var w = 0;
        var l = 0;
        foreach (var x in t)
        {
            if (x.Net > 0)
            {
                w++;
                l = 0;
                cw = Math.Max(cw, w);
            }
            else if (x.Net < 0)
            {
                l++;
                w = 0;
                cl = Math.Max(cl, l);
            }
        }

        return (cw, cl);
    }

    private static double PfSide(IReadOnlyList<Btc15mTrade> t, int side)
    {
        var rows = t.Where(x => x.Side == side).Select(x => x.Net).ToList();
        var w = rows.Where(v => v > 0).Sum();
        var l = rows.Where(v => v < 0).Sum();
        return Math.Abs(l) < 1e-12 ? (w > 0 ? double.PositiveInfinity : double.NaN) : w / Math.Abs(l);
    }

    private static string Num(double v) =>
        double.IsNaN(v) ? "n/a" : double.IsInfinity(v) ? "Inf" : v.ToString("0.0000", CultureInfo.InvariantCulture);

    private static string Pct(double v) =>
        double.IsNaN(v) ? "n/a" : v.ToString("0.00%", CultureInfo.InvariantCulture);

    private static string PctN(double v) => (v * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";
}
