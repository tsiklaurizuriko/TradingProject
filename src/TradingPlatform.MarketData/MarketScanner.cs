using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.MarketData;

public sealed class MarketScanner : IMarketScanner
{
    private readonly IPublicMarketDataClient _market;
    private readonly IFuturesUniverseCatalog _catalog;
    private readonly ScannerOptions _scannerOptions;
    private readonly ILogger<MarketScanner> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MarketScanRow> _current = [];
    private DateTimeOffset _cacheUntil;

    public MarketScanner(
        IPublicMarketDataClient market,
        IFuturesUniverseCatalog catalog,
        IOptions<TradingOptions> options,
        ILogger<MarketScanner> logger)
    {
        _market = market;
        _catalog = catalog;
        _scannerOptions = options.Value.Scanner ?? new ScannerOptions();
        _logger = logger;
    }

    public IReadOnlyList<MarketScanRow> Current => _current;

    public async Task<IReadOnlyList<MarketScanRow>> ScanAsync(CancellationToken cancellationToken = default)
    {
        if (_current.Count > 0 && DateTimeOffset.UtcNow < _cacheUntil)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_current.Count > 0 && DateTimeOffset.UtcNow < _cacheUntil)
            {
                return _current;
            }

            var contracts = await _catalog.GetDiscoveredAsync(cancellationToken);
            var bySymbol = contracts.ToDictionary(c => c.Symbol, StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<RankedUsdtSpotSymbol> stats;
            try
            {
                stats = await _market.GetPaperUniverseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "24h ticker overlay failed; scanning discovery metadata only");
                stats = [];
            }

            var books = new Dictionary<string, FuturesBookTicker>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var row in await _market.GetBookTickersAsync(cancellationToken))
                {
                    books[row.Symbol] = row;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Book ticker overlay failed; spread stays 0");
            }

            var premium = new Dictionary<string, FuturesPremiumIndex>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var row in await _market.GetPremiumIndexAsync(cancellationToken))
                {
                    premium[row.Symbol] = row;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Premium index overlay failed; funding stays 0");
            }

            var statsBySymbol = stats.ToDictionary(s => s.Symbol, StringComparer.OrdinalIgnoreCase);
            var raw = new List<MarketScanRow>(bySymbol.Count);
            foreach (var contract in contracts)
            {
                statsBySymbol.TryGetValue(contract.Symbol, out var ticker);
                books.TryGetValue(contract.Symbol, out var book);
                premium.TryGetValue(contract.Symbol, out var mark);
                var last = ticker?.LastPrice ?? 0m;
                var high = ticker?.HighPrice ?? 0m;
                var low = ticker?.LowPrice ?? 0m;
                var volume = ticker?.QuoteVolume ?? 0m;
                var trades = ticker?.Trades24h ?? 0;
                var change = ticker?.PriceChangePercent ?? 0m;
                var bid = book?.Bid ?? 0m;
                var ask = book?.Ask ?? 0m;
                var spread = MarketScanScoring.SpreadBps(bid, ask);
                var vol = MarketScanScoring.VolatilityPercent(high, low, last);
                var quality = MarketScanScoring.DataQuality(
                    last,
                    volume,
                    trades,
                    contract.TickSize,
                    contract.StepSize,
                    contract.MinQuantity);
                raw.Add(new MarketScanRow(
                    contract,
                    last,
                    volume,
                    change,
                    high,
                    low,
                    trades,
                    bid,
                    ask,
                    spread,
                    mark?.LastFundingRate ?? 0m,
                    mark?.MarkPrice ?? 0m,
                    OpenInterest: 0m,
                    vol,
                    Math.Min(100m, Math.Abs(change)),
                    quality,
                    ScanScore: 0m,
                    ScanRank: 0));
            }

            _current = MarketScanScoring.Rank(raw, _scannerOptions.Weights);
            _cacheUntil = DateTimeOffset.UtcNow.AddMinutes(1);
            _logger.LogInformation(
                "Scanned {Count} discovered USDT perpetuals. Eligibility is applied separately; scan rank is not a trade list.",
                _current.Count);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }
}
