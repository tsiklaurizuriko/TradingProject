using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Research;

namespace TradingPlatform.StrategyResearch;

internal static class ScalpingCoverage
{
    public static readonly string[] Timeframes = ["1m", "3m", "5m", "15m"];

    public static readonly string[] Universe =
    [
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT", "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "LINKUSDT", "LTCUSDT",
        "DOTUSDT", "NEARUSDT", "UNIUSDT", "ATOMUSDT", "APTUSDT", "SUIUSDT", "TONUSDT", "FILUSDT", "ARBUSDT", "OPUSDT"
    ];

    public static DateTimeOffset WindowStart(string timeframe, DateTimeOffset end) =>
        timeframe is "1m" or "3m" ? end.AddDays(-90) : end.AddDays(-365);

    public static async Task<IReadOnlyList<ScalpingCoverageRow>> RunAsync(
        HttpClient http,
        string cacheDir,
        string outDir,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outDir);
        var end = DateTimeOffset.UtcNow;
        var rows = new List<ScalpingCoverageRow>();
        foreach (var timeframe in Timeframes)
        {
            var start = WindowStart(timeframe, end);
            foreach (var symbol in Universe)
            {
                Exception? last = null;
                for (var attempt = 1; attempt <= 4; attempt++)
                {
                    try
                    {
                        var (loaded, hit, got) = await ResearchKlineCache.LoadAsync(
                            http, cacheDir, symbol, timeframe, start, end, cancellationToken);
                        var closed = loaded.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList();
                        var taker = ResearchKlineCache.TakerCoverage(closed);
                        var gaps = ResearchKlineCache.GapCount(closed, timeframe);
                        var status = closed.Count < 80
                            ? ResearchStatuses.InsufficientData
                            : ResearchStatuses.Researching;
                        var row = new ScalpingCoverageRow(
                            symbol,
                            timeframe,
                            closed.Count == 0 ? null : closed[0].OpenTime,
                            closed.Count == 0 ? null : closed[^1].CloseTime,
                            "fapi.binance.com/fapi/v1/klines",
                            timeframe,
                            gaps,
                            closed.Count,
                            got,
                            hit,
                            taker,
                            status,
                            taker < 0.5
                                ? "Taker buy volume sparse. Taker strategies stay DATA_UNAVAILABLE (not treated as 0 imbalance). Funding/OI/mark/index not in this OHLCV cache."
                                : "OHLCV only. Funding/OI/mark/index not persisted here. Liquidations DATA_UNAVAILABLE.");
                        rows.Add(row);
                        Console.WriteLine($"{symbol} {timeframe} bars={closed.Count} gaps={gaps} taker={taker:P0} {status}");
                        last = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        await Task.Delay(250 * attempt, cancellationToken);
                    }
                }

                if (last is not null)
                {
                    rows.Add(new ScalpingCoverageRow(
                        symbol,
                        timeframe,
                        null,
                        null,
                        "fapi.binance.com/fapi/v1/klines",
                        timeframe,
                        0,
                        0,
                        0,
                        false,
                        0,
                        ResearchStatuses.DataUnavailable,
                        last.Message));
                    Console.WriteLine($"{symbol} {timeframe} DATA_UNAVAILABLE ({last.Message})");
                }
            }
        }

        var jsonPath = Path.Combine(outDir, "coverage.json");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        var mdPath = Path.Combine(outDir, "coverage.md");
        await File.WriteAllTextAsync(mdPath, ScalpingReport.CoverageMarkdown(rows), cancellationToken);
        return rows;
    }
}
