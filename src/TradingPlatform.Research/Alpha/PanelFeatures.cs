namespace TradingPlatform.Research.Alpha;

/// <summary>
/// Causal panel features. Every value at hour t reads bars ≤ t only (window sums cover t-L+1..t). Results are NaN
/// when the window has fewer than 80% of its bars. Prefix sums are built lazily and shared.
/// </summary>
public sealed class PanelFeatures
{
    public const double MinCoverage = 0.8;
    public const int BetaWindowHours = 720;
    private readonly HourlyPanel _p;
    private readonly int _btc;
    private double[][]? _ret;
    private double[][]? _retSq;
    private int[][]? _count;
    private double[][]? _qv;
    private double[][]? _taker;
    private double[][]? _funding;
    private double[][]? _range;
    private double[][]? _close;
    private double[][]? _beta;
    private readonly object _gate = new();

    public PanelFeatures(PanelUniverse universe)
    {
        Universe = universe;
        _p = universe.Panel;
        _btc = _p.IndexOf("BTCUSDT");
    }

    public PanelUniverse Universe { get; }
    public int Btc => _btc;

    /// <summary>Simple return close(t)/close(t-L) - 1.</summary>
    public double Ret(int c, int t, int lookback)
    {
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var a = _p.Close[c][t - lookback];
        var b = _p.Close[c][t];
        return float.IsNaN(a) || float.IsNaN(b) ? double.NaN : b / (double)a - 1d;
    }

    /// <summary>Standard deviation of hourly returns over the last L bars.</summary>
    public double Vol(int c, int t, int lookback)
    {
        Ensure();
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var n = _count![c][t + 1] - _count[c][t + 1 - lookback];
        if (n < lookback * MinCoverage || n < 2)
        {
            return double.NaN;
        }

        var s = _ret![c][t + 1] - _ret[c][t + 1 - lookback];
        var s2 = _retSq![c][t + 1] - _retSq[c][t + 1 - lookback];
        var variance = (s2 - s * s / n) / (n - 1);
        return variance > 0 ? Math.Sqrt(variance) : double.NaN;
    }

    /// <summary>Return over L divided by hourly vol over <paramref name="volLookback"/> × sqrt(L).</summary>
    public double VolAdjustedRet(int c, int t, int lookback, int volLookback = 720)
    {
        var r = Ret(c, t, lookback);
        var v = Vol(c, t, volLookback);
        return double.IsNaN(r) || double.IsNaN(v) ? double.NaN : r / (v * Math.Sqrt(lookback));
    }

    /// <summary>BTC beta from the 720 hourly returns ending at the previous UTC day close.</summary>
    public double Beta(int c, int t)
    {
        EnsureBeta();
        var day = t / 24;
        return day < _beta![c].Length ? _beta[c][day] : double.NaN;
    }

    /// <summary>Return over L minus beta × BTC return over L.</summary>
    public double Residual(int c, int t, int lookback)
    {
        if (_btc < 0)
        {
            return double.NaN;
        }

        var r = Ret(c, t, lookback);
        var rb = Ret(_btc, t, lookback);
        var beta = c == _btc ? 1d : Beta(c, t);
        return double.IsNaN(r) || double.IsNaN(rb) || double.IsNaN(beta) ? double.NaN : r - beta * rb;
    }

    /// <summary>Sum of funding settled in the last L bars (fraction, e.g. 0.0001 = 1 bp).</summary>
    public double FundingSum(int c, int t, int lookback)
    {
        Ensure();
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var n = _count![c][t + 1] - _count[c][t + 1 - lookback];
        if (n < lookback * MinCoverage)
        {
            return double.NaN;
        }

        return _funding![c][t + 1] - _funding[c][t + 1 - lookback];
    }

    /// <summary>z-score of the 24h funding sum against its 30 previous daily values (sampled 24h apart).</summary>
    public double FundingZ(int c, int t, int days = 30)
    {
        var now = FundingSum(c, t, 24);
        if (double.IsNaN(now))
        {
            return double.NaN;
        }

        var values = new List<double>(days);
        for (var k = 1; k <= days; k++)
        {
            var v = FundingSum(c, t - 24 * k, 24);
            if (!double.IsNaN(v))
            {
                values.Add(v);
            }
        }

        if (values.Count < days * MinCoverage)
        {
            return double.NaN;
        }

        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        return sd > 1e-9 ? (now - mean) / sd : double.NaN;
    }

    /// <summary>Taker-buy share of quote volume over the last L bars, minus 0.5.</summary>
    public double TakerImbalance(int c, int t, int lookback)
    {
        Ensure();
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var qv = _qv![c][t + 1] - _qv[c][t + 1 - lookback];
        var tb = _taker![c][t + 1] - _taker[c][t + 1 - lookback];
        var n = _count![c][t + 1] - _count[c][t + 1 - lookback];
        return n < lookback * MinCoverage || qv <= 0 ? double.NaN : tb / qv - 0.5d;
    }

    /// <summary>Quote volume over the last L bars relative to the average L-bar volume of the preceding <paramref name="baseline"/> bars.</summary>
    public double VolumeRatio(int c, int t, int lookback, int baseline = 720)
    {
        Ensure();
        if (t - lookback - baseline < 0)
        {
            return double.NaN;
        }

        var recent = _qv![c][t + 1] - _qv[c][t + 1 - lookback];
        var before = _qv[c][t + 1 - lookback] - _qv[c][t + 1 - lookback - baseline];
        var nRecent = _count![c][t + 1] - _count[c][t + 1 - lookback];
        var nBefore = _count[c][t + 1 - lookback] - _count[c][t + 1 - lookback - baseline];
        if (nRecent < lookback * MinCoverage || nBefore < baseline * MinCoverage || before <= 0)
        {
            return double.NaN;
        }

        return recent / nRecent / (before / nBefore);
    }

    /// <summary>Average of (high-low)/close over the last L bars.</summary>
    public double AverageRange(int c, int t, int lookback)
    {
        Ensure();
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var n = _count![c][t + 1] - _count[c][t + 1 - lookback];
        return n < lookback * MinCoverage ? double.NaN : (_range![c][t + 1] - _range[c][t + 1 - lookback]) / n;
    }

    public double BarRange(int c, int t)
    {
        var close = _p.Close[c][t];
        return float.IsNaN(close) ? double.NaN : (_p.High[c][t] - _p.Low[c][t]) / (double)close;
    }

    /// <summary>Highest high of bars t-L..t-1 (the current bar excluded).</summary>
    public double PriorHigh(int c, int t, int lookback)
    {
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var row = _p.High[c];
        var max = double.NaN;
        var n = 0;
        for (var k = t - lookback; k < t; k++)
        {
            var v = row[k];
            if (float.IsNaN(v))
            {
                continue;
            }

            n++;
            max = double.IsNaN(max) || v > max ? v : max;
        }

        return n < lookback * MinCoverage ? double.NaN : max;
    }

    /// <summary>Lowest low of bars t-L..t-1 (the current bar excluded).</summary>
    public double PriorLow(int c, int t, int lookback)
    {
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var row = _p.Low[c];
        var min = double.NaN;
        var n = 0;
        for (var k = t - lookback; k < t; k++)
        {
            var v = row[k];
            if (float.IsNaN(v))
            {
                continue;
            }

            n++;
            min = double.IsNaN(min) || v < min ? v : min;
        }

        return n < lookback * MinCoverage ? double.NaN : min;
    }

    /// <summary>Simple moving average of closes over the last L bars.</summary>
    public double Sma(int c, int t, int lookback)
    {
        Ensure();
        if (t - lookback + 1 < 0)
        {
            return double.NaN;
        }

        var n = _count![c][t + 1] - _count[c][t + 1 - lookback];
        return n < lookback * MinCoverage ? double.NaN : (_close![c][t + 1] - _close[c][t + 1 - lookback]) / n;
    }

    /// <summary>Cross-sectional statistics over eligible coins at hour t, cached per (t, lookback).</summary>
    public MarketState Market(int t, int lookback)
    {
        var key = ((long)t << 16) | (uint)lookback;
        lock (_gate)
        {
            if (_market.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var rets = new List<double>();
        var funding = new List<double>();
        for (var c = 0; c < _p.Coins; c++)
        {
            if (!Universe.Eligible(c, t))
            {
                continue;
            }

            var r = Ret(c, t, lookback);
            if (!double.IsNaN(r))
            {
                rets.Add(r);
            }

            var f = FundingSum(c, t, 24);
            if (!double.IsNaN(f))
            {
                funding.Add(f);
            }
        }

        MarketState state;
        if (rets.Count < CrossSectionalModel.MinimumCoins)
        {
            state = new MarketState(rets.Count, double.NaN, double.NaN, double.NaN, double.NaN);
        }
        else
        {
            var mean = rets.Average();
            var sd = Math.Sqrt(rets.Sum(r => (r - mean) * (r - mean)) / (rets.Count - 1));
            funding.Sort();
            state = new MarketState(
                rets.Count,
                rets.Count(r => r > 0) / (double)rets.Count,
                sd,
                mean,
                funding.Count > 0 ? funding[funding.Count / 2] : double.NaN);
        }

        lock (_gate)
        {
            _market[key] = state;
        }

        return state;
    }

    private readonly Dictionary<long, MarketState> _market = new();

    private void Ensure()
    {
        if (_ret is not null)
        {
            return;
        }

        lock (_gate)
        {
            if (_ret is not null)
            {
                return;
            }

            var n = _p.Coins;
            var hours = _p.Hours;
            var ret = new double[n][];
            var retSq = new double[n][];
            var count = new int[n][];
            var qv = new double[n][];
            var taker = new double[n][];
            var funding = new double[n][];
            var range = new double[n][];
            var closes = new double[n][];
            Parallel.For(0, n, c =>
            {
                var close = _p.Close[c];
                var cs = new double[hours + 1];
                var r = new double[hours + 1];
                var r2 = new double[hours + 1];
                var k = new int[hours + 1];
                var q = new double[hours + 1];
                var tb = new double[hours + 1];
                var f = new double[hours + 1];
                var rg = new double[hours + 1];
                for (var t = 0; t < hours; t++)
                {
                    var x = 0d;
                    var present = 0;
                    var vq = 0d;
                    var vt = 0d;
                    var vr = 0d;
                    if (!float.IsNaN(close[t]))
                    {
                        present = 1;
                        if (t > 0 && !float.IsNaN(close[t - 1]))
                        {
                            x = close[t] / (double)close[t - 1] - 1d;
                        }

                        var qq = _p.QuoteVolume[c][t];
                        var tt = _p.TakerBuyQuote[c][t];
                        vq = float.IsFinite(qq) ? qq : 0d;
                        vt = float.IsFinite(tt) ? tt : 0d;
                        vr = (_p.High[c][t] - _p.Low[c][t]) / (double)close[t];
                    }

                    var fr = _p.Funding[c][t];
                    r[t + 1] = r[t] + x;
                    r2[t + 1] = r2[t] + x * x;
                    k[t + 1] = k[t] + present;
                    q[t + 1] = q[t] + vq;
                    tb[t + 1] = tb[t] + vt;
                    f[t + 1] = f[t] + (float.IsNaN(fr) ? 0d : fr);
                    rg[t + 1] = rg[t] + (double.IsFinite(vr) ? vr : 0d);
                    cs[t + 1] = cs[t] + (present == 1 ? close[t] : 0d);
                }

                closes[c] = cs;
                ret[c] = r;
                retSq[c] = r2;
                count[c] = k;
                qv[c] = q;
                taker[c] = tb;
                funding[c] = f;
                range[c] = rg;
            });

            _retSq = retSq;
            _count = count;
            _qv = qv;
            _taker = taker;
            _funding = funding;
            _range = range;
            _close = closes;
            _ret = ret;
        }
    }

    private void EnsureBeta()
    {
        if (_beta is not null)
        {
            return;
        }

        lock (_gate)
        {
            if (_beta is not null)
            {
                return;
            }

            var n = _p.Coins;
            var days = _p.Days + 1;
            var beta = new double[n][];
            Parallel.For(0, n, c =>
            {
                var row = new double[days];
                Array.Fill(row, double.NaN);
                if (_btc >= 0)
                {
                    for (var d = BetaWindowHours / 24; d < days; d++)
                    {
                        var end = d * 24 - 1;
                        if (end >= _p.Hours)
                        {
                            break;
                        }

                        double sx = 0, sy = 0, sxx = 0, sxy = 0;
                        var k = 0;
                        for (var t = end - BetaWindowHours + 1; t <= end; t++)
                        {
                            if (t < 1)
                            {
                                continue;
                            }

                            var a0 = _p.Close[c][t - 1];
                            var a1 = _p.Close[c][t];
                            var b0 = _p.Close[_btc][t - 1];
                            var b1 = _p.Close[_btc][t];
                            if (float.IsNaN(a0) || float.IsNaN(a1) || float.IsNaN(b0) || float.IsNaN(b1))
                            {
                                continue;
                            }

                            var y = a1 / (double)a0 - 1d;
                            var x = b1 / (double)b0 - 1d;
                            sx += x;
                            sy += y;
                            sxx += x * x;
                            sxy += x * y;
                            k++;
                        }

                        if (k >= BetaWindowHours * MinCoverage)
                        {
                            var vx = sxx - sx * sx / k;
                            row[d] = vx > 0 ? (sxy - sx * sy / k) / vx : double.NaN;
                        }
                    }
                }

                beta[c] = row;
            });

            _beta = beta;
        }
    }
}

public sealed record MarketState(int Coins, double Breadth, double Dispersion, double MeanReturn, double MedianFunding24h);
