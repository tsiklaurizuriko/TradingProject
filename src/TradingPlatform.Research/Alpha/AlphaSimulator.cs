using TradingPlatform.Research.Framework;

namespace TradingPlatform.Research.Alpha;

/// <summary>Hour range [From, To) of the panel that a simulation may score. OOS ranges come only from an <see cref="OosTicket"/>.</summary>
public sealed record SimWindow
{
    private SimWindow(int from, int to, string split)
    {
        From = from;
        To = to;
        Split = split;
    }

    public int From { get; }
    public int To { get; }
    public string Split { get; }

    public static SimWindow InSample(AlphaSplits s) => new(s.FromDay * 24, s.InSampleEndDay * 24, "IS");

    public static SimWindow Validation(AlphaSplits s) => new(s.InSampleEndDay * 24, s.ValidationEndDay * 24, "Validation");

    public static SimWindow Oos(AlphaSplits s, OosTicket ticket)
    {
        if (ticket.From + s.FromDay != s.ValidationEndDay || ticket.To + s.FromDay != s.ToDay)
        {
            throw new InvalidOperationException("The OOS ticket does not match these splits.");
        }

        return new SimWindow(s.ValidationEndDay * 24, s.ToDay * 24, "OOS");
    }
}

/// <summary>60/20/20 by UTC day over panel days [FromDay, ToDay), using <see cref="OosVault.Split"/>.</summary>
public sealed record AlphaSplits(int FromDay, int ToDay, int InSampleEndDay, int ValidationEndDay)
{
    public int Days => ToDay - FromDay;

    public static AlphaSplits For(int fromDay, int toDay)
    {
        var s = OosVault.Split(toDay - fromDay);
        return new AlphaSplits(fromDay, toDay, fromDay + s.InSampleEnd, fromDay + s.ValidationEnd);
    }
}

/// <summary>
/// Produces target weights (fractions of the book, + long / − short) at the close of decision bars.
/// It may read panel data at hours ≤ <c>hour</c> only.
/// </summary>
public interface ITargetModel
{
    bool IsDecision(int hour);

    /// <summary>Fills <paramref name="target"/> (zeroed by the caller) for the close of bar <paramref name="hour"/>.</summary>
    void Targets(int hour, double[] target);
}

public sealed record SimOptions(CostProfileKind Cost, double Book = 100_000d, double GrossCap = 1d);

public sealed record CoinStats(string Symbol, int Entries, double Net, double Gross, double Costs, double Funding);

public sealed class SimResult
{
    public required string Split { get; init; }
    public required CostProfileKind Cost { get; init; }
    public required int FromDay { get; init; }
    public required double[] DailyReturns { get; init; }

    /// <summary>Daily price PnL before fees, slippage and funding (fractions of the book).</summary>
    public required double[] DailyGrossReturns { get; init; }
    public required double Gross { get; init; }
    public required double Fees { get; init; }
    public required double Slippage { get; init; }
    public required double Funding { get; init; }
    public required double Turnover { get; init; }
    public required int Entries { get; init; }
    public required int DelistExits { get; init; }
    public required double AverageGross { get; init; }
    public required double AverageNet { get; init; }
    public required IReadOnlyList<CoinStats> Coins { get; init; }
    public double Book { get; init; }
    public double Net => Gross - Fees - Slippage - Funding;
}

/// <summary>
/// Hourly book simulation. Weights decided at the close of bar h execute at the close of bar h + delay and earn
/// close-to-close returns from the next bar. Each unit of turnover pays the per-side cost of the coin's liquidity
/// bucket plus square-root impact on the trailing median daily quote volume. Positions pay the funding settled in
/// each bar they are held over (long pays positive funding). A held coin whose bar is missing is closed at its
/// last close and counted as a delist exit. The window starts and ends flat.
/// </summary>
public static class AlphaSimulator
{
    public static SimResult Run(PanelUniverse universe, ITargetModel model, SimWindow window, SimOptions options)
    {
        var panel = universe.Panel;
        var n = panel.Coins;
        var delay = AlphaCosts.DelayBars(options.Cost);
        var fee = AlphaCosts.FeePercent(options.Cost);
        var weights = new double[n];
        var target = new double[n];
        var pending = new Queue<(int ExecuteAt, double[] Target)>();
        var days = (window.To - window.From + 23) / 24;
        var daily = new double[days];
        var dailyGross = new double[days];
        var coinNet = new double[n];
        var coinGross = new double[n];
        var coinCost = new double[n];
        var coinFunding = new double[n];
        var coinEntries = new int[n];
        double gross = 0, fees = 0, slippage = 0, funding = 0, turnover = 0, sumGross = 0, sumNet = 0;
        var entries = 0;
        var delists = 0;
        var book = options.Book;

        for (var t = window.From; t < window.To; t++)
        {
            var day = (t - window.From) / 24;
            var pnl = 0d;
            for (var c = 0; c < n; c++)
            {
                var w = weights[c];
                if (w == 0d)
                {
                    continue;
                }

                var close = panel.Close[c][t];
                var prev = t > 0 ? panel.Close[c][t - 1] : float.NaN;
                if (float.IsNaN(close) || float.IsNaN(prev))
                {
                    var cost = Trade(universe, c, t - 1, -w, options, fee, ref fees, ref slippage, ref turnover);
                    coinCost[c] += cost;
                    coinNet[c] -= cost;
                    pnl -= cost;
                    weights[c] = 0d;
                    delists++;
                    continue;
                }

                var g = w * (close / (double)prev - 1d) * book;
                pnl += g;
                gross += g;
                dailyGross[day] += g / book;
                coinGross[c] += g;
                coinNet[c] += g;
                var f = panel.Funding[c][t];
                if (!float.IsNaN(f))
                {
                    var paid = w * f * book;
                    funding += paid;
                    coinFunding[c] += paid;
                    coinNet[c] -= paid;
                    pnl -= paid;
                }
            }

            var last = t == window.To - 1;
            if (!last && model.IsDecision(t))
            {
                Array.Clear(target);
                model.Targets(t, target);
                CapGross(target, options.GrossCap);
                pending.Enqueue((t + delay, (double[])target.Clone()));
            }

            double[]? execute = null;
            while (pending.Count > 0 && pending.Peek().ExecuteAt <= t)
            {
                execute = pending.Dequeue().Target;
            }

            if (last)
            {
                execute = new double[n];
            }

            if (execute is not null)
            {
                for (var c = 0; c < n; c++)
                {
                    var want = execute[c];
                    if (want != 0d && float.IsNaN(panel.Close[c][t]))
                    {
                        want = 0d;
                    }

                    var delta = want - weights[c];
                    if (delta == 0d)
                    {
                        continue;
                    }

                    var cost = Trade(universe, c, t, delta, options, fee, ref fees, ref slippage, ref turnover);
                    coinCost[c] += cost;
                    coinNet[c] -= cost;
                    pnl -= cost;
                    if (want != 0d && (weights[c] == 0d || Math.Sign(want) != Math.Sign(weights[c])))
                    {
                        entries++;
                        coinEntries[c]++;
                    }

                    weights[c] = want;
                }
            }

            daily[day] += pnl / book;
            var g2 = 0d;
            var net = 0d;
            foreach (var w in weights)
            {
                g2 += Math.Abs(w);
                net += w;
            }

            sumGross += g2;
            sumNet += net;
        }

        var hours = Math.Max(1, window.To - window.From);
        var coins = new List<CoinStats>();
        for (var c = 0; c < n; c++)
        {
            if (coinEntries[c] > 0)
            {
                coins.Add(new CoinStats(panel.Symbols[c], coinEntries[c], coinNet[c], coinGross[c], coinCost[c], coinFunding[c]));
            }
        }

        return new SimResult
        {
            Split = window.Split,
            Cost = options.Cost,
            FromDay = window.From / 24,
            DailyReturns = daily,
            DailyGrossReturns = dailyGross,
            Gross = gross,
            Fees = fees,
            Slippage = slippage,
            Funding = funding,
            Turnover = turnover,
            Entries = entries,
            DelistExits = delists,
            AverageGross = sumGross / hours,
            AverageNet = sumNet / hours,
            Coins = coins,
            Book = book
        };
    }

    private static double Trade(PanelUniverse universe, int coin, int hour, double delta, SimOptions options, double fee,
        ref double fees, ref double slippage, ref double turnover)
    {
        var notional = Math.Abs(delta) * options.Book;
        var day = Math.Clamp(hour, 0, universe.Panel.Hours - 1) / 24;
        var perSide = AlphaCosts.PerSidePercent(options.Cost, universe.MedianQuoteVolume[coin][day], universe.DailyVolatilityPercent[coin][day], notional);
        var feeCost = notional * fee / 100d;
        var slipCost = notional * (perSide - fee) / 100d;
        fees += feeCost;
        slippage += slipCost;
        turnover += Math.Abs(delta);
        return feeCost + slipCost;
    }

    private static void CapGross(double[] target, double cap)
    {
        var gross = 0d;
        foreach (var w in target)
        {
            gross += Math.Abs(w);
        }

        if (gross > cap && gross > 0d)
        {
            var scale = cap / gross;
            for (var i = 0; i < target.Length; i++)
            {
                target[i] *= scale;
            }
        }
    }
}
