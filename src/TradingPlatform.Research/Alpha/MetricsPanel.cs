using System.Globalization;
using System.IO.Compression;

namespace TradingPlatform.Research.Alpha;

/// <summary>
/// Vision daily metrics (5-minute prints) on the hourly grid of an <see cref="HourlyPanel"/>. Bar t takes the last
/// print with create_time strictly before its close, so a print stamped exactly at the close is used from the next bar.
/// </summary>
public sealed class MetricsPanel
{
    public MetricsPanel(HourlyPanel panel)
    {
        Panel = panel;
        OiValue = Alloc(panel);
        TopTraderRatio = Alloc(panel);
        GlobalRatio = Alloc(panel);
        TakerRatio = Alloc(panel);
    }

    public HourlyPanel Panel { get; }

    /// <summary>sum_open_interest_value (USDT).</summary>
    public float[][] OiValue { get; }

    /// <summary>sum_toptrader_long_short_ratio (top-trader positions).</summary>
    public float[][] TopTraderRatio { get; }

    /// <summary>count_long_short_ratio (all accounts).</summary>
    public float[][] GlobalRatio { get; }

    /// <summary>sum_taker_long_short_vol_ratio (taker buy / sell volume).</summary>
    public float[][] TakerRatio { get; }

    public double OiChange(int c, int t, int lookback)
    {
        if (t - lookback < 0)
        {
            return double.NaN;
        }

        var a = OiValue[c][t - lookback];
        var b = OiValue[c][t];
        return a > 0 && b > 0 ? b / (double)a - 1d : double.NaN;
    }

    /// <summary>z-score of the value at t against the 30 values sampled 24h apart before it.</summary>
    public double Z(float[][] field, int c, int t, int days = 30)
    {
        var now = field[c][t];
        if (float.IsNaN(now))
        {
            return double.NaN;
        }

        var values = new List<double>(days);
        for (var k = 1; k <= days; k++)
        {
            var at = t - 24 * k;
            if (at >= 0 && !float.IsNaN(field[c][at]))
            {
                values.Add(field[c][at]);
            }
        }

        if (values.Count < days * 0.8)
        {
            return double.NaN;
        }

        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        return sd > 1e-9 ? (now - mean) / sd : double.NaN;
    }

    public double Mean(float[][] field, int c, int t, int lookback)
    {
        if (t - lookback + 1 < 0)
        {
            return double.NaN;
        }

        var sum = 0d;
        var n = 0;
        for (var k = t - lookback + 1; k <= t; k++)
        {
            var v = field[c][k];
            if (!float.IsNaN(v))
            {
                sum += v;
                n++;
            }
        }

        return n < lookback * 0.8 ? double.NaN : sum / n;
    }

    public static MetricsPanel Load(HourlyPanel panel, string metricsDir)
    {
        var metrics = new MetricsPanel(panel);
        Parallel.For(0, panel.Coins, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, c =>
        {
            var dir = Path.Combine(metricsDir, panel.Symbols[c]);
            if (!Directory.Exists(dir))
            {
                return;
            }

            var stamp = new short[panel.Hours];
            Array.Fill(stamp, (short)-1);
            foreach (var zip in Directory.EnumerateFiles(dir, "*.zip"))
            {
                ZipArchive archive;
                try
                {
                    archive = ZipFile.OpenRead(zip);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                using (archive)
                {
                    foreach (var entry in archive.Entries)
                    {
                        using var reader = new StreamReader(entry.Open());
                        while (reader.ReadLine() is { } line)
                        {
                            metrics.Apply(c, line, stamp);
                        }
                    }
                }
            }
        });

        return metrics;
    }

    /// <summary>create_time,symbol,sum_open_interest,sum_open_interest_value,count_toptrader_long_short_ratio,sum_toptrader_long_short_ratio,count_long_short_ratio,sum_taker_long_short_vol_ratio</summary>
    /// <param name="stamp">Per-coin, per-hour second-of-hour of the print already stored (−1 for none); the latest print wins.</param>
    public void Apply(int coin, string line, short[] stamp)
    {
        var parts = line.Split(',');
        if (parts.Length < 8 || !DateTimeOffset.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at))
        {
            return;
        }

        var t = (int)Math.Floor((at - Panel.Start).TotalHours);
        if (t < 0 || t >= Panel.Hours)
        {
            return;
        }

        var seconds = (short)(at.Minute * 60 + at.Second);
        if (stamp[t] >= seconds)
        {
            return;
        }

        stamp[t] = seconds;
        Set(OiValue, coin, t, parts[3]);
        Set(TopTraderRatio, coin, t, parts[5]);
        Set(GlobalRatio, coin, t, parts[6]);
        Set(TakerRatio, coin, t, parts[7]);
    }

    private static void Set(float[][] field, int coin, int t, string raw)
    {
        field[coin][t] = float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && float.IsFinite(v) ? v : float.NaN;
    }

    private static float[][] Alloc(HourlyPanel panel)
    {
        var rows = new float[panel.Coins][];
        for (var c = 0; c < panel.Coins; c++)
        {
            rows[c] = new float[panel.Hours];
            Array.Fill(rows[c], float.NaN);
        }

        return rows;
    }
}
