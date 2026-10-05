namespace TradingPlatform.Research.Framework;

public sealed record SharpeMoments(double Sharpe, int Observations, double Skewness, double Kurtosis);

/// <summary>
/// Multiple-testing statistics. Sharpe values here are per observation (not annualized).
/// Kurtosis is raw (normal = 3).
/// </summary>
public static class ResearchStatistics
{
    private const double EulerGamma = 0.5772156649015329;

    public static SharpeMoments Moments(IReadOnlyList<double> returns)
    {
        var n = returns.Count;
        if (n < 2)
        {
            return new SharpeMoments(0d, n, 0d, 3d);
        }

        var mean = returns.Average();
        var m2 = 0d;
        var m3 = 0d;
        var m4 = 0d;
        foreach (var r in returns)
        {
            var d = r - mean;
            var d2 = d * d;
            m2 += d2;
            m3 += d2 * d;
            m4 += d2 * d2;
        }

        m2 /= n;
        m3 /= n;
        m4 /= n;
        var sampleSd = Math.Sqrt(m2 * n / (n - 1));
        if (sampleSd <= 0d)
        {
            return new SharpeMoments(0d, n, 0d, 3d);
        }

        var skew = m2 <= 0d ? 0d : m3 / Math.Pow(m2, 1.5);
        var kurt = m2 <= 0d ? 3d : m4 / (m2 * m2);
        return new SharpeMoments(mean / sampleSd, n, skew, kurt);
    }

    /// <summary>Bailey and López de Prado probabilistic Sharpe ratio: P(true SR > benchmark).</summary>
    public static double ProbabilisticSharpe(SharpeMoments m, double benchmarkSharpe = 0d)
    {
        if (m.Observations < 2)
        {
            return 0d;
        }

        var denominator = 1d - m.Skewness * m.Sharpe + (m.Kurtosis - 1d) / 4d * m.Sharpe * m.Sharpe;
        if (denominator <= 0d)
        {
            denominator = 1e-12;
        }

        var z = (m.Sharpe - benchmarkSharpe) * Math.Sqrt(m.Observations - 1d) / Math.Sqrt(denominator);
        return NormalCdf(z);
    }

    /// <summary>Expected maximum Sharpe of <paramref name="trials"/> independent zero-skill trials.</summary>
    public static double ExpectedMaxSharpe(int trials, double sharpeVariance)
    {
        if (trials <= 1 || sharpeVariance <= 0d)
        {
            return 0d;
        }

        var sd = Math.Sqrt(sharpeVariance);
        return sd * ((1d - EulerGamma) * NormalInverse(1d - 1d / trials)
            + EulerGamma * NormalInverse(1d - 1d / (trials * Math.E)));
    }

    /// <summary>
    /// Deflated Sharpe probability. <paramref name="sharpeVariance"/> is the variance of per-observation
    /// Sharpe ratios across the family's trials. With one trial this equals the PSR against zero.
    /// </summary>
    public static double DeflatedSharpe(SharpeMoments m, int trials, double sharpeVariance) =>
        ProbabilisticSharpe(m, ExpectedMaxSharpe(trials, sharpeVariance));

    /// <summary>One-sided p-value for H0: true Sharpe ≤ 0.</summary>
    public static double SharpePValue(SharpeMoments m) => 1d - ProbabilisticSharpe(m);

    /// <summary>Benjamini–Hochberg step-up. Returns a discovery flag per input, in input order.</summary>
    public static bool[] BenjaminiHochberg(IReadOnlyList<double> pValues, double falseDiscoveryRate = 0.10)
    {
        var m = pValues.Count;
        var discoveries = new bool[m];
        if (m == 0)
        {
            return discoveries;
        }

        var order = Enumerable.Range(0, m).OrderBy(i => pValues[i]).ToArray();
        var cutoff = -1;
        for (var k = 0; k < m; k++)
        {
            if (pValues[order[k]] <= (k + 1d) / m * falseDiscoveryRate)
            {
                cutoff = k;
            }
        }

        for (var k = 0; k <= cutoff; k++)
        {
            discoveries[order[k]] = true;
        }

        return discoveries;
    }

    public static double Variance(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0d;
        }

        var mean = values.Average();
        return values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
    }

    public static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2d));

    /// <summary>Acklam's rational approximation of the standard normal quantile.</summary>
    public static double NormalInverse(double p)
    {
        if (p <= 0d)
        {
            return double.NegativeInfinity;
        }

        if (p >= 1d)
        {
            return double.PositiveInfinity;
        }

        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
        const double low = 0.02425;
        if (p < low)
        {
            var q = Math.Sqrt(-2d * Math.Log(p));
            return (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1d);
        }

        if (p > 1d - low)
        {
            var q = Math.Sqrt(-2d * Math.Log(1d - p));
            return -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5])
                / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1d);
        }

        var r = p - 0.5;
        var s = r * r;
        return (((((a[0] * s + a[1]) * s + a[2]) * s + a[3]) * s + a[4]) * s + a[5]) * r
            / (((((b[0] * s + b[1]) * s + b[2]) * s + b[3]) * s + b[4]) * s + 1d);
    }

    private static double Erfc(double x)
    {
        // Numerical Recipes erfc, fractional error < 1.2e-7.
        var z = Math.Abs(x);
        var t = 1d / (1d + 0.5 * z);
        var r = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 + t * (0.09678418
            + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 + t * (1.48851587
            + t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0d ? r : 2d - r;
    }
}
