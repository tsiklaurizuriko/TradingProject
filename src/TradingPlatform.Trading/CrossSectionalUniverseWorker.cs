using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Research;

namespace TradingPlatform.Trading;

/// <summary>
/// Fills the closed 15-minute cache off the bot cycle so a cross-sectional rank does not stall other bots.
/// </summary>
public sealed class CrossSectionalUniverseWorker : BackgroundService
{
    private readonly IFuturesUniverseCatalog _catalog;
    private readonly IPublicMarketDataClient _market;
    private readonly IMarketDataCache _cache;
    private readonly IOptionsMonitor<TradingOptions> _options;
    private readonly ILogger<CrossSectionalUniverseWorker> _logger;

    public CrossSectionalUniverseWorker(
        IFuturesUniverseCatalog catalog,
        IPublicMarketDataClient market,
        IMarketDataCache cache,
        IOptionsMonitor<TradingOptions> options,
        ILogger<CrossSectionalUniverseWorker> logger)
    {
        _catalog = catalog;
        _market = market;
        _cache = cache;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var flags = _options.CurrentValue.CrossSectionalReversal ?? new CrossSectionalReversalOptions();
            if (!flags.Enabled)
            {
                await Delay(TimeSpan.FromMinutes(1), stoppingToken);
                continue;
            }

            try
            {
                var written = await RefreshAsync(flags, stoppingToken);
                _logger.LogInformation("Cross-sectional 15m cache refreshed for {Count} coins. No orders were sent.", written);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cross-sectional 15m cache refresh failed. Open positions are unchanged.");
            }

            await Delay(TimeSpan.FromMinutes(Math.Max(1, flags.RebalanceIntervalMinutes)), stoppingToken);
        }
    }

    private async Task<int> RefreshAsync(CrossSectionalReversalOptions flags, CancellationToken cancellationToken)
    {
        var contracts = await _catalog.GetDiscoveredAsync(cancellationToken);
        var names = contracts
            .Select(row => row.Symbol)
            .Where(CrossSectionMath.AcceptedName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!names.Contains("BTCUSDT", StringComparer.OrdinalIgnoreCase))
        {
            names.Insert(0, "BTCUSDT");
        }

        var limit = Math.Max(flags.HistoryBarsRequired + 8, 104);
        var written = 0;
        await Parallel.ForEachAsync(
            names,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            async (symbol, token) =>
            {
                try
                {
                    var candles = await _market.GetClosedKlinesAsync(symbol, Timeframe.FifteenMinutes, limit, token);
                    if (candles.Count == 0)
                    {
                        return;
                    }

                    _cache.SetKlines(symbol, Timeframe.FifteenMinutes, candles);
                    Interlocked.Increment(ref written);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Closed 15m klines skipped for {Symbol}", symbol);
                }
            });
        return written;
    }

    private static async Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
