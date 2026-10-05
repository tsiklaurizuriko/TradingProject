using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Binance;
using Xunit;

namespace TradingPlatform.UnitTests;

public sealed class UserDataStreamTests
{
    [Fact]
    public void Order_trade_update_is_read_with_fill_and_commission()
    {
        var frame = UserDataEvents.Parse("""
            {"e":"ORDER_TRADE_UPDATE","E":1759600000000,"T":1759600000000,
             "o":{"s":"BTCUSDT","c":"slABC","S":"SELL","o":"MARKET","ot":"STOP_MARKET","x":"TRADE","X":"FILLED",
                  "i":8886774,"l":"0.010","z":"0.010","L":"49000.1","ap":"49000.1","n":"0.196","N":"USDT","t":123,"R":true}}
            """);

        frame.Kind.Should().Be(UserDataEventKind.OrderUpdate);
        frame.EventTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1759600000000));
        var order = frame.Order!;
        order.Symbol.Should().Be("BTCUSDT");
        order.ClientOrderId.Should().Be("slABC");
        order.ExchangeOrderId.Should().Be("8886774");
        order.OrderType.Should().Be("STOP_MARKET");
        order.Status.Should().Be("FILLED");
        order.CumulativeQuantity.Should().Be(0.010m);
        order.LastFilledPrice.Should().Be(49000.1m);
        order.Commission.Should().Be(0.196m);
        order.CommissionAsset.Should().Be("USDT");
        order.TradeId.Should().Be("123");
        order.ReduceOnly.Should().BeTrue();
    }

    [Fact]
    public void Account_update_lists_position_changes()
    {
        var frame = UserDataEvents.Parse("""
            {"e":"ACCOUNT_UPDATE","E":1,"a":{"m":"ORDER","B":[],"P":[{"s":"ETHUSDT","pa":"-1.5","ep":"3000","mt":"isolated","ps":"BOTH"},{"pa":"1"}]}}
            """);

        frame.Kind.Should().Be(UserDataEventKind.AccountUpdate);
        frame.Positions.Should().ContainSingle().Which.Should().Be(new UserDataPositionChange("ETHUSDT", -1.5m, 3000m));
    }

    [Theory]
    [InlineData("""{"e":"listenKeyExpired","E":1}""", UserDataEventKind.ListenKeyExpired)]
    [InlineData("""{"e":"MARGIN_CALL","E":1}""", UserDataEventKind.MarginCall)]
    [InlineData("""{"e":"ALGO_UPDATE","E":1}""", UserDataEventKind.AlgoUpdate)]
    [InlineData("""{"stream":"x","data":{"e":"listenKeyExpired"}}""", UserDataEventKind.ListenKeyExpired)]
    [InlineData("""{"e":"TRADE_LITE","E":1}""", UserDataEventKind.Ignored)]
    [InlineData("""{"e":"ORDER_TRADE_UPDATE"}""", UserDataEventKind.Ignored)]
    [InlineData("""not json""", UserDataEventKind.Ignored)]
    [InlineData("""[1,2]""", UserDataEventKind.Ignored)]
    public void Frames_map_to_a_kind_and_never_throw(string json, UserDataEventKind expected)
    {
        UserDataEvents.Parse(json).Kind.Should().Be(expected);
    }

    [Fact]
    public async Task Exchange_events_wake_the_engine_and_an_expired_key_reconnects()
    {
        var signal = new ExchangeEventSignal();
        var now = DateTimeOffset.UtcNow;

        BinanceUserDataStreamWorker.Handle(new UserDataEvent(UserDataEventKind.Ignored, now), signal, now).Should().BeTrue();
        (await signal.WaitAsync(TimeSpan.Zero)).Should().BeFalse("an ignored frame does not wake the engine");

        BinanceUserDataStreamWorker.Handle(new UserDataEvent(UserDataEventKind.OrderUpdate, now), signal, now).Should().BeTrue();
        (await signal.WaitAsync(TimeSpan.Zero)).Should().BeTrue();
        signal.Status.LastEventAt.Should().Be(now);

        BinanceUserDataStreamWorker.Handle(new UserDataEvent(UserDataEventKind.ListenKeyExpired, now), signal, now).Should().BeFalse();
        (await signal.WaitAsync(TimeSpan.Zero)).Should().BeTrue("the gap is covered by an immediate REST cycle");
    }

    [Fact]
    public async Task A_burst_of_events_wakes_the_engine_once()
    {
        var signal = new ExchangeEventSignal();
        for (var i = 0; i < 20; i++)
        {
            signal.Raise("fill", DateTimeOffset.UtcNow);
        }

        (await signal.WaitAsync(TimeSpan.Zero)).Should().BeTrue();
        (await signal.WaitAsync(TimeSpan.Zero)).Should().BeFalse();
    }

    [Fact]
    public void Status_keeps_the_last_event_time_across_reconnects()
    {
        var signal = new ExchangeEventSignal();
        var at = DateTimeOffset.UtcNow;
        signal.Raise("fill", at);
        signal.SetStatus(false, "reconnecting", at.AddSeconds(1));

        signal.Status.Should().Be(new UserDataStreamStatus(false, at, at.AddSeconds(1), "reconnecting"));
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 10)]
    [InlineData(4, 40)]
    [InlineData(50, 300)]
    public void Reconnect_backoff_doubles_and_is_capped(int failures, int seconds)
    {
        BinanceUserDataStreamWorker.Backoff(failures).Should().Be(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public async Task Listen_key_calls_send_the_api_key_only()
    {
        var handler = new RecordingHandler("""{"listenKey":"lk-1"}""");
        var client = new BinanceSignedRestClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") },
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://fapi.example/") }),
            NullLogger<BinanceSignedRestClient>.Instance,
            new ConfigurationBuilder().Build());

        var key = await client.CreateFuturesListenKeyAsync("public-key", CancellationToken.None);
        await client.KeepAliveFuturesListenKeyAsync("public-key", CancellationToken.None);
        await client.CloseFuturesListenKeyAsync("public-key", CancellationToken.None);

        key.Should().Be("lk-1");
        handler.Requests.Select(r => r.Method).Should().Equal(HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete);
        handler.Requests.Should().OnlyContain(r =>
            r.Uri == "https://fapi.example/fapi/v1/listenKey"
            && r.ApiKey == "public-key");
    }

    [Fact]
    public async Task Missing_listen_key_is_an_exchange_error_not_an_empty_stream()
    {
        var handler = new RecordingHandler("{}");
        var client = new BinanceSignedRestClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") },
            new SingleClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://fapi.example/") }),
            NullLogger<BinanceSignedRestClient>.Instance,
            new ConfigurationBuilder().Build());

        var act = () => client.CreateFuturesListenKeyAsync("public-key", CancellationToken.None);

        await act.Should().ThrowAsync<Domain.Errors.DomainException>();
    }

    private sealed record Seen(HttpMethod Method, string Uri, string? ApiKey);

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-MBX-APIKEY", out var values) ? values.Single() : null));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
