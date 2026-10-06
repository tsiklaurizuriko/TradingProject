using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

/// <summary>
/// Optional USD-M market stream. Final 1m klines of the coins bots read are appended to <see cref="FuturesKlineStore"/>,
/// which builds 3m..4h bars from them, and <c>!miniTicker@arr</c> keeps last prices current.
/// REST stays the fallback: a silent or broken stream only means candles are requested again.
/// </summary>
public sealed class BinanceMarketStreamWorker : BackgroundService
{
    public const int StreamsPerConnection = 200;
    private const int SubscribeBatch = 50;

    public static readonly TimeSpan RotateAfter = TimeSpan.FromHours(23);
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan PlanEvery = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan DemandWindow = TimeSpan.FromHours(2);
    private static readonly TimeSpan PriceDemandWindow = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AuditEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EvictEvery = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan EvictIdle = TimeSpan.FromHours(6);
    private static readonly TimeSpan SilentLimit = TimeSpan.FromMinutes(3);

    private readonly FuturesKlineStore _store;
    private readonly BinancePublicMarketDataClient _rest;
    private readonly ILogger<BinanceMarketStreamWorker> _logger;
    private readonly Uri _url;
    private readonly List<Connection> _connections = [];
    private int _audits;

    public BinanceMarketStreamWorker(
        IHttpClientFactory httpFactory,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        _store = FuturesKlineStore.Shared;
        _rest = new BinancePublicMarketDataClient(httpFactory, loggerFactory.CreateLogger<BinancePublicMarketDataClient>());
        _logger = loggerFactory.CreateLogger<BinanceMarketStreamWorker>();
        _url = new Uri(StreamUrl(configuration));
    }

    /// <summary>Kline and ticker streams moved to <c>/market</c> on 2026-04-23; the old <c>/stream</c> root no longer serves them.</summary>
    public static string StreamUrl(IConfiguration configuration) =>
        configuration["Binance:MarketStream:Url"]
        ?? $"{(configuration["Binance:FuturesWebSocketBaseUrl"] ?? "wss://fstream.binance.com").TrimEnd('/')}/market/stream";

    public static string SubscribeMessage(IReadOnlyList<string> streams, int id) =>
        $"{{\"method\":\"SUBSCRIBE\",\"params\":[{string.Join(',', streams.Select(stream => $"\"{stream}\""))}],\"id\":{id}}}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.LogInformation("Binance market stream enabled at {Url}. Coins are subscribed as bots read them.", _url);
        var lastAudit = DateTimeOffset.UtcNow;
        var lastEvict = DateTimeOffset.UtcNow;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = DateTimeOffset.UtcNow;
                var desired = Desired(now);
                Plan(desired, stoppingToken);

                if (now - lastAudit >= AuditEvery)
                {
                    lastAudit = now;
                    await AuditAsync(stoppingToken);
                }

                if (now - lastEvict >= EvictEvery)
                {
                    lastEvict = now;
                    var streamed = desired
                        .Where(stream => stream.EndsWith("@kline_1m", StringComparison.Ordinal))
                        .Select(stream => stream[..stream.IndexOf('@')].ToUpperInvariant())
                        .ToHashSet(StringComparer.Ordinal);
                    var removed = _store.Evict(now - EvictIdle, streamed);
                    if (removed > 0)
                    {
                        _logger.LogDebug("Dropped {Count} candle series nobody read for {Hours}h", removed, (int)EvictIdle.TotalHours);
                    }
                }

                await Task.Delay(PlanEvery, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
        finally
        {
            foreach (var connection in _connections)
            {
                await connection.StopAsync();
            }
        }
    }

    private HashSet<string> Desired(DateTimeOffset now)
    {
        var desired = _store.StreamCoins(now - DemandWindow)
            .Select(coin => MarketStreamFrames.KlineStream(coin, Timeframe.OneMinute))
            .ToHashSet(StringComparer.Ordinal);
        if (now - FuturesPriceBook.DemandedAt <= PriceDemandWindow)
        {
            desired.Add(MarketStreamFrames.MiniTickerStream);
        }

        return desired;
    }

    private void Plan(HashSet<string> desired, CancellationToken stoppingToken)
    {
        foreach (var connection in _connections)
        {
            connection.Retain(desired);
        }

        var assigned = _connections.SelectMany(connection => connection.Streams).ToHashSet(StringComparer.Ordinal);
        var missing = desired.Where(stream => !assigned.Contains(stream)).OrderBy(stream => stream, StringComparer.Ordinal).ToList();
        foreach (var stream in missing)
        {
            var target = _connections.FirstOrDefault(connection => connection.Count < StreamsPerConnection);
            if (target is null)
            {
                target = new Connection(_connections.Count + 1, _url, _store, _logger);
                _connections.Add(target);
                target.Start(stoppingToken);
            }

            target.Add(stream);
        }
    }

    /// <summary>Compares one stream-built bar with REST. Any difference turns building off; REST then serves those intervals.</summary>
    private async Task AuditAsync(CancellationToken cancellationToken)
    {
        if (!_store.AggregationEnabled || !_store.TryTakeAuditSample(out var sample))
        {
            return;
        }

        var rest = await _rest.FetchClosedBarAsync(sample.Symbol, sample.Timeframe, sample.Candle.OpenTime, cancellationToken);
        if (rest is null)
        {
            return;
        }

        var built = sample.Candle;
        var same = rest.Open == built.Open
                   && rest.High == built.High
                   && rest.Low == built.Low
                   && rest.Close == built.Close
                   && rest.Volume == built.Volume
                   && (rest.TradeCount ?? 0) == (built.TradeCount ?? 0)
                   && rest.CloseTime == built.CloseTime;
        _audits++;
        if (same)
        {
            _logger.LogDebug("Stream-built {Interval} bar for {Coin} at {Open} matches REST ({Audits} checked)", sample.Timeframe.ToBinanceInterval(), sample.Symbol, built.OpenTime, _audits);
            return;
        }

        var reason =
            $"{sample.Symbol} {sample.Timeframe.ToBinanceInterval()} {built.OpenTime:u}: built O{built.Open} H{built.High} L{built.Low} C{built.Close} V{built.Volume}, REST O{rest.Open} H{rest.High} L{rest.Low} C{rest.Close} V{rest.Volume}";
        _store.DisableAggregation(reason);
        _logger.LogWarning("A bar built from the 1m stream differs from Binance REST ({Reason}). Building bars from the stream is off; REST serves those intervals.", reason);
    }

    private sealed class Connection
    {
        private readonly int _number;
        private readonly Uri _url;
        private readonly FuturesKlineStore _store;
        private readonly ILogger _logger;
        private readonly object _sync = new();
        private readonly HashSet<string> _streams = new(StringComparer.Ordinal);
        private readonly SemaphoreSlim _send = new(1, 1);
        private readonly CancellationTokenSource _stop = new();
        private ClientWebSocket? _socket;
        private HashSet<string> _sent = new(StringComparer.Ordinal);
        private int _requestId;
        private Task? _run;

        public Connection(int number, Uri url, FuturesKlineStore store, ILogger logger)
        {
            _number = number;
            _url = url;
            _store = store;
            _logger = logger;
        }

        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _streams.Count;
                }
            }
        }

        public IReadOnlyList<string> Streams
        {
            get
            {
                lock (_sync)
                {
                    return _streams.ToList();
                }
            }
        }

        public void Start(CancellationToken stoppingToken)
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _stop.Token);
            _run = Task.Run(() => RunAsync(linked.Token), CancellationToken.None);
        }

        public void Add(string stream)
        {
            lock (_sync)
            {
                _streams.Add(stream);
            }

            _ = FlushAsync();
        }

        /// <summary>Unwanted streams are dropped at the next reconnect; they only cost bandwidth until then.</summary>
        public void Retain(HashSet<string> desired)
        {
            lock (_sync)
            {
                _streams.RemoveWhere(stream => !desired.Contains(stream));
            }
        }

        public async Task StopAsync()
        {
            await _stop.CancelAsync();
            if (_run is not null)
            {
                try
                {
                    await _run;
                }
                catch
                {
                    // stopping
                }
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            var failures = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                var wait = TimeSpan.FromSeconds(1);
                try
                {
                    await SessionAsync(cancellationToken);
                    failures = 0;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    failures++;
                    wait = BinanceUserDataStreamWorker.Backoff(failures);
                    _logger.LogWarning(
                        "Market stream connection {Number} failed ({Failures} in a row): {Error}. Retrying in {Seconds}s; candles come from REST meanwhile.",
                        _number,
                        failures,
                        ex.Message,
                        (int)wait.TotalSeconds);
                }

                try
                {
                    await Task.Delay(wait, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task SessionAsync(CancellationToken cancellationToken)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            session.CancelAfter(RotateAfter);
            using var socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            try
            {
                await socket.ConnectAsync(_url, session.Token);
                lock (_sync)
                {
                    _socket = socket;
                    _sent = new HashSet<string>(StringComparer.Ordinal);
                }

                _logger.LogInformation("Market stream connection {Number} open with {Count} stream(s).", _number, Count);
                _ = FlushAsync();
                await ReceiveAsync(socket, session.Token);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Market stream connection {Number} rotating.", _number);
            }
            finally
            {
                lock (_sync)
                {
                    _socket = null;
                }

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
            }
        }

        /// <summary>Subscribes streams not yet sent on the open socket, in small batches under Binance's 10 messages/s limit.</summary>
        private async Task FlushAsync()
        {
            if (!await _send.WaitAsync(0))
            {
                return;
            }

            try
            {
                while (true)
                {
                    ClientWebSocket? socket;
                    List<string> batch;
                    lock (_sync)
                    {
                        socket = _socket;
                        batch = _streams.Where(stream => !_sent.Contains(stream)).Take(SubscribeBatch).ToList();
                    }

                    if (socket is not { State: WebSocketState.Open } || batch.Count == 0)
                    {
                        return;
                    }

                    var message = Encoding.UTF8.GetBytes(SubscribeMessage(batch, Interlocked.Increment(ref _requestId)));
                    await socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, _stop.Token);
                    lock (_sync)
                    {
                        if (ReferenceEquals(socket, _socket))
                        {
                            _sent.UnionWith(batch);
                        }
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(250), _stop.Token);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                // the session loop reconnects and subscribes again
            }
            finally
            {
                _send.Release();
            }

            if (HasUnsent())
            {
                _ = FlushAsync();
            }
        }

        private bool HasUnsent()
        {
            lock (_sync)
            {
                return _socket is { State: WebSocketState.Open } && _streams.Any(stream => !_sent.Contains(stream));
            }
        }

        private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
        {
            var buffer = new byte[64 * 1024];
            using var frame = new MemoryStream();
            var lastData = DateTimeOffset.UtcNow;
            while (socket.State == WebSocketState.Open)
            {
                using var silent = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                silent.CancelAfter(SilentLimit);
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, silent.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new InvalidOperationException($"no data for {(int)(DateTimeOffset.UtcNow - lastData).TotalSeconds}s");
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Binance closed market stream connection {Number}: {Status} {Reason}", _number, result.CloseStatus, result.CloseStatusDescription);
                    return;
                }

                frame.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                lastData = DateTimeOffset.UtcNow;
                var bytes = new ReadOnlySpan<byte>(frame.GetBuffer(), 0, (int)frame.Length);
                Dispatch(bytes);
                frame.SetLength(0);
            }
        }

        private void Dispatch(ReadOnlySpan<byte> frame)
        {
            if (MarketStreamFrames.TryParseClosedKline(frame, out var symbol, out var timeframe, out var candle))
            {
                if (timeframe == Timeframe.OneMinute)
                {
                    _store.AppendStreamBar(symbol, candle);
                }

                return;
            }

            if (MarketStreamFrames.IsMiniTicker(frame))
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var (coin, price) in MarketStreamFrames.ParseMiniTickers(frame))
                {
                    FuturesPriceBook.Set(coin, price, now);
                }
            }
        }
    }
}
