using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TradingPlatform.Research.Alpha;

/// <summary>
/// Coins × UTC hours. Bar t opens at <see cref="Start"/> + t hours and closes one hour later; every field of bar t
/// is known only at that close. Missing bars are NaN. Funding[c][t] is the rate settled inside bar t (calc time in
/// (open, close]), NaN when nothing settled; a position held over bar t pays it.
/// </summary>
public sealed class HourlyPanel
{
    public HourlyPanel(DateTimeOffset start, int hours, IReadOnlyList<string> symbols)
    {
        Start = start;
        Hours = hours;
        Symbols = symbols;
        var n = symbols.Count;
        Close = Alloc(n, hours);
        High = Alloc(n, hours);
        Low = Alloc(n, hours);
        QuoteVolume = Alloc(n, hours);
        TakerBuyQuote = Alloc(n, hours);
        Funding = Alloc(n, hours);
    }

    public DateTimeOffset Start { get; }
    public int Hours { get; }
    public IReadOnlyList<string> Symbols { get; }
    public int Coins => Symbols.Count;
    public float[][] Close { get; }
    public float[][] High { get; }
    public float[][] Low { get; }
    public float[][] QuoteVolume { get; }
    public float[][] TakerBuyQuote { get; }
    public float[][] Funding { get; }

    public int Days => Hours / 24;

    public DateTimeOffset BarOpen(int t) => Start.AddHours(t);

    public DateTimeOffset BarClose(int t) => Start.AddHours(t + 1);

    public int IndexOf(string symbol)
    {
        for (var c = 0; c < Symbols.Count; c++)
        {
            if (string.Equals(Symbols[c], symbol, StringComparison.OrdinalIgnoreCase))
            {
                return c;
            }
        }

        return -1;
    }

    /// <summary>Hour index of the bar whose open is <paramref name="time"/> floored to the hour.</summary>
    public int HourIndex(DateTimeOffset time) => (int)Math.Floor((time - Start).TotalHours);

    private static float[][] Alloc(int n, int hours)
    {
        var rows = new float[n][];
        for (var c = 0; c < n; c++)
        {
            rows[c] = new float[hours];
            Array.Fill(rows[c], float.NaN);
        }

        return rows;
    }

    private const int Magic = 0x50484C41;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        w.Write(Magic);
        w.Write(Start.ToUnixTimeMilliseconds());
        w.Write(Hours);
        w.Write(Coins);
        foreach (var s in Symbols)
        {
            w.Write(s);
        }

        foreach (var field in Fields())
        {
            foreach (var row in field)
            {
                var bytes = new byte[row.Length * sizeof(float)];
                Buffer.BlockCopy(row, 0, bytes, 0, bytes.Length);
                w.Write(bytes);
            }
        }
    }

    public static HourlyPanel Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var r = new BinaryReader(stream);
        if (r.ReadInt32() != Magic)
        {
            throw new InvalidDataException($"{path} is not an hourly panel.");
        }

        var start = DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64());
        var hours = r.ReadInt32();
        var coins = r.ReadInt32();
        var symbols = new string[coins];
        for (var c = 0; c < coins; c++)
        {
            symbols[c] = r.ReadString();
        }

        var panel = new HourlyPanel(start, hours, symbols);
        foreach (var field in panel.Fields())
        {
            foreach (var row in field)
            {
                var bytes = r.ReadBytes(row.Length * sizeof(float));
                Buffer.BlockCopy(bytes, 0, row, 0, bytes.Length);
            }
        }

        return panel;
    }

    /// <summary>Stable hash over symbols, hours and every close and funding value.</summary>
    public string Fingerprint()
    {
        using var sha = SHA256.Create();
        var header = Encoding.UTF8.GetBytes($"{Start.ToUnixTimeMilliseconds()}|{Hours}|{string.Join(',', Symbols)}");
        sha.TransformBlock(header, 0, header.Length, null, 0);
        foreach (var field in new[] { Close, Funding })
        {
            foreach (var row in field)
            {
                var bytes = new byte[row.Length * sizeof(float)];
                Buffer.BlockCopy(row, 0, bytes, 0, bytes.Length);
                sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
            }
        }

        sha.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(sha.Hash!, 0, 8).ToLowerInvariant();
    }

    private IEnumerable<float[][]> Fields()
    {
        yield return Close;
        yield return High;
        yield return Low;
        yield return QuoteVolume;
        yield return TakerBuyQuote;
        yield return Funding;
    }
}

/// <summary>Reads data.binance.vision monthly 1h kline and funding zips into an <see cref="HourlyPanel"/>.</summary>
public static class VisionPanelLoader
{
    public static HourlyPanel Load(string visionDir, DateTimeOffset start, DateTimeOffset end, IReadOnlyList<string> symbols)
    {
        var hours = (int)(end - start).TotalHours;
        var panel = new HourlyPanel(start, hours, symbols);
        Parallel.For(0, symbols.Count, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, c =>
        {
            var symbol = symbols[c];
            var klineDir = Path.Combine(visionDir, "klines-1h", symbol);
            if (Directory.Exists(klineDir))
            {
                foreach (var zip in Directory.EnumerateFiles(klineDir, "*.zip"))
                {
                    foreach (var line in ReadZipLines(zip))
                    {
                        ApplyKline(panel, c, line);
                    }
                }
            }

            var fundingDir = Path.Combine(visionDir, "funding", symbol);
            if (Directory.Exists(fundingDir))
            {
                foreach (var zip in Directory.EnumerateFiles(fundingDir, "*.zip"))
                {
                    foreach (var line in ReadZipLines(zip))
                    {
                        ApplyFunding(panel, c, line);
                    }
                }
            }
        });

        return panel;
    }

    /// <summary>open_time,open,high,low,close,volume,close_time,quote_volume,count,taker_buy_volume,taker_buy_quote_volume,ignore. Older files have no header.</summary>
    public static void ApplyKline(HourlyPanel panel, int coin, string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 11 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var openMs))
        {
            return;
        }

        var t = panel.HourIndex(DateTimeOffset.FromUnixTimeMilliseconds(openMs));
        if (t < 0 || t >= panel.Hours)
        {
            return;
        }

        var close = Parse(parts[4]);
        if (!(close > 0f))
        {
            return;
        }

        panel.Close[coin][t] = close;
        panel.High[coin][t] = Parse(parts[2]);
        panel.Low[coin][t] = Parse(parts[3]);
        panel.QuoteVolume[coin][t] = Parse(parts[7]);
        panel.TakerBuyQuote[coin][t] = Parse(parts[10]);
    }

    /// <summary>calc_time,funding_interval_hours,last_funding_rate (rate is the last column in every layout).</summary>
    public static void ApplyFunding(HourlyPanel panel, int coin, string line)
    {
        var parts = line.Split(',');
        if (parts.Length < 2 || !long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var calcMs))
        {
            return;
        }

        var calc = DateTimeOffset.FromUnixTimeMilliseconds(calcMs);
        var t = (int)Math.Ceiling((calc - panel.Start).TotalHours) - 1;
        if (t < 0 || t >= panel.Hours)
        {
            return;
        }

        var rate = Parse(parts[^1]);
        if (float.IsFinite(rate))
        {
            var row = panel.Funding[coin];
            row[t] = float.IsNaN(row[t]) ? rate : row[t] + rate;
        }
    }

    private static float Parse(string s) =>
        float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : float.NaN;

    private static IEnumerable<string> ReadZipLines(string path)
    {
        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            yield break;
        }

        using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                while (reader.ReadLine() is { } line)
                {
                    yield return line;
                }
            }
        }
    }
}
