using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Binance;

/// <summary>
/// Binance USD-M user-data stream. Order, algo and account events wake the bot engine so a stop fill or
/// liquidation is handled within seconds instead of at the next poll. REST stays the source of truth:
/// nothing here books a fill or changes a position, and a dead stream falls back to polling.
/// Runs only on the host that holds the bot-engine lease.
/// </summary>
public sealed class BinanceUserDataStreamWorker : BackgroundService
{
    public static readonly TimeSpan KeepAliveEvery = TimeSpan.FromMinutes(30);

    /// <summary>Binance closes a stream connection at 24h; rotating earlier avoids a surprise drop.</summary>
    public static readonly TimeSpan RotateAfter = TimeSpan.FromHours(23);

    private readonly IServiceScopeFactory _scopes;
    private readonly IExchangeEventSignal _events;
    private readonly ILogger<BinanceUserDataStreamWorker> _logger;
    private readonly string _socketBase;

    public BinanceUserDataStreamWorker(
        IServiceScopeFactory scopes,
        IExchangeEventSignal events,
        IConfiguration configuration,
        ILogger<BinanceUserDataStreamWorker> logger)
    {
        _scopes = scopes;
        _events = events;
        _logger = logger;
        _socketBase = (configuration["Binance:FuturesWebSocketBaseUrl"] ?? "wss://fstream.binance.com").TrimEnd('/');
    }

    /// <summary>
    /// Since 2026-04-23 the listen key is a query parameter on <c>/private/ws</c> and the events must be named;
    /// the old <c>/ws/{key}</c> form connects but delivers nothing.
    /// </summary>
    public const string Events =
        "ORDER_TRADE_UPDATE/ACCOUNT_UPDATE/MARGIN_CALL/ALGO_ORDER_UPDATE/CONDITIONAL_ORDER_TRIGGER_REJECT/listenKeyExpired";

    public static Uri StreamUri(string socketBase, string listenKey) =>
        new($"{socketBase.TrimEnd('/')}/private/ws?listenKey={Uri.EscapeDataString(listenKey)}&events={Events}");

    public static TimeSpan Backoff(int failures) =>
        TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Clamp(failures - 1, 0, 6))));

    /// <summary>What one frame means for the engine. Returns false when the session must reconnect.</summary>
    public static bool Handle(UserDataEvent frame, IExchangeEventSignal events, DateTimeOffset now)
    {
        switch (frame.Kind)
        {
            case UserDataEventKind.ListenKeyExpired:
                events.Raise("listen key expired", now);
                return false;
            case UserDataEventKind.OrderUpdate:
            case UserDataEventKind.AlgoUpdate:
            case UserDataEventKind.AccountUpdate:
            case UserDataEventKind.MarginCall:
                events.Raise(frame.Kind.ToString(), now);
                return true;
            default:
                return true;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            var wait = TimeSpan.FromSeconds(30);
            try
            {
                using var scope = _scopes.CreateScope();
                if (!await scope.ServiceProvider.GetRequiredService<IWorkerLease>().HoldAsync(WorkerLeaseNames.BotEngine, stoppingToken))
                {
                    _events.SetStatus(false, "Standby: another host runs the bot engine.", DateTimeOffset.UtcNow);
                }
                else if (await ApiKeyAsync(scope.ServiceProvider, stoppingToken) is not { } apiKey)
                {
                    _events.SetStatus(false, "No live API key saved. Polling only.", DateTimeOffset.UtcNow);
                    wait = TimeSpan.FromSeconds(60);
                }
                else
                {
                    var rest = scope.ServiceProvider.GetRequiredService<BinanceSignedRestClient>();
                    await RunSessionAsync(rest, apiKey, stoppingToken);
                    failures = 0;
                    wait = TimeSpan.FromSeconds(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                wait = Backoff(failures);
                _logger.LogWarning("User-data stream failed ({Failures} in a row): {Error}. Retrying in {Seconds}s; REST polling continues.", failures, ex.Message, (int)wait.TotalSeconds);
                _events.SetStatus(false, $"Stream down: {ex.Message}. Polling only.", DateTimeOffset.UtcNow);
            }

            try
            {
                await Task.Delay(wait, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _events.SetStatus(false, "User-data stream stopped.", DateTimeOffset.UtcNow);
    }

    private static async Task<string?> ApiKeyAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var admin = await services.GetRequiredService<ITradingStore>().GetFirstAdminAsync(cancellationToken);
        var credentials = services.GetRequiredService<IExchangeCredentialStore>();
        var account = await credentials.GetLiveAccountAsync(admin.Id, cancellationToken);
        return account is null ? null : (await credentials.GetAsync(account.Id, cancellationToken))?.ApiKey;
    }

    private async Task RunSessionAsync(BinanceSignedRestClient rest, string apiKey, CancellationToken stoppingToken)
    {
        var listenKey = await rest.CreateFuturesListenKeyAsync(apiKey, stoppingToken);
        using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        session.CancelAfter(RotateAfter);
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        Task? keepAlive = null;
        try
        {
            await socket.ConnectAsync(StreamUri(_socketBase, listenKey), session.Token);
            _events.SetStatus(true, "User-data stream connected.", DateTimeOffset.UtcNow);
            _events.Raise("stream connected", DateTimeOffset.UtcNow);
            _logger.LogInformation("Binance user-data stream connected.");

            keepAlive = KeepAliveAsync(rest, apiKey, session);
            await ReceiveAsync(socket, session.Token);
        }
        catch (OperationCanceledException) when (session.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("User-data stream session ended (rotation or keepalive failure). Reconnecting.");
        }
        finally
        {
            await session.CancelAsync();
            if (keepAlive is not null)
            {
                await keepAlive;
            }

            _events.SetStatus(false, "User-data stream reconnecting. Polling only.", DateTimeOffset.UtcNow);
            _events.Raise("stream gap", DateTimeOffset.UtcNow);
            await CloseQuietlyAsync(socket, rest, apiKey);
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        using var frame = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _logger.LogInformation("Binance closed the user-data stream: {Status} {Reason}", result.CloseStatus, result.CloseStatusDescription);
                return;
            }

            frame.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(frame.GetBuffer(), 0, (int)frame.Length);
            frame.SetLength(0);
            var parsed = UserDataEvents.Parse(text);
            if (parsed.Order is { } order)
            {
                _logger.LogInformation(
                    "User-data {Status} {Type} {Coin} {ClientOrderId} filled {Filled}",
                    order.Status,
                    order.OrderType,
                    order.Symbol,
                    order.ClientOrderId,
                    order.CumulativeQuantity);
            }

            if (!Handle(parsed, _events, DateTimeOffset.UtcNow))
            {
                return;
            }
        }
    }

    private async Task KeepAliveAsync(BinanceSignedRestClient rest, string apiKey, CancellationTokenSource session)
    {
        try
        {
            while (!session.IsCancellationRequested)
            {
                await Task.Delay(KeepAliveEvery, session.Token);
                await rest.KeepAliveFuturesListenKeyAsync(apiKey, session.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // session ended
        }
        catch (Exception ex)
        {
            _logger.LogWarning("User-data listen key keepalive failed: {Error}. Reconnecting.", ex.Message);
            await session.CancelAsync();
        }
    }

    private static async Task CloseQuietlyAsync(ClientWebSocket socket, BinanceSignedRestClient rest, string apiKey)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "rotate", timeout.Token);
            }
        }
        catch
        {
            // best effort
        }

        try
        {
            await rest.CloseFuturesListenKeyAsync(apiKey, timeout.Token);
        }
        catch
        {
            // the key expires on its own after 60 minutes
        }
    }
}
