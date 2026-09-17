using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TradingPlatform.Api.Hubs;

[AllowAnonymous]
public sealed class TradingHub : Hub
{
    public const string Route = "/hubs/trading";

    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, "system-health");
        await base.OnConnectedAsync();
    }

    public Task SubscribeMarket(string symbol) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"market:{symbol.ToUpperInvariant()}");

    public Task UnsubscribeMarket(string symbol) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"market:{symbol.ToUpperInvariant()}");

    public Task SubscribeBot(Guid botId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"bot:{botId:D}");

    public Task UnsubscribeBot(Guid botId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"bot:{botId:D}");
}
