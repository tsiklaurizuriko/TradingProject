using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

/// <summary>Latest top-trader ranking shared by every top-trader contrarian bot. Bots mark demand; the worker idles without it.</summary>
public sealed class TopTraderRankBook
{
    private readonly object _gate = new();
    private DateTimeOffset _wantedUntil;
    private TopTraderRanking? _latest;

    public void Want(DateTimeOffset now)
    {
        lock (_gate)
        {
            _wantedUntil = now.AddHours(2);
        }
    }

    public bool Wanted(DateTimeOffset now)
    {
        lock (_gate)
        {
            return now < _wantedUntil;
        }
    }

    public TopTraderRanking? Latest
    {
        get
        {
            lock (_gate)
            {
                return _latest;
            }
        }
    }

    public void Publish(TopTraderRanking ranking)
    {
        lock (_gate)
        {
            _latest = ranking;
        }
    }
}

/// <summary>
/// Keeps ~30 days of hourly top-trader position ratios for coins with median daily volume ≥ $5M and publishes the
/// 00:00 UTC ranking from the 23:00 row, the last print strictly before the close. Public endpoints only. No orders are sent from here.
/// </summary>
public sealed class TopTraderRankWorker : BackgroundService
{
    private static readonly TimeSpan Fallback = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan GiveUp = TimeSpan.FromMinutes(50);
    private readonly IFuturesUniverseCatalog _catalog;
    private readonly IPublicMarketDataClient _market;
    private readonly TopTraderRankBook _book;
    private readonly ILogger<TopTraderRankWorker> _logger;
    private readonly ConcurrentDictionary<string, List<(DateTimeOffset Time, decimal Value)>> _series = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string> _universe = [];
    private DateTime _universeDay;
    private DateTimeOffset _lastFetchHour;

    public TopTraderRankWorker(
        IFuturesUniverseCatalog catalog,
        IPublicMarketDataClient market,
        TopTraderRankBook book,
        ILogger<TopTraderRankWorker> logger)
    {
        _catalog = catalog;
        _market = market;
        _book = book;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_book.Wanted(DateTimeOffset.UtcNow))
                {
                    await TickAsync(DateTimeOffset.UtcNow, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Top-trader ranking refresh failed. Open positions are unchanged.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task TickAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (_universeDay != now.UtcDateTime.Date || _universe.Count == 0)
        {
            _universe = await LoadUniverseAsync(cancellationToken);
            _universeDay = now.UtcDateTime.Date;
            _logger.LogInformation("Top-trader universe: {Count} coins with median daily volume ≥ $5M.", _universe.Count);
        }

        var decision = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var pending = _book.Latest?.At != decision && now - decision < GiveUp;
        var hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        if (hour != _lastFetchHour || pending)
        {
            await RefreshSeriesAsync(now, cancellationToken);
            _lastFetchHour = hour;
        }

        if (!pending)
        {
            return;
        }

        var withSeries = _universe.Where(_series.ContainsKey).ToList();
        if (withSeries.Count == 0)
        {
            return;
        }

        var dataAt = decision.AddHours(-1);
        var covered = withSeries.Count(c => _series[c].Any(row => row.Time == dataAt));
        if (covered < withSeries.Count * 0.8 && now - decision < Fallback)
        {
            return;
        }

        var z = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var coin in withSeries)
        {
            z[coin] = ObservationStrategies.TopTraderZ(_series[coin], dataAt);
        }

        var ranking = ObservationStrategies.RankTopTrader(decision, dataAt, z);
        _book.Publish(ranking);
        _logger.LogInformation(
            "Top-trader ranking {At:u} from prints at {DataAt:u}: {Coins} coins, longs {Longs}, shorts {Shorts}. No orders were sent.",
            decision, dataAt, ranking.Coins, string.Join(",", ranking.Longs.Order()), string.Join(",", ranking.Shorts.Order()));
    }

    private async Task<IReadOnlyList<string>> LoadUniverseAsync(CancellationToken cancellationToken)
    {
        var contracts = await _catalog.GetDiscoveredAsync(cancellationToken);
        var names = contracts
            .Select(row => row.Symbol)
            .Where(CrossSectionMath.AcceptedName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var eligible = new ConcurrentBag<string>();
        await Parallel.ForEachAsync(
            names,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (symbol, token) =>
            {
                try
                {
                    var days = await _market.GetClosedKlinesAsync(symbol, Timeframe.OneDay, ObservationStrategies.UniverseDays + 2, token);
                    if (MedianDailyQuote(days, DateTimeOffset.UtcNow) >= ObservationStrategies.UniverseMinMedianQuoteVolume)
                    {
                        eligible.Add(symbol);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Daily klines skipped for {Symbol}", symbol);
                }
            });
        return eligible.Order(StringComparer.Ordinal).ToList();
    }

    /// <summary>Median of base volume × close over the closed daily bars of the 30 days before today; 0 when fewer than 20 days.</summary>
    public static decimal MedianDailyQuote(IReadOnlyList<Domain.Market.MarketCandle> days, DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
        var values = days
            .Where(d => d.OpenTime < today && d.OpenTime >= today.AddDays(-ObservationStrategies.UniverseDays))
            .Select(d => d.Volume * d.Close)
            .OrderBy(v => v)
            .ToList();
        if (values.Count < ObservationStrategies.UniverseMinDays)
        {
            return 0m;
        }

        return values.Count % 2 == 1 ? values[values.Count / 2] : (values[values.Count / 2 - 1] + values[values.Count / 2]) / 2m;
    }

    private async Task RefreshSeriesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var stale in _series.Keys.Where(k => !_universe.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            _series.TryRemove(stale, out _);
        }

        await Parallel.ForEachAsync(
            _universe,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (symbol, token) =>
            {
                try
                {
                    var known = _series.TryGetValue(symbol, out var rows) ? rows : null;
                    var start = known is { Count: > 0 } ? known[^1].Time.AddMilliseconds(1) : now.AddDays(-30);
                    var fresh = await _market.GetTopTraderPositionRatioHistoryAsync(symbol, Timeframe.OneHour, start, now, token);
                    var merged = (known ?? [])
                        .Concat(fresh.Select(f => (f.Time, f.Value)))
                        .Where(r => r.Time >= now.AddDays(-31))
                        .GroupBy(r => r.Time)
                        .Select(g => g.Last())
                        .OrderBy(r => r.Time)
                        .ToList();
                    if (merged.Count > 0)
                    {
                        _series[symbol] = merged;
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Top-trader ratio skipped for {Symbol}", symbol);
                }
            });
    }
}
