using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.MarketData;

public sealed class UniverseRefreshWorker : BackgroundService
{
    private readonly IFuturesUniverseCatalog _catalog;
    private readonly IMarketScanner _scanner;
    private readonly TradingOptions _options;
    private readonly ILogger<UniverseRefreshWorker> _logger;

    public UniverseRefreshWorker(
        IFuturesUniverseCatalog catalog,
        IMarketScanner scanner,
        IOptions<TradingOptions> options,
        ILogger<UniverseRefreshWorker> logger)
    {
        _catalog = catalog;
        _scanner = scanner;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.UniverseRefreshMinutes));
        _logger.LogInformation("USD-M universe refresh every {Minutes} minutes. New perpetuals are picked up without a deploy.", interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var discovered = await _catalog.RefreshAsync(stoppingToken);
                var scanned = await _scanner.ScanAsync(stoppingToken);
                _logger.LogInformation("Universe refresh: {Discovered} discovered, {Scanned} scanned", discovered.Count, scanned.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Universe refresh failed; keeping last cache");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
