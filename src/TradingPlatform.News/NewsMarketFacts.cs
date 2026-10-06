using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.News;

public sealed record NewsMarketExtras(
    decimal? Bid = null,
    decimal? Ask = null,
    decimal? QuoteVolume24h = null,
    decimal? OpenInterest = null,
    decimal? PreviousOpenInterest = null,
    decimal? FundingRate = null,
    decimal? PreviousFundingRate = null,
    bool RequireBook = false,
    bool RequireLiquidity = false,
    bool RequireDerivatives = false);

public sealed record NewsMarketFacts
{
    public string Symbol { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public DateTimeOffset? PriceAt { get; init; }
    public double? ReturnSinceDetection { get; init; }
    public double? Return1m { get; init; }
    public double? Return5m { get; init; }
    public double? Return15m { get; init; }
    public double? Return1h { get; init; }
    public double? RelativeVolume { get; init; }
    public double? AtrPercent { get; init; }
    public double? TakerImbalance { get; init; }
    public decimal? SpreadPercent { get; init; }
    public decimal? QuoteVolume24h { get; init; }
    public decimal? OpenInterest { get; init; }
    public double? OpenInterestChange { get; init; }
    public decimal? FundingRate { get; init; }
    public decimal? FundingChange { get; init; }
    public double? BtcReturn5m { get; init; }
    public double? BtcReturn15m { get; init; }
    public double? BtcReturn1h { get; init; }
    public double? BtcAtrPercent { get; init; }
    public bool EvaluateBtc { get; init; } = true;
    public bool Stale { get; init; }
    public bool MissingRequired { get; init; }
    public string Detail { get; init; } = string.Empty;

    public static NewsMarketFacts Missing(string symbol) => new() { Symbol = symbol, MissingRequired = true };
}

public static class NewsMarketFactsBuilder
{
    public static NewsMarketFacts FromCandles(
        string symbol,
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>? candles,
        DateTimeOffset decisionTime,
        DateTimeOffset detectedAt,
        NewsOptions options,
        NewsMarketExtras? extras = null,
        NewsMarketFacts? btc = null)
    {
        var book = Closed(candles, decisionTime);
        var executionName = options.Strategy.Timeframes.Execution;
        var flowName = options.Strategy.Timeframes.Flow;
        var trendName = options.Strategy.Timeframes.Trend;
        book.TryGetValue(executionName, out var execution);
        book.TryGetValue(flowName, out var flow);
        book.TryGetValue(trendName, out var trend);
        book.TryGetValue("1m", out var oneMinute);
        execution ??= [];
        var price = execution.Count > 0 ? execution[^1].Close : 0m;
        var priceAt = execution.Count > 0 ? execution[^1].CloseTime : (DateTimeOffset?)null;
        var stale = false;
        if (priceAt is { } at && TimeframeExtensions.TryParseInterval(executionName, out var parsed))
        {
            stale = decisionTime - at > parsed.ToDuration() * 2;
        }

        var spread = SpreadPercent(extras?.Bid, extras?.Ask);
        double? oiChange = null;
        if (extras?.OpenInterest is { } latest && extras.PreviousOpenInterest is { } previous && previous > 0m)
        {
            oiChange = (double)((latest - previous) / previous * 100m);
        }

        decimal? fundingChange = extras?.FundingRate is { } funding && extras.PreviousFundingRate is { } prior
            ? funding - prior
            : null;
        var atr = Last(execution, rows => new CausalIndicatorCache(rows).AtrPercent(14));
        var relative = Last(flow ?? execution, rows => new CausalIndicatorCache(rows).RelativeVolume(20));
        var taker = flow is { Count: > 0 } || execution.Count > 0
            ? TakerFlow.Imbalance((flow ?? execution)[^1])
            : null;
        var missing = price <= 0m
            || atr is null
            || (extras?.RequireBook == true && spread is null)
            || (extras?.RequireLiquidity == true && extras.QuoteVolume24h is null)
            || (extras?.RequireDerivatives == true && (extras.OpenInterest is null || extras.FundingRate is null || oiChange is null));
        var isBtc = string.Equals(symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase);
        return new NewsMarketFacts
        {
            Symbol = symbol,
            Price = price,
            PriceAt = priceAt,
            ReturnSinceDetection = ReturnSince(execution, detectedAt),
            Return1m = oneMinute is { Count: > 0 } ? ReturnOver(oneMinute, decisionTime, TimeSpan.FromMinutes(1)) : null,
            Return5m = ReturnOver(flow ?? execution, decisionTime, TimeSpan.FromMinutes(5)),
            Return15m = ReturnOver(execution, decisionTime, TimeSpan.FromMinutes(15)),
            Return1h = ReturnOver(trend ?? execution, decisionTime, TimeSpan.FromHours(1)),
            RelativeVolume = relative is { } rvol ? (double)rvol : null,
            AtrPercent = atr is { } atrValue ? (double)atrValue : null,
            TakerImbalance = taker is { } imbalance ? (double)imbalance : null,
            SpreadPercent = spread,
            QuoteVolume24h = extras?.QuoteVolume24h,
            OpenInterest = extras?.OpenInterest,
            OpenInterestChange = oiChange,
            FundingRate = extras?.FundingRate,
            FundingChange = fundingChange,
            BtcReturn5m = isBtc ? null : btc?.Return5m,
            BtcReturn15m = isBtc ? null : btc?.Return15m,
            BtcReturn1h = isBtc ? null : btc?.Return1h,
            BtcAtrPercent = isBtc ? null : btc?.AtrPercent,
            EvaluateBtc = !isBtc,
            Stale = stale,
            MissingRequired = missing || execution.Count == 0,
            Detail = "closedThrough=" + decisionTime.ToString("O")
        };
    }

    public static IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> Closed(
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>>? candles,
        DateTimeOffset decisionTime)
    {
        var result = new Dictionary<string, IReadOnlyList<MarketCandle>>(StringComparer.OrdinalIgnoreCase);
        if (candles is null)
        {
            return result;
        }

        foreach (var pair in candles)
        {
            result[pair.Key] = pair.Value.Where(candle => candle.CloseTime <= decisionTime).OrderBy(candle => candle.CloseTime).ToList();
        }

        return result;
    }

    private static double? ReturnSince(IReadOnlyList<MarketCandle> candles, DateTimeOffset detectedAt)
    {
        if (candles.Count == 0 || candles[^1].Close <= 0m)
        {
            return null;
        }

        var then = candles.LastOrDefault(candle => candle.CloseTime <= detectedAt);
        if (then is null || then.Close <= 0m)
        {
            return null;
        }

        return (double)((candles[^1].Close - then.Close) / then.Close * 100m);
    }

    private static double? ReturnOver(IReadOnlyList<MarketCandle> candles, DateTimeOffset decisionTime, TimeSpan span)
    {
        if (candles.Count == 0 || candles[^1].Close <= 0m)
        {
            return null;
        }

        var then = candles.LastOrDefault(candle => candle.CloseTime <= decisionTime - span);
        if (then is null || then.Close <= 0m)
        {
            return null;
        }

        return (double)((candles[^1].Close - then.Close) / then.Close * 100m);
    }

    private static decimal? Last(IReadOnlyList<MarketCandle> candles, Func<IReadOnlyList<MarketCandle>, IReadOnlyList<decimal?>> series)
    {
        if (candles.Count < 20)
        {
            return null;
        }

        return series(candles)[^1];
    }

    public static decimal? SpreadPercent(decimal? bid, decimal? ask)
    {
        if (bid is not { } bidPrice || ask is not { } askPrice || bidPrice <= 0m || askPrice < bidPrice)
        {
            return null;
        }

        var mid = (bidPrice + askPrice) / 2m;
        return mid <= 0m ? null : (askPrice - bidPrice) / mid * 100m;
    }
}
