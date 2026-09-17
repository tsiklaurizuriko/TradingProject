using Microsoft.AspNetCore.SignalR;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Api.Hubs;

public sealed class SignalRTradingRealtimePublisher : ITradingRealtimePublisher
{
    private readonly IHubContext<TradingHub> _hub;
    private readonly IServiceScopeFactory _scopes;

    public SignalRTradingRealtimePublisher(IHubContext<TradingHub> hub, IServiceScopeFactory scopes)
    {
        _hub = hub;
        _scopes = scopes;
    }

    public Task PublishTickerAsync(string symbol, decimal price, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync("ticker", new { symbol, price, timestamp }, cancellationToken);

    public async Task PublishOverviewAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _scopes.CreateScope();
        var query = scope.ServiceProvider.GetRequiredService<ITradingQueryService>();
        var overview = await query.GetOverviewAsync(cancellationToken);
        await _hub.Clients.All.SendAsync("overview", overview, cancellationToken);
    }

    public Task PublishBotAsync(Guid botId, string status, CancellationToken cancellationToken = default) =>
        _hub.Clients.All.SendAsync("botStatus", new { botId, status }, cancellationToken);
}
