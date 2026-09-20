using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public static class FuturesHistoryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static string Root(string artifactsDir) =>
        Path.Combine(artifactsDir, "data", ResearchDatasets.Version);

    public static async Task<IReadOnlyList<FundingPoint>> LoadOrFetchFundingAsync(
        BinanceFuturesHistoryClient client,
        string root,
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, "funding", $"{symbol}.json");
        var existing = force ? [] : await ReadAsync<FundingPoint>(path, cancellationToken);
        List<FundingPoint> merged = existing.ToList();
        if (force
            || existing.Count == 0
            || existing[0].FundingTime > start.AddHours(8)
            || existing[^1].FundingTime < end.AddHours(-8))
        {
            var fetched = await client.GetFundingAsync(symbol, start, end, cancellationToken);
            merged = Merge(existing, fetched, x => x.FundingTime);
            await WriteAsync(path, merged, cancellationToken);
        }

        return Slice(merged, start, end, x => x.FundingTime);
    }

    public static async Task<IReadOnlyList<PricePoint>> LoadOrFetchMarkAsync(
        BinanceFuturesHistoryClient client,
        string root,
        string symbol,
        string interval,
        DateTimeOffset start,
        DateTimeOffset end,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, "mark", $"{symbol}_{interval}.json");
        var existing = force ? [] : await ReadAsync<PricePoint>(path, cancellationToken);
        var merged = existing.ToList();
        if (NeedsRefresh(existing, start, end, x => x.OpenTime, x => x.CloseTime, force))
        {
            var fetched = await client.GetMarkPriceKlinesAsync(symbol, interval, start, end, cancellationToken);
            merged = Merge(existing, fetched, x => x.OpenTime);
            await WriteAsync(path, merged, cancellationToken);
        }

        return Slice(merged, start, end, x => x.CloseTime);
    }

    public static async Task<IReadOnlyList<PricePoint>> LoadOrFetchIndexAsync(
        BinanceFuturesHistoryClient client,
        string root,
        string pair,
        string interval,
        DateTimeOffset start,
        DateTimeOffset end,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, "index", $"{pair}_{interval}.json");
        var existing = force ? [] : await ReadAsync<PricePoint>(path, cancellationToken);
        var merged = existing.ToList();
        if (NeedsRefresh(existing, start, end, x => x.OpenTime, x => x.CloseTime, force))
        {
            var fetched = await client.GetIndexPriceKlinesAsync(pair, interval, start, end, cancellationToken);
            merged = Merge(existing, fetched, x => x.OpenTime);
            await WriteAsync(path, merged, cancellationToken);
        }

        return Slice(merged, start, end, x => x.CloseTime);
    }

    public static async Task<IReadOnlyList<OpenInterestPoint>> LoadOrFetchOpenInterestAsync(
        BinanceFuturesHistoryClient client,
        string root,
        string symbol,
        string period,
        DateTimeOffset start,
        DateTimeOffset end,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, "oi", $"{symbol}_{period}.json");
        var existing = force ? [] : await ReadAsync<OpenInterestPoint>(path, cancellationToken);
        var merged = existing.ToList();
        var fetchStart = BinanceFuturesHistoryClient.ClampOpenInterestStart(start);
        if (NeedsRefresh(existing, fetchStart, end, x => x.Timestamp, x => x.Timestamp, force))
        {
            var fetched = await client.GetOpenInterestHistAsync(symbol, period, fetchStart, end, cancellationToken);
            merged = Merge(existing, fetched, x => x.Timestamp);
            await WriteAsync(path, merged, cancellationToken);
        }

        return Slice(merged, fetchStart, end, x => x.Timestamp);
    }

    public static async Task<IReadOnlyList<TakerFlowPoint>> WriteTakerAsync(
        string root,
        string symbol,
        string interval,
        IReadOnlyList<TakerFlowPoint> rows,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, "taker", $"{symbol}_{interval}.json");
        await WriteAsync(path, rows, cancellationToken);
        return rows;
    }

    public static async Task<IReadOnlyList<BasisPoint>> WriteBasisAsync(
        string root,
        string symbol,
        string interval,
        IReadOnlyList<PricePoint> mark,
        IReadOnlyList<PricePoint> index,
        CancellationToken cancellationToken = default)
    {
        var rows = BinanceFuturesHistoryClient.BuildBasis(mark, index);
        var path = Path.Combine(root, "basis", $"{symbol}_{interval}.json");
        await WriteAsync(path, rows, cancellationToken);
        return rows;
    }

    public static IReadOnlyList<FundingPoint> ReadFunding(string root, string symbol, DateTimeOffset start, DateTimeOffset end) =>
        Slice(ReadSync<FundingPoint>(Path.Combine(root, "funding", $"{symbol}.json")), start, end, x => x.FundingTime);

    public static IReadOnlyList<PricePoint> ReadMark(string root, string symbol, string interval, DateTimeOffset start, DateTimeOffset end) =>
        Slice(ReadSync<PricePoint>(Path.Combine(root, "mark", $"{symbol}_{interval}.json")), start, end, x => x.CloseTime);

    public static IReadOnlyList<PricePoint> ReadIndex(string root, string pair, string interval, DateTimeOffset start, DateTimeOffset end) =>
        Slice(ReadSync<PricePoint>(Path.Combine(root, "index", $"{pair}_{interval}.json")), start, end, x => x.CloseTime);

    public static IReadOnlyList<BasisPoint> ReadBasis(string root, string symbol, string interval, DateTimeOffset start, DateTimeOffset end) =>
        Slice(ReadSync<BasisPoint>(Path.Combine(root, "basis", $"{symbol}_{interval}.json")), start, end, x => x.CloseTime);

    public static IReadOnlyList<OpenInterestPoint> ReadOpenInterest(string root, string symbol, string period, DateTimeOffset start, DateTimeOffset end) =>
        Slice(ReadSync<OpenInterestPoint>(Path.Combine(root, "oi", $"{symbol}_{period}.json")), start, end, x => x.Timestamp);

    public static StrategyFuturesSeries Align(
        IReadOnlyList<MarketCandle> candles,
        IReadOnlyList<FundingPoint>? funding,
        IReadOnlyList<OpenInterestPoint>? oi,
        IReadOnlyList<PricePoint>? mark,
        IReadOnlyList<PricePoint>? index,
        IReadOnlyList<BasisPoint>? basis)
    {
        return new StrategyFuturesSeries
        {
            FundingRate = funding is null
                ? null
                : AlignedMarketSeries.Align(funding.Select(x => (x.FundingTime, x.FundingRate)).ToList(), candles),
            OpenInterest = oi is null
                ? null
                : AlignedMarketSeries.Align(oi.Select(x => (x.Timestamp, x.OpenInterest)).ToList(), candles),
            MarkPrice = mark is null
                ? null
                : AlignedMarketSeries.Align(mark.Select(x => (x.CloseTime, x.Close)).ToList(), candles),
            IndexPrice = index is null
                ? null
                : AlignedMarketSeries.Align(index.Select(x => (x.CloseTime, x.Close)).ToList(), candles),
            NormalizedBasis = basis is null
                ? null
                : AlignedMarketSeries.Align(basis.Select(x => (x.CloseTime, x.NormalizedBasis)).ToList(), candles)
        };
    }

    public static async Task WriteCheckpointAsync(
        string root,
        string dataset,
        string symbol,
        string interval,
        DateTimeOffset lastEnd,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(root, "meta", "checkpoint.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var line = JsonSerializer.Serialize(new
        {
            Dataset = dataset,
            Symbol = symbol,
            Interval = interval,
            Version = ResearchDatasets.Version,
            LastEndUtc = lastEnd
        });
        await File.AppendAllTextAsync(path, line + Environment.NewLine, cancellationToken);
    }

    private static bool NeedsRefresh<T>(
        IReadOnlyList<T> existing,
        DateTimeOffset start,
        DateTimeOffset end,
        Func<T, DateTimeOffset> open,
        Func<T, DateTimeOffset> close,
        bool force)
    {
        if (force || existing.Count == 0)
        {
            return true;
        }

        return open(existing[0]) > start.AddMinutes(30) || close(existing[^1]) < end.AddMinutes(-30);
    }

    private static List<T> Merge<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Func<T, DateTimeOffset> time) =>
        left.Concat(right).GroupBy(time).Select(g => g.Last()).OrderBy(time).ToList();

    private static IReadOnlyList<T> Slice<T>(IReadOnlyList<T> rows, DateTimeOffset start, DateTimeOffset end, Func<T, DateTimeOffset> time) =>
        rows.Where(r => time(r) >= start && time(r) <= end).ToList();

    private static List<T> ReadSync<T>(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<T>> ReadAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, cancellationToken: cancellationToken) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static async Task WriteAsync<T>(string path, IReadOnlyList<T> rows, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(rows, JsonOptions), cancellationToken);
    }
}
