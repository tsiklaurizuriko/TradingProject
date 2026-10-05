using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Execution.Shadow;

namespace TradingPlatform.Trading.Shadow;

/// <summary>
/// Plays the exchange's matching engine for the shadow account: triggers stops and takes on real mark prices and settles
/// funding. A fill wakes the bot engine, as the Binance user-data stream does on Live.
/// </summary>
public sealed class ShadowExchangeWorker : BackgroundService
{
    private readonly ShadowExchange _exchange;
    private readonly IShadowPriceFeed _prices;
    private readonly IExchangeEventSignal _events;
    private readonly IClock _clock;
    private readonly ILogger<ShadowExchangeWorker> _logger;

    public ShadowExchangeWorker(
        ShadowExchange exchange,
        IShadowPriceFeed prices,
        IExchangeEventSignal events,
        IClock clock,
        ILogger<ShadowExchangeWorker> logger)
    {
        _exchange = exchange;
        _prices = prices;
        _events = events;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var every = TimeSpan.FromSeconds(Math.Max(1, _exchange.Options.TickSeconds));
        _events.SetStatus(true, "Shadow venue: fills are simulated in process.", _clock.UtcNow);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Shadow exchange tick failed. Stops are checked again on the next tick.");
            }

            try
            {
                await Task.Delay(every, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> TickAsync(CancellationToken cancellationToken)
    {
        var symbols = _exchange.Symbols();
        if (symbols.Count == 0)
        {
            return 0;
        }

        var premium = await _prices.GetPremiumAsync(cancellationToken);
        var books = new Dictionary<string, FuturesBookTicker>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (await _prices.GetBookAsync(symbol, cancellationToken) is { } book)
            {
                books[symbol] = book;
            }
        }

        var fills = _exchange.Tick(
            premium.ToDictionary(pair => pair.Key, pair => pair.Value.MarkPrice, StringComparer.OrdinalIgnoreCase),
            premium.ToDictionary(pair => pair.Key, pair => pair.Value.LastFundingRate, StringComparer.OrdinalIgnoreCase),
            symbol => books.GetValueOrDefault(symbol),
            _clock.UtcNow);
        if (fills > 0)
        {
            _events.Raise("shadow fill", _clock.UtcNow);
        }

        return fills;
    }
}
