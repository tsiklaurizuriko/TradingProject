using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Binance;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using Xunit;

namespace TradingPlatform.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BinanceWeightGateCollection
{
    public const string Name = "Binance weight gate";
}

public sealed class FuturesKlineStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Latest_rows_are_served_until_the_next_bar_closes()
    {
        var store = new FuturesKlineStore();
        var bars = Bars(T0, Timeframe.FifteenMinutes, 10);
        var now = bars[^1].CloseTime.AddSeconds(5);
        store.Merge("BTCUSDT", Timeframe.FifteenMinutes, bars, bars[0].OpenTime, fromListing: false, now);

        store.TryLatest("btcusdt", Timeframe.FifteenMinutes, 4, now.AddMinutes(14), out var hit).Should().BeTrue();
        hit.Select(bar => bar.OpenTime).Should().Equal(bars.TakeLast(4).Select(bar => bar.OpenTime));
        store.TryLatest("BTCUSDT", Timeframe.FifteenMinutes, 11, now, out _).Should().BeFalse("ten rows cannot answer eleven");
        store.TryLatest("BTCUSDT", Timeframe.FifteenMinutes, 4, now.AddMinutes(15), out _).Should().BeFalse("the next bar has closed");
        store.Cover("BTCUSDT", Timeframe.FifteenMinutes, now.AddMinutes(31)).Missing.Should().Be(2);
    }

    [Fact]
    public void Overlapping_tail_joins_the_series_and_a_gap_replaces_it()
    {
        var store = new FuturesKlineStore();
        var bars = Bars(T0, Timeframe.OneHour, 10);
        store.Merge("ETHUSDT", Timeframe.OneHour, bars, bars[0].OpenTime, false, T0);

        var tail = Bars(bars[^1].OpenTime, Timeframe.OneHour, 3);
        var now = tail[^1].CloseTime.AddSeconds(1);
        store.Merge("ETHUSDT", Timeframe.OneHour, tail, tail[0].OpenTime, false, now);
        store.TryLatest("ETHUSDT", Timeframe.OneHour, 12, now, out var joined).Should().BeTrue();
        joined.Select(bar => bar.OpenTime).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        joined[^1].OpenTime.Should().Be(tail[^1].OpenTime);

        var older = Bars(T0.AddDays(-5), Timeframe.OneHour, 3);
        store.Merge("ETHUSDT", Timeframe.OneHour, older, older[0].OpenTime, false, now);
        store.Tail("ETHUSDT", Timeframe.OneHour, 100).Should().HaveCount(12, "an older block that does not touch the series is ignored");

        var later = Bars(T0.AddDays(2), Timeframe.OneHour, 5);
        var laterNow = later[^1].CloseTime.AddSeconds(1);
        store.Merge("ETHUSDT", Timeframe.OneHour, later, later[0].OpenTime, false, laterNow);
        store.Tail("ETHUSDT", Timeframe.OneHour, 100).Select(bar => bar.OpenTime).Should().Equal(later.Select(bar => bar.OpenTime));
    }

    [Fact]
    public void Range_is_served_only_when_the_series_reaches_back_to_the_start()
    {
        var store = new FuturesKlineStore();
        var bars = Bars(T0, Timeframe.OneHour, 30);
        var now = bars[^1].CloseTime.AddMinutes(1);
        var unalignedStart = bars[0].OpenTime.AddMinutes(-20);
        store.Merge("SOLUSDT", Timeframe.OneHour, bars, unalignedStart, false, now);

        store.TryRange("SOLUSDT", Timeframe.OneHour, unalignedStart, now, 1000, now, out var rows).Should().BeTrue();
        rows.Should().HaveCount(30);
        store.TryRange("SOLUSDT", Timeframe.OneHour, bars[5].OpenTime, bars[9].OpenTime, 1000, now, out rows).Should().BeTrue();
        rows.Select(bar => bar.OpenTime).Should().Equal(bars.Skip(5).Take(5).Select(bar => bar.OpenTime));
        store.TryRange("SOLUSDT", Timeframe.OneHour, unalignedStart.AddHours(-2), now, 1000, now, out _).Should().BeFalse();
    }

    [Fact]
    public void Empty_listing_is_remembered_for_a_while()
    {
        var store = new FuturesKlineStore();
        store.MarkEmpty("NEWUSDT", Timeframe.OneHour, T0.AddMinutes(5));

        store.TryLatest("NEWUSDT", Timeframe.OneHour, 50, T0, out var rows).Should().BeTrue();
        rows.Should().BeEmpty();
        store.TryLatest("NEWUSDT", Timeframe.OneHour, 50, T0.AddMinutes(6), out _).Should().BeFalse();
    }

    [Fact]
    public void Trimming_keeps_what_readers_asked_for()
    {
        var store = new FuturesKlineStore();
        store.Demand("BTCUSDT", Timeframe.FifteenMinutes, 119, T0);
        var bars = Bars(T0, Timeframe.FifteenMinutes, 400);
        var now = bars[^1].CloseTime.AddSeconds(1);
        store.Merge("BTCUSDT", Timeframe.FifteenMinutes, bars, bars[0].OpenTime, false, now);

        var kept = store.Tail("BTCUSDT", Timeframe.FifteenMinutes, 1000);
        kept.Should().HaveCount(119);
        store.TryLatest("BTCUSDT", Timeframe.FifteenMinutes, 119, now, out _).Should().BeTrue();
    }

    [Fact]
    public void Built_bar_sums_the_minutes_exactly()
    {
        var minutes = Bars(T0, Timeframe.OneMinute, 15);
        var bar = FuturesKlineStore.Build(minutes, T0, TimeSpan.FromMinutes(15))!;

        bar.OpenTime.Should().Be(T0);
        bar.CloseTime.Should().Be(T0.AddMinutes(15).AddMilliseconds(-1));
        bar.Open.Should().Be(minutes[0].Open);
        bar.Close.Should().Be(minutes[^1].Close);
        bar.High.Should().Be(minutes.Max(m => m.High));
        bar.Low.Should().Be(minutes.Min(m => m.Low));
        bar.Volume.Should().Be(minutes.Sum(m => m.Volume));
        bar.TradeCount.Should().Be(minutes.Sum(m => m.TradeCount));
        bar.TakerBuyVolume.Should().Be(minutes.Sum(m => m.TakerBuyVolume));

        FuturesKlineStore.Build(minutes.Where((_, i) => i != 7).ToList(), T0, TimeSpan.FromMinutes(15)).Should().BeNull("a missing minute");
        FuturesKlineStore.Build(minutes.Take(14).ToList(), T0, TimeSpan.FromMinutes(15)).Should().BeNull("an unfinished window");
    }

    [Fact]
    public void Stream_minutes_close_a_demanded_contiguous_interval()
    {
        var store = new FuturesKlineStore();
        var quarter = Bars(T0.AddHours(-2), Timeframe.FifteenMinutes, 8);
        store.Merge("BTCUSDT", Timeframe.FifteenMinutes, quarter, quarter[0].OpenTime, false, T0.AddSeconds(1));
        var hours = Bars(T0.AddHours(-3), Timeframe.OneHour, 2);
        store.Merge("BTCUSDT", Timeframe.OneHour, hours, hours[0].OpenTime, false, T0.AddSeconds(1));

        var minutes = Bars(T0, Timeframe.OneMinute, 15);
        foreach (var minute in minutes)
        {
            store.AppendStreamBar("btcusdt", minute);
        }

        var at = T0.AddMinutes(15).AddMilliseconds(300);
        store.TryLatest("BTCUSDT", Timeframe.FifteenMinutes, 9, at, out var rows).Should().BeTrue();
        rows[^1].OpenTime.Should().Be(T0);
        rows[^1].Volume.Should().Be(minutes.Sum(m => m.Volume));
        store.Tail("BTCUSDT", Timeframe.OneHour, 10).Should().HaveCount(2, "no hour closed in this window");
        store.Tail("BTCUSDT", Timeframe.FiveMinutes, 10).Should().BeEmpty("nobody reads 5m bars for this coin");

        store.TryTakeAuditSample(out var sample).Should().BeTrue();
        sample.Timeframe.Should().Be(Timeframe.FifteenMinutes);

        store.DisableAggregation("test");
        store.Tail("BTCUSDT", Timeframe.FifteenMinutes, 10).Should().BeEmpty("series holding built bars are reloaded from REST");
        store.Tail("BTCUSDT", Timeframe.OneHour, 10).Should().HaveCount(2);
        store.AggregationEnabled.Should().BeFalse();
    }

    [Fact]
    public void A_skipped_stream_minute_restarts_the_minute_series()
    {
        var store = new FuturesKlineStore();
        var minutes = Bars(T0, Timeframe.OneMinute, 5);
        store.AppendStreamBar("BTCUSDT", minutes[0]);
        store.AppendStreamBar("BTCUSDT", minutes[1]);
        store.AppendStreamBar("BTCUSDT", minutes[3]);
        store.AppendStreamBar("BTCUSDT", minutes[2]);

        store.Tail("BTCUSDT", Timeframe.OneMinute, 10).Select(bar => bar.OpenTime).Should().Equal(minutes[3].OpenTime);
    }

    [Fact]
    public void Stream_coins_are_the_ones_read_on_stream_built_intervals()
    {
        var store = new FuturesKlineStore();
        store.Demand("BTCUSDT", Timeframe.FifteenMinutes, 119, T0);
        store.Demand("ETHUSDT", Timeframe.OneDay, 30, T0);
        store.Demand("SOLUSDT", Timeframe.OneMinute, 119, T0.AddHours(-5));

        store.StreamCoins(T0.AddHours(-2)).Should().Equal("BTCUSDT");
    }

    internal static List<MarketCandle> Bars(DateTimeOffset firstOpen, Timeframe timeframe, int count)
    {
        var step = timeframe.ToDuration();
        return Enumerable.Range(0, count)
            .Select(i =>
            {
                var open = firstOpen + step * i;
                var basePrice = 100m + i;
                return new MarketCandle
                {
                    OpenTime = open,
                    CloseTime = open + step - TimeSpan.FromMilliseconds(1),
                    Open = basePrice,
                    High = basePrice + 2.5m + (i % 3),
                    Low = basePrice - 1.25m - (i % 2),
                    Close = basePrice + 0.5m,
                    Volume = 10.001m + i,
                    TradeCount = 7 + i,
                    TakerBuyVolume = 4.5m + i,
                    IsClosed = true,
                    ExchangeTimestamp = open + step - TimeSpan.FromMilliseconds(1)
                };
            })
            .ToList();
    }
}

[Collection(BinanceWeightGateCollection.Name)]
public sealed class SharedKlineClientTests
{
    [Fact]
    public async Task Repeated_reads_within_a_bar_cost_one_request()
    {
        var (client, handler, _) = Client();

        var first = await client.GetClosedKlinesAsync("BTCUSDT", Timeframe.FifteenMinutes, 120);
        var second = await client.GetClosedKlinesAsync("BTCUSDT", Timeframe.FifteenMinutes, 120);
        var smaller = await client.GetClosedKlinesAsync("BTCUSDT", Timeframe.FifteenMinutes, 104);

        handler.Requests.Should().ContainSingle();
        first.Should().HaveCount(119, "the forming bar is dropped, as with a direct REST call");
        second.Select(bar => bar.OpenTime).Should().Equal(first.Select(bar => bar.OpenTime));
        smaller.Should().HaveCount(103);
        smaller[^1].OpenTime.Should().Be(first[^1].OpenTime);
        first[^1].CloseTime.Should().BeBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Concurrent_reads_share_one_request()
    {
        var (client, handler, _) = Client(delay: TimeSpan.FromMilliseconds(150));

        var reads = Enumerable.Range(0, 8).Select(_ => client.GetClosedKlinesAsync("ETHUSDT", Timeframe.OneHour, 120)).ToList();
        var results = await Task.WhenAll(reads);

        handler.Requests.Should().ContainSingle();
        results.Should().OnlyContain(rows => rows.Count == 119);
    }

    [Fact]
    public async Task A_behind_series_asks_only_for_the_missing_tail()
    {
        var (client, handler, store) = Client();
        var step = Timeframe.FifteenMinutes.ToDuration();
        var formingOpen = FakeKlines.FormingOpen(DateTimeOffset.UtcNow, step);
        var stale = FuturesKlineStoreTests.Bars(formingOpen - step * 123, Timeframe.FifteenMinutes, 120);
        store.Merge("BTCUSDT", Timeframe.FifteenMinutes, stale, stale[0].OpenTime, false, stale[^1].CloseTime);

        var rows = await client.GetClosedKlinesAsync("BTCUSDT", Timeframe.FifteenMinutes, 120);

        handler.Requests.Should().ContainSingle().Which.Should().Contain("limit=5");
        rows.Should().HaveCount(119);
        rows[^1].OpenTime.Should().Be(formingOpen - step);
        rows.Select(bar => bar.OpenTime).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        KlineSeries.Inspect(rows, Timeframe.FifteenMinutes).MissingBars.Should().Be(0);
    }

    [Fact]
    public async Task Live_range_is_loaded_once_and_then_served_from_memory()
    {
        var (client, handler, _) = Client();
        var now = DateTimeOffset.UtcNow;
        var start = now.AddHours(-260).AddMinutes(-7);

        var first = await client.GetClosedKlinesRangeAsync("SOLUSDT", Timeframe.OneHour, start, now, 1000);
        var second = await client.GetClosedKlinesRangeAsync("SOLUSDT", Timeframe.OneHour, start, now.AddSeconds(1), 1000);

        handler.Requests.Should().ContainSingle();
        first.Should().NotBeEmpty();
        second.Select(bar => bar.OpenTime).Should().Equal(first.Select(bar => bar.OpenTime));
    }

    [Fact]
    public async Task Historical_range_goes_to_rest_and_is_not_kept()
    {
        var (client, handler, store) = Client();
        var end = DateTimeOffset.UtcNow.AddDays(-30);

        await client.GetClosedKlinesRangeAsync("SOLUSDT", Timeframe.OneHour, end.AddDays(-2), end, 1000);
        await client.GetClosedKlinesRangeAsync("SOLUSDT", Timeframe.OneHour, end.AddDays(-2), end, 1000);

        handler.Requests.Should().HaveCount(2);
        store.SeriesCount.Should().Be(0);
    }

    [Fact]
    public async Task Background_reads_wait_for_weight_instead_of_being_skipped()
    {
        BinancePublicWeightGate.Reset();
        try
        {
            var (client, handler, _) = Client();
            var foreground = BinancePublicWeight.MinuteBudget - BinancePublicWeight.SignedReserve;
            (await BinancePublicWeightGate.TryAcquireAsync(foreground, TimeSpan.Zero, CancellationToken.None)).Should().BeTrue();

            (await client.GetClosedKlinesAsync("BTCUSDT", Timeframe.OneDay, 30)).Should().BeEmpty("a bot-cycle read is skipped when the budget is full");
            handler.Requests.Should().BeEmpty();

            MarketDataPriority.IsBackground.Should().BeFalse();
            using (MarketDataPriority.Background())
            {
                MarketDataPriority.IsBackground.Should().BeTrue();
                using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                var act = () => client.GetClosedKlinesAsync("ETHUSDT", Timeframe.OneDay, 30, cancel.Token);
                await act.Should().ThrowAsync<OperationCanceledException>("it waits for weight instead of returning nothing");
            }

            MarketDataPriority.IsBackground.Should().BeFalse();
        }
        finally
        {
            BinancePublicWeightGate.Reset();
        }
    }

    private static (BinancePublicMarketDataClient Client, FakeKlines Handler, FuturesKlineStore Store) Client(TimeSpan? delay = null)
    {
        BinancePublicWeightGate.Reset();
        var handler = new FakeKlines(delay ?? TimeSpan.Zero);
        var store = new FuturesKlineStore();
        var client = new BinancePublicMarketDataClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://fapi.example/") },
            NullLogger<BinancePublicMarketDataClient>.Instance,
            store);
        return (client, handler, store);
    }

    /// <summary>Serves Binance-shaped klines for any coin, including the forming bar like the real endpoint.</summary>
    private sealed class FakeKlines(TimeSpan delay) : HttpMessageHandler
    {
        private readonly object _sync = new();

        public List<string> Requests { get; } = [];

        public static DateTimeOffset FormingOpen(DateTimeOffset now, TimeSpan step) =>
            DateTimeOffset.FromUnixTimeMilliseconds(now.ToUnixTimeMilliseconds() / (long)step.TotalMilliseconds * (long)step.TotalMilliseconds);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (_sync)
            {
                Requests.Add(url);
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            var query = request.RequestUri.Query.TrimStart('?').Split('&')
                .Select(part => part.Split('='))
                .ToDictionary(part => part[0], part => part.Length > 1 ? part[1] : "");
            TimeframeExtensions.TryParseInterval(query["interval"], out var timeframe);
            var step = timeframe.ToDuration();
            var limit = query.TryGetValue("limit", out var l) ? int.Parse(l, CultureInfo.InvariantCulture) : 500;
            var forming = FormingOpen(DateTimeOffset.UtcNow, step);
            DateTimeOffset first;
            if (query.TryGetValue("startTime", out var startText))
            {
                var startMs = long.Parse(startText, CultureInfo.InvariantCulture);
                var stepMs = (long)step.TotalMilliseconds;
                first = DateTimeOffset.FromUnixTimeMilliseconds((startMs + stepMs - 1) / stepMs * stepMs);
            }
            else
            {
                first = forming - step * (limit - 1);
            }

            var end = query.TryGetValue("endTime", out var endText)
                ? DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(endText, CultureInfo.InvariantCulture))
                : forming;
            var rows = new List<string>();
            for (var open = first; open <= forming && open <= end && rows.Count < limit; open += step)
            {
                var ms = open.ToUnixTimeMilliseconds();
                var close = ms + (long)step.TotalMilliseconds - 1;
                rows.Add($"[{ms},\"100.0\",\"101.5\",\"99.5\",\"100.7\",\"12.5\",{close},\"1250\",42,\"6.1\",\"610\",\"0\"]");
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[" + string.Join(',', rows) + "]", Encoding.UTF8, "application/json")
            };
        }
    }
}

public sealed class MarketStreamFrameTests
{
    [Fact]
    public void Final_kline_frame_is_read_and_updates_are_rejected_early()
    {
        const string final = """
            {"stream":"btcusdt@kline_1m","data":{"e":"kline","E":1791240060493,"s":"BTCUSDT","k":{"t":1791240000000, "T":1791240059999, "s":"BTCUSDT", "i":"1m", "f":8148430588, "L":8148431450, "o":"85885.40", "c":"85893.90", "h":"85900.00", "l":"85885.40", "v":"30.033", "n":856, "x":true, "q":"2579673.59220", "V":"8.679", "Q":"745444.22690", "B":"0"}}}
            """;

        MarketStreamFrames.TryParseClosedKline(Encoding.UTF8.GetBytes(final), out var symbol, out var timeframe, out var candle).Should().BeTrue();
        symbol.Should().Be("BTCUSDT");
        timeframe.Should().Be(Timeframe.OneMinute);
        candle.OpenTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1791240000000));
        candle.CloseTime.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1791240059999));
        candle.Open.Should().Be(85885.40m);
        candle.High.Should().Be(85900.00m);
        candle.Low.Should().Be(85885.40m);
        candle.Close.Should().Be(85893.90m);
        candle.Volume.Should().Be(30.033m);
        candle.TradeCount.Should().Be(856);
        candle.TakerBuyVolume.Should().Be(8.679m);

        var forming = final.Replace("\"x\":true", "\"x\":false", StringComparison.Ordinal);
        MarketStreamFrames.TryParseClosedKline(Encoding.UTF8.GetBytes(forming), out _, out _, out _).Should().BeFalse();
        MarketStreamFrames.TryParseClosedKline("""{"result":null,"id":1}"""u8, out _, out _, out _).Should().BeFalse();
        MarketStreamFrames.TryParseClosedKline("""{"stream":"a@kline_1m","x":true,"data":"#"""u8, out _, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Mini_ticker_array_gives_last_prices()
    {
        var frame = """
            {"stream":"!miniTicker@arr","data":[{"e":"24hrMiniTicker","E":1,"s":"BTCUSDT","c":"85893.90","o":"1","h":"1","l":"1","v":"1","q":"1"},{"e":"24hrMiniTicker","s":"ETHUSDT","c":"0"}]}
            """u8;

        MarketStreamFrames.IsMiniTicker(frame).Should().BeTrue();
        MarketStreamFrames.ParseMiniTickers(frame).Should().Equal(("BTCUSDT", 85893.90m));
    }

    [Fact]
    public void Subscribe_message_and_stream_names_match_binance()
    {
        MarketStreamFrames.KlineStream("BTCUSDT", Timeframe.OneMinute).Should().Be("btcusdt@kline_1m");
        BinanceMarketStreamWorker.SubscribeMessage(["btcusdt@kline_1m", "!miniTicker@arr"], 7)
            .Should().Be("""{"method":"SUBSCRIBE","params":["btcusdt@kline_1m","!miniTicker@arr"],"id":7}""");
    }

    [Fact]
    public void Market_stream_uses_the_market_route()
    {
        BinanceMarketStreamWorker.StreamUrl(new ConfigurationBuilder().Build())
            .Should().Be("wss://fstream.binance.com/market/stream");
        var testnet = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Binance:FuturesWebSocketBaseUrl"] = "wss://stream.binancefuture.com/" })
            .Build();
        BinanceMarketStreamWorker.StreamUrl(testnet).Should().Be("wss://stream.binancefuture.com/market/stream");
    }

    [Fact]
    public void User_data_stream_puts_the_listen_key_in_the_query_with_named_events()
    {
        var uri = BinanceUserDataStreamWorker.StreamUri("wss://fstream.binance.com/", "abc123");

        uri.AbsolutePath.Should().Be("/private/ws");
        uri.Query.Should().StartWith("?listenKey=abc123&events=");
        foreach (var name in new[] { "ORDER_TRADE_UPDATE", "ACCOUNT_UPDATE", "ALGO_ORDER_UPDATE", "MARGIN_CALL", "listenKeyExpired" })
        {
            uri.Query.Should().Contain(name);
        }
    }

    [Theory]
    [InlineData("""{"e":"ALGO_ORDER_UPDATE","E":1}""")]
    [InlineData("""{"e":"CONDITIONAL_ORDER_TRIGGER_REJECT","E":1}""")]
    public void Algo_events_under_either_name_wake_the_engine(string json)
    {
        UserDataEvents.Parse(json).Kind.Should().Be(UserDataEventKind.AlgoUpdate);
    }
}
