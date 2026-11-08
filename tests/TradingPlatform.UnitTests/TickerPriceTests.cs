using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Binance;
using Xunit;

namespace TradingPlatform.UnitTests;

[Collection(BinanceWeightGateCollection.Name)]
public sealed class TickerPriceTests
{
    [Fact]
    public void Empty_ticker_is_unpriced_and_a_normal_ticker_keeps_its_price()
    {
        using var empty = JsonDocument.Parse("{}");
        BinancePublicMarketDataClient.ReadTickerPrice(empty.RootElement, out _).Should().Be(TickerPriceRead.Unpriced);

        using var priced = JsonDocument.Parse("""{"symbol":"BTCUSDT","price":"76035.50","time":1}""");
        BinancePublicMarketDataClient.ReadTickerPrice(priced.RootElement, out var price).Should().Be(TickerPriceRead.Priced);
        price.Should().Be(76035.50m);

        using var rejected = JsonDocument.Parse("""{"code":-1001,"msg":"Internal error"}""");
        BinancePublicMarketDataClient.ReadTickerPrice(rejected.RootElement, out _).Should().Be(TickerPriceRead.Unavailable);
    }

    [Fact]
    public async Task Empty_ticker_does_not_throw_a_missing_key_and_is_not_requested_again()
    {
        BinancePublicWeightGate.Reset();
        FuturesPriceBook.Clear();
        BinancePublicMarketDataClient.ClearUnpriced();
        var handler = new EmptyTicker();
        var client = new BinancePublicMarketDataClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://fapi.example/") },
            NullLogger<BinancePublicMarketDataClient>.Instance,
            new FuturesKlineStore());
        try
        {
            var act = () => client.GetLastPriceAsync("MITOUSDT");
            var missed = await act.Should().ThrowAsync<SymbolPriceUnavailableException>();
            missed.Which.Symbol.Should().Be("MITOUSDT");
            await act.Should().ThrowAsync<SymbolPriceUnavailableException>();
            handler.Calls.Should().Be(1);
        }
        finally
        {
            BinancePublicWeightGate.Reset();
            FuturesPriceBook.Clear();
            BinancePublicMarketDataClient.ClearUnpriced();
        }
    }

    private sealed class EmptyTicker : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }
}
