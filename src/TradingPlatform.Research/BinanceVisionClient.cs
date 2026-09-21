using System.Globalization;
using System.IO.Compression;
using System.Net;

namespace TradingPlatform.Research;

public sealed record VisionMetricsPoint(
    DateTimeOffset CreateTime,
    string Symbol,
    decimal SumOpenInterest,
    decimal SumOpenInterestValue,
    decimal? CountTopTraderLongShortRatio,
    decimal? SumTopTraderLongShortRatio,
    decimal? CountLongShortRatio,
    decimal? SumTakerLongShortVolRatio);

/// <summary>
/// Binance Vision USD-M daily metrics (5m). Source: https://data.binance.vision/data/futures/um/daily/metrics/
/// Columns: create_time, symbol, sum_open_interest, sum_open_interest_value,
/// count_toptrader_long_short_ratio, sum_toptrader_long_short_ratio,
/// count_long_short_ratio, sum_taker_long_short_vol_ratio.
/// </summary>
public sealed class BinanceVisionClient
{
    public const string Source = "https://data.binance.vision/data/futures/um/daily/metrics/{symbol}/{symbol}-metrics-{yyyy-MM-dd}.zip";
    public const string Resolution = "5m";

    private readonly HttpClient _http;
    private readonly TimeSpan _pause;
    private readonly int _retries;

    public BinanceVisionClient(HttpClient http, TimeSpan? pause = null, int retries = 4)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _pause = pause ?? TimeSpan.FromMilliseconds(40);
        _retries = Math.Max(1, retries);
    }

    public async Task<IReadOnlyList<VisionMetricsPoint>> LoadMetricsAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        string cacheDir,
        CancellationToken cancellationToken = default)
    {
        var id = symbol.ToUpperInvariant();
        var zipDir = Path.Combine(cacheDir, id);
        Directory.CreateDirectory(zipDir);
        var rows = new List<VisionMetricsPoint>();
        var day = start.UtcDateTime.Date;
        var last = end.UtcDateTime.Date;
        while (day <= last)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var zipPath = Path.Combine(zipDir, $"{id}-metrics-{stamp}.zip");
            if (!File.Exists(zipPath) || new FileInfo(zipPath).Length < 32)
            {
                var url = $"data/futures/um/daily/metrics/{id}/{id}-metrics-{stamp}.zip";
                var ok = await DownloadZipAsync(url, zipPath, cancellationToken);
                if (!ok)
                {
                    day = day.AddDays(1);
                    continue;
                }
            }

            rows.AddRange(ReadZip(zipPath, id));
            day = day.AddDays(1);
        }

        return rows
            .GroupBy(r => r.CreateTime)
            .Select(g => g.Last())
            .OrderBy(r => r.CreateTime)
            .ToList();
    }

    public static List<VisionMetricsPoint> ParseCsv(string csv, string symbol)
    {
        var rows = new List<VisionMetricsPoint>();
        using var reader = new StringReader(csv);
        var header = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(header))
        {
            return rows;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length < 4)
            {
                continue;
            }

            if (!DateTime.TryParseExact(
                    parts[0].Trim(),
                    "yyyy-MM-dd HH:mm:ss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var created))
            {
                continue;
            }

            rows.Add(new VisionMetricsPoint(
                new DateTimeOffset(created, TimeSpan.Zero),
                parts[1].Trim().Length == 0 ? symbol : parts[1].Trim(),
                Dec(parts[2]),
                Dec(parts[3]),
                Opt(parts, 4),
                Opt(parts, 5),
                Opt(parts, 6),
                Opt(parts, 7)));
        }

        return rows;
    }

    public static List<VisionMetricsPoint> ReadZip(string zipPath, string symbol)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return [];
        }

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return ParseCsv(reader.ReadToEnd(), symbol);
    }

    /// <summary>
    /// Last 5m observation with CreateTime &lt;= candle close. No interpolation. Missing hours stay NaN.
    /// </summary>
    public static double[,] AlignToPanel(
        Wave3Panel panel,
        IReadOnlyDictionary<string, IReadOnlyList<VisionMetricsPoint>> bySymbol,
        Func<VisionMetricsPoint, double> select)
    {
        var values = new double[panel.Length, panel.Width];
        for (var s = 0; s < panel.Width; s++)
        {
            bySymbol.TryGetValue(panel.Symbols[s], out var series);
            series ??= [];
            var p = 0;
            for (var t = 0; t < panel.Length; t++)
            {
                var close = panel.Bars[t, s].CloseTime;
                while (p + 1 < series.Count && series[p + 1].CreateTime <= close)
                {
                    p++;
                }

                if (series.Count == 0 || p >= series.Count || series[p].CreateTime > close)
                {
                    values[t, s] = double.NaN;
                    continue;
                }

                values[t, s] = select(series[p]);
            }
        }

        return values;
    }

    public static double[,] PctChange(double[,] level, int lookback)
    {
        var n = level.GetLength(0);
        var w = level.GetLength(1);
        var d = new double[n, w];
        for (var t = 0; t < n; t++)
        {
            for (var s = 0; s < w; s++)
            {
                if (t < lookback)
                {
                    d[t, s] = double.NaN;
                    continue;
                }

                var prev = level[t - lookback, s];
                var now = level[t, s];
                d[t, s] = double.IsNaN(prev) || double.IsNaN(now) || Math.Abs(prev) < 1e-12
                    ? double.NaN
                    : now / prev - 1.0;
            }
        }

        return d;
    }

    public static double AsDouble(decimal? value) =>
        value is null ? double.NaN : (double)value.Value;

    private async Task<bool> DownloadZipAsync(string relativeUrl, string zipPath, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= _retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var response = await _http.GetAsync(relativeUrl, cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return false;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(_pause * attempt, cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    last = new HttpRequestException($"Vision {response.StatusCode} {relativeUrl}");
                    await Task.Delay(_pause * attempt, cancellationToken);
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                if (bytes.Length < 32)
                {
                    return false;
                }

                var tmp = zipPath + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes, cancellationToken);
                File.Move(tmp, zipPath, overwrite: true);
                if (_pause > TimeSpan.Zero)
                {
                    await Task.Delay(_pause, cancellationToken);
                }

                return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last = ex;
                await Task.Delay(_pause * attempt, cancellationToken);
            }
        }

        _ = last;
        return false;
    }

    private static decimal Dec(string raw) =>
        decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;

    private static decimal? Opt(string[] parts, int i)
    {
        if (i >= parts.Length || string.IsNullOrWhiteSpace(parts[i]))
        {
            return null;
        }

        return decimal.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
