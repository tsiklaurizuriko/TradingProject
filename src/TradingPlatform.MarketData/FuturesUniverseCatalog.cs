using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.MarketData;

public sealed class FuturesUniverseCatalog : IFuturesUniverseCatalog
{
    private readonly IPublicMarketDataClient _market;
    private readonly TradingOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<FuturesUniverseCatalog> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<DiscoveredFuturesContract> _memory = [];
    private DateTimeOffset? _refreshedAt;

    public FuturesUniverseCatalog(
        IPublicMarketDataClient market,
        IOptions<TradingOptions> options,
        IHostEnvironment environment,
        ILogger<FuturesUniverseCatalog> logger)
    {
        _market = market;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
        _memory = ReadDisk();
        if (_memory.Count > 0)
        {
            _refreshedAt = DateTimeOffset.UtcNow;
        }
    }

    public DateTimeOffset? LastRefreshedAt => _refreshedAt;

    public async Task<IReadOnlyList<DiscoveredFuturesContract>> GetDiscoveredAsync(CancellationToken cancellationToken = default)
    {
        if (_memory.Count > 0 && _refreshedAt is { } at && DateTimeOffset.UtcNow - at < RefreshWindow())
        {
            return _memory;
        }

        return await RefreshAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DiscoveredFuturesContract>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_memory.Count > 0 && _refreshedAt is { } at && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(30))
            {
                return _memory;
            }

            var contracts = await _market.DiscoverUsdtPerpetualsAsync(cancellationToken);
            if (contracts.Count == 0)
            {
                if (_memory.Count > 0)
                {
                    _logger.LogWarning("Binance discovery returned empty; keeping {Count} cached USDT perpetuals", _memory.Count);
                    return _memory;
                }

                return [];
            }

            _memory = contracts;
            _refreshedAt = DateTimeOffset.UtcNow;
            WriteDisk(contracts);
            _logger.LogInformation("Cached {Count} USD-M USDT perpetual contracts. New listings appear on the next refresh without a deploy.", contracts.Count);
            return _memory;
        }
        finally
        {
            _gate.Release();
        }
    }

    private TimeSpan RefreshWindow() =>
        TimeSpan.FromMinutes(Math.Max(1, _options.UniverseRefreshMinutes));

    private string CachePath()
    {
        var relative = string.IsNullOrWhiteSpace(_options.UniverseCachePath)
            ? "data/futures-universe.json"
            : _options.UniverseCachePath;
        return Path.IsPathRooted(relative)
            ? relative
            : Path.Combine(_environment.ContentRootPath, relative);
    }

    private IReadOnlyList<DiscoveredFuturesContract> ReadDisk()
    {
        try
        {
            var path = CachePath();
            if (!File.Exists(path))
            {
                return [];
            }

            var json = File.ReadAllText(path);
            var payload = JsonSerializer.Deserialize<UniverseCacheFile>(json);
            return payload?.Contracts ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read futures universe cache");
            return [];
        }
    }

    private void WriteDisk(IReadOnlyList<DiscoveredFuturesContract> contracts)
    {
        try
        {
            var path = CachePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(new UniverseCacheFile(DateTimeOffset.UtcNow, contracts));
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write futures universe cache");
        }
    }

    private sealed record UniverseCacheFile(DateTimeOffset FetchedAt, IReadOnlyList<DiscoveredFuturesContract> Contracts);
}
