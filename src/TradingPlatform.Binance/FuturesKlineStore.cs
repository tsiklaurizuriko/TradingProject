using System.Collections.Concurrent;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

/// <summary>
/// Closed USD-M candles per coin and interval, shared by every caller in the process.
/// REST loads and the market stream write here. Readers get the latest closed bars without a request while the series is current.
/// </summary>
public sealed class FuturesKlineStore
{
    public const int MaxRows = 1500;

    /// <summary>1m rows kept for every streamed coin, enough to build a 4h bar.</summary>
    public const int BaseRows = 250;

    /// <summary>Longest gap closed with a small tail request instead of a full reload.</summary>
    public const int MaxIncrementalBars = 96;

    public static readonly Timeframe[] Aggregated =
    [
        Timeframe.ThreeMinutes,
        Timeframe.FiveMinutes,
        Timeframe.FifteenMinutes,
        Timeframe.ThirtyMinutes,
        Timeframe.OneHour,
        Timeframe.TwoHours,
        Timeframe.FourHours
    ];

    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    public static FuturesKlineStore Shared { get; } = new();

    private readonly ConcurrentDictionary<(string Symbol, Timeframe Timeframe), Series> _series = new();
    private readonly ConcurrentQueue<AggregatedBar> _built = new();
    private volatile bool _aggregationOff;

    public readonly record struct Coverage(int Count, DateTimeOffset? CoveredFrom, int Missing, bool FromListing);

    public readonly record struct AggregatedBar(string Symbol, Timeframe Timeframe, MarketCandle Candle);

    public bool AggregationEnabled => !_aggregationOff;

    public string? AggregationOffReason { get; private set; }

    public SemaphoreSlim FetchLock(string symbol, Timeframe timeframe) => Get(symbol, timeframe).Fetch;

    /// <summary>Records that a caller reads this series, and how many rows it needs kept.</summary>
    public void Demand(string symbol, Timeframe timeframe, int rows, DateTimeOffset now)
    {
        var series = Get(symbol, timeframe);
        lock (series.Gate)
        {
            series.Want = Math.Max(series.Want, Math.Clamp(rows, 1, MaxRows));
            series.DemandedAt = now;
        }
    }

    /// <summary>The latest <paramref name="count"/> closed bars, when the series is current or was just reloaded.</summary>
    public bool TryLatest(string symbol, Timeframe timeframe, int count, DateTimeOffset now, out IReadOnlyList<MarketCandle> rows)
    {
        rows = [];
        if (!_series.TryGetValue(Key(symbol, timeframe), out var series))
        {
            return false;
        }

        lock (series.Gate)
        {
            if (series.Rows.Count == 0)
            {
                return now < series.EmptyUntil;
            }

            if (!Current(series, timeframe, now))
            {
                return false;
            }

            if (series.Rows.Count >= count)
            {
                rows = series.Rows.GetRange(series.Rows.Count - count, count);
                return true;
            }

            if (series.FromListing)
            {
                rows = series.Rows.ToList();
                return true;
            }

            return false;
        }
    }

    /// <summary>Closed bars opened in [start, end], when the series is current and reaches back to <paramref name="start"/>.</summary>
    public bool TryRange(
        string symbol,
        Timeframe timeframe,
        DateTimeOffset start,
        DateTimeOffset end,
        int cap,
        DateTimeOffset now,
        out IReadOnlyList<MarketCandle> rows)
    {
        rows = [];
        if (!_series.TryGetValue(Key(symbol, timeframe), out var series))
        {
            return false;
        }

        lock (series.Gate)
        {
            if (series.Rows.Count == 0)
            {
                return now < series.EmptyUntil;
            }

            if (!Current(series, timeframe, now) || (series.CoveredFrom > start && !series.FromListing))
            {
                return false;
            }

            rows = series.Rows
                .Where(row => row.OpenTime >= start && row.OpenTime <= end)
                .Take(cap)
                .ToList();
            return true;
        }
    }

    /// <summary>Latest rows whatever their age. Used when Binance has not published the newest bar yet.</summary>
    public IReadOnlyList<MarketCandle> Tail(string symbol, Timeframe timeframe, int count)
    {
        if (!_series.TryGetValue(Key(symbol, timeframe), out var series))
        {
            return [];
        }

        lock (series.Gate)
        {
            var take = Math.Min(count, series.Rows.Count);
            return series.Rows.GetRange(series.Rows.Count - take, take);
        }
    }

    public Coverage Cover(string symbol, Timeframe timeframe, DateTimeOffset now)
    {
        if (!_series.TryGetValue(Key(symbol, timeframe), out var series))
        {
            return new Coverage(0, null, 0, false);
        }

        lock (series.Gate)
        {
            if (series.Rows.Count == 0)
            {
                return new Coverage(0, null, 0, series.FromListing);
            }

            var step = timeframe.ToDuration();
            var nextClose = series.Rows[^1].CloseTime + step;
            var missing = 0;
            if (nextClose <= now)
            {
                missing = (int)Math.Min(int.MaxValue, Math.Floor((now - nextClose).Ticks / (double)step.Ticks) + 1);
            }

            return new Coverage(series.Rows.Count, series.CoveredFrom, missing, series.FromListing);
        }
    }

    /// <summary>
    /// Adds REST rows. Overlapping or adjacent rows join the series, a later block with a gap replaces it,
    /// and an older adjacent block extends it backwards.
    /// </summary>
    public void Merge(
        string symbol,
        Timeframe timeframe,
        IReadOnlyList<MarketCandle> fetched,
        DateTimeOffset coveredFrom,
        bool fromListing,
        DateTimeOffset now)
    {
        if (fetched.Count == 0)
        {
            return;
        }

        var series = Get(symbol, timeframe);
        var step = timeframe.ToDuration();
        lock (series.Gate)
        {
            var rows = series.Rows;
            var firstNew = fetched[0].OpenTime;
            var lastNew = fetched[^1].OpenTime;
            if (rows.Count > 0 && lastNew + Slack(timeframe, step) < rows[0].OpenTime)
            {
                return;
            }

            if (rows.Count == 0 || firstNew > rows[^1].OpenTime + Slack(timeframe, step))
            {
                series.Rows = KlineSeries.Normalize(fetched, out _).ToList();
                series.CoveredFrom = Min(coveredFrom, series.Rows[0].OpenTime);
                series.FromListing = fromListing;
                series.Aggregated = false;
            }
            else
            {
                var byOpen = new SortedDictionary<DateTimeOffset, MarketCandle>();
                foreach (var row in rows)
                {
                    byOpen[row.OpenTime] = row;
                }

                foreach (var row in fetched)
                {
                    byOpen[row.OpenTime] = row;
                }

                var extendsBack = firstNew < rows[0].OpenTime;
                series.Rows = byOpen.Values.ToList();
                if (extendsBack || coveredFrom < series.CoveredFrom)
                {
                    series.CoveredFrom = Min(coveredFrom, series.Rows[0].OpenTime);
                    series.FromListing = fromListing;
                }
            }

            series.EmptyUntil = default;
            series.HoldUntil = Current(series, timeframe, now) ? default : now + Hold;
            Trim(series, timeframe);
            series.Signal();
        }
    }

    /// <summary>Binance returned no rows: the coin is not listed yet or was removed. Avoid asking again for a while.</summary>
    public void MarkEmpty(string symbol, Timeframe timeframe, DateTimeOffset until)
    {
        var series = Get(symbol, timeframe);
        lock (series.Gate)
        {
            if (series.Rows.Count == 0)
            {
                series.EmptyUntil = until;
            }
        }
    }

    /// <summary>Short pause before asking Binance again when a reload did not bring the series up to date.</summary>
    public void HoldOff(string symbol, Timeframe timeframe, DateTimeOffset now)
    {
        var series = Get(symbol, timeframe);
        lock (series.Gate)
        {
            series.HoldUntil = now + Hold;
        }
    }

    /// <summary>
    /// A final 1m bar from the market stream. It extends the 1m series, and closes any demanded higher interval
    /// whose window it completes when that series is contiguous.
    /// </summary>
    public void AppendStreamBar(string symbol, MarketCandle bar)
    {
        var id = symbol.ToUpperInvariant();
        var baseSeries = Get(id, Timeframe.OneMinute);
        List<MarketCandle>? window = null;
        lock (baseSeries.Gate)
        {
            if (!Append(baseSeries, Timeframe.OneMinute, bar))
            {
                return;
            }

            baseSeries.StreamedAt = DateTimeOffset.UtcNow;
            if (!_aggregationOff)
            {
                window = baseSeries.Rows.Count > 1
                    ? baseSeries.Rows.GetRange(Math.Max(0, baseSeries.Rows.Count - 240), Math.Min(240, baseSeries.Rows.Count))
                    : [bar];
            }
        }

        if (window is null)
        {
            return;
        }

        var closeAt = bar.OpenTime + OneMinute;
        foreach (var timeframe in Aggregated)
        {
            var step = timeframe.ToDuration();
            if (closeAt.ToUnixTimeMilliseconds() % (long)step.TotalMilliseconds != 0
                || !_series.TryGetValue((id, timeframe), out var target))
            {
                continue;
            }

            var built = Build(window, closeAt - step, step);
            if (built is null)
            {
                continue;
            }

            lock (target.Gate)
            {
                if (target.Rows.Count == 0 || target.Rows[^1].OpenTime != built.OpenTime - step)
                {
                    continue;
                }

                if (_aggregationOff)
                {
                    return;
                }

                target.Rows.Add(built);
                target.Aggregated = true;
                target.HoldUntil = default;
                Trim(target, timeframe);
                target.Signal();
            }

            _built.Enqueue(new AggregatedBar(id, timeframe, built));
            while (_built.Count > 200 && _built.TryDequeue(out _))
            {
            }
        }
    }

    /// <summary>True when the stream is about to close this interval's bar, so waiting a moment saves a request.</summary>
    public bool ExpectsStreamBar(string symbol, Timeframe timeframe, DateTimeOffset now)
    {
        if (_aggregationOff
            || (timeframe != Timeframe.OneMinute && !Aggregated.Contains(timeframe))
            || !_series.TryGetValue(Key(symbol, Timeframe.OneMinute), out var baseSeries)
            || !_series.TryGetValue(Key(symbol, timeframe), out var target))
        {
            return false;
        }

        var step = timeframe.ToDuration();
        DateTimeOffset expectedOpen;
        lock (target.Gate)
        {
            if (target.Rows.Count == 0)
            {
                return false;
            }

            expectedOpen = target.Rows[^1].OpenTime + step;
            if (expectedOpen + step > now || now - (expectedOpen + step) > TimeSpan.FromSeconds(5))
            {
                return false;
            }
        }

        lock (baseSeries.Gate)
        {
            var rows = baseSeries.Rows;
            if (rows.Count == 0 || now - baseSeries.StreamedAt > TimeSpan.FromMinutes(2))
            {
                return false;
            }

            var lastMinute = expectedOpen + step - OneMinute;
            if (rows[^1].OpenTime < lastMinute - OneMinute)
            {
                return false;
            }

            return timeframe == Timeframe.OneMinute || rows[0].OpenTime <= expectedOpen;
        }
    }

    public async Task<bool> WaitForChangeAsync(string symbol, Timeframe timeframe, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!_series.TryGetValue(Key(symbol, timeframe), out var series))
        {
            return false;
        }

        Task changed;
        lock (series.Gate)
        {
            changed = series.Changed.Task;
        }

        try
        {
            await changed.WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    /// <summary>Coins whose 1m or stream-built intervals were read since <paramref name="since"/>.</summary>
    public IReadOnlyList<string> StreamCoins(DateTimeOffset since)
    {
        var coins = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ((symbol, timeframe), series) in _series)
        {
            if ((timeframe == Timeframe.OneMinute || Aggregated.Contains(timeframe)) && series.DemandedAt >= since)
            {
                coins.Add(symbol);
            }
        }

        return coins.OrderBy(coin => coin, StringComparer.Ordinal).ToList();
    }

    public bool TryTakeAuditSample(out AggregatedBar sample) => _built.TryDequeue(out sample);

    /// <summary>Stops building bars from 1m data and drops every series that holds them; REST reloads them.</summary>
    public void DisableAggregation(string reason)
    {
        _aggregationOff = true;
        AggregationOffReason = reason;
        foreach (var ((_, timeframe), series) in _series)
        {
            if (timeframe == Timeframe.OneMinute)
            {
                continue;
            }

            lock (series.Gate)
            {
                if (!series.Aggregated)
                {
                    continue;
                }

                series.Rows = [];
                series.Aggregated = false;
                series.FromListing = false;
                series.CoveredFrom = default;
            }
        }

        while (_built.TryDequeue(out _))
        {
        }
    }

    /// <summary>Drops series nobody has read since <paramref name="before"/>, such as delisted coins.</summary>
    public int Evict(DateTimeOffset before, IReadOnlySet<string> streamed)
    {
        var removed = 0;
        foreach (var (key, series) in _series)
        {
            if (series.DemandedAt >= before
                || (key.Timeframe == Timeframe.OneMinute && streamed.Contains(key.Symbol))
                || series.Fetch.CurrentCount == 0)
            {
                continue;
            }

            if (_series.TryRemove(key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    public int SeriesCount => _series.Count;

    internal void Clear()
    {
        _series.Clear();
        while (_built.TryDequeue(out _))
        {
        }

        _aggregationOff = false;
        AggregationOffReason = null;
    }

    internal static MarketCandle? Build(IReadOnlyList<MarketCandle> minutes, DateTimeOffset open, TimeSpan step)
    {
        var count = (int)(step.Ticks / OneMinute.Ticks);
        var first = -1;
        for (var i = minutes.Count - 1; i >= 0; i--)
        {
            if (minutes[i].OpenTime == open)
            {
                first = i;
                break;
            }

            if (minutes[i].OpenTime < open)
            {
                return null;
            }
        }

        if (first < 0 || first + count > minutes.Count)
        {
            return null;
        }

        var head = minutes[first];
        var bar = new MarketCandle
        {
            OpenTime = open,
            CloseTime = open + step - TimeSpan.FromMilliseconds(1),
            Open = head.Open,
            High = head.High,
            Low = head.Low,
            Close = head.Close,
            Volume = 0m,
            TradeCount = 0,
            TakerBuyVolume = 0m,
            IsClosed = true
        };

        for (var i = 0; i < count; i++)
        {
            var minute = minutes[first + i];
            if (minute.OpenTime != open + TimeSpan.FromTicks(OneMinute.Ticks * i))
            {
                return null;
            }

            bar.High = Math.Max(bar.High, minute.High);
            bar.Low = Math.Min(bar.Low, minute.Low);
            bar.Close = minute.Close;
            bar.Volume += minute.Volume;
            bar.TradeCount += minute.TradeCount ?? 0;
            bar.TakerBuyVolume += minute.TakerBuyVolume;
        }

        bar.ExchangeTimestamp = bar.CloseTime;
        return bar;
    }

    private static bool Append(Series series, Timeframe timeframe, MarketCandle bar)
    {
        var rows = series.Rows;
        if (rows.Count > 0)
        {
            var last = rows[^1].OpenTime;
            if (bar.OpenTime < last)
            {
                return false;
            }

            if (bar.OpenTime == last)
            {
                rows[^1] = bar;
                series.Signal();
                return true;
            }

            if (bar.OpenTime != last + timeframe.ToDuration())
            {
                rows.Clear();
                series.FromListing = false;
            }
        }

        rows.Add(bar);
        if (rows.Count == 1)
        {
            series.CoveredFrom = bar.OpenTime;
        }

        series.HoldUntil = default;
        Trim(series, timeframe);
        series.Signal();
        return true;
    }

    private static void Trim(Series series, Timeframe timeframe)
    {
        var floor = timeframe == Timeframe.OneMinute ? BaseRows : 1;
        var keep = series.Want == 0 && timeframe != Timeframe.OneMinute ? MaxRows : Math.Max(series.Want, floor);
        keep = Math.Min(keep, MaxRows);
        if (series.Rows.Count > keep + 64)
        {
            series.Rows.RemoveRange(0, series.Rows.Count - keep);
            series.FromListing = false;
            series.CoveredFrom = series.Rows[0].OpenTime;
        }
    }

    private static bool Current(Series series, Timeframe timeframe, DateTimeOffset now) =>
        series.Rows.Count > 0
        && (now < series.Rows[^1].CloseTime + timeframe.ToDuration() || now < series.HoldUntil);

    private static TimeSpan Slack(Timeframe timeframe, TimeSpan step) =>
        timeframe == Timeframe.OneWeek ? step + TimeSpan.FromDays(1) : step;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    private Series Get(string symbol, Timeframe timeframe) => _series.GetOrAdd(Key(symbol, timeframe), _ => new Series());

    private static (string, Timeframe) Key(string symbol, Timeframe timeframe) => (symbol.ToUpperInvariant(), timeframe);

    private sealed class Series
    {
        public readonly object Gate = new();
        public readonly SemaphoreSlim Fetch = new(1, 1);
        public List<MarketCandle> Rows = [];
        public bool FromListing;
        public bool Aggregated;
        public int Want;
        public DateTimeOffset DemandedAt;
        public DateTimeOffset CoveredFrom;
        public DateTimeOffset EmptyUntil;
        public DateTimeOffset HoldUntil;
        public DateTimeOffset StreamedAt;
        public TaskCompletionSource Changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Signal()
        {
            var done = Changed;
            Changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            done.TrySetResult();
        }
    }
}
