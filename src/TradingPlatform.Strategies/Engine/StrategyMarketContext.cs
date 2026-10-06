using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

/// <summary>Raw extra inputs a strategy needs besides its own candles. Null or empty means unavailable.</summary>
public sealed record StrategyMarketInputs(
    IReadOnlyList<MarketCandle>? HigherTimeframe,
    IReadOnlyList<TimedValue>? OpenInterest,
    IReadOnlyList<TimedValue>? Funding)
{
    public static readonly StrategyMarketInputs None = new(null, null, null);
}

/// <summary>
/// One way to load and align higher-timeframe candles, open interest and funding for a strategy.
/// Live bots and API backtests both call this, so a signal on the same candles is the same signal.
/// Every value is causal: bar i only sees observations stamped at or before its close.
/// </summary>
public static class StrategyMarketContext
{
    /// <summary>Canonical templates read a completed 1h regime (EMA200).</summary>
    public static readonly Timeframe HigherTimeframe = Timeframe.OneHour;
    public const int HigherTimeframeWarmupBars = 260;

    /// <summary>Impulse Catch reads a 30-day trend from completed daily candles. Everything else reads 1h.</summary>
    public static Timeframe HigherTimeframeFor(string? templateKey) =>
        StrategyTemplateKeys.CanonicalId(templateKey) == StrategyTemplateKeys.ImpulseCatch ? Timeframe.OneDay : HigherTimeframe;

    public static int HigherTimeframeWarmupFor(string? templateKey) =>
        StrategyTemplateKeys.CanonicalId(templateKey) == StrategyTemplateKeys.ImpulseCatch
            ? RefactoredStrategyEvaluator.Pump.DailyWarmupBars
            : HigherTimeframeWarmupBars;

    public static bool NeedsHigherTimeframe(string? templateKey) =>
        StrategyTemplateKeys.CanonicalId(templateKey) is StrategyTemplateKeys.ImpulseCatch
            or StrategyTemplateKeys.BinHv45
            or StrategyTemplateKeys.ClucMay72018
            or StrategyTemplateKeys.CombinedBinHCluc
            or StrategyTemplateKeys.FlowZone
            or StrategyTemplateKeys.EmaRsiTrend
        || StrategyTemplateKeys.RequiredDatasets(templateKey).Contains("CompletedHtf");

    public static bool NeedsOpenInterest(string? templateKey) =>
        StrategyTemplateKeys.CanonicalId(templateKey) is StrategyTemplateKeys.FlowZone or StrategyTemplateKeys.SqueezeWatch
        || StrategyTemplateKeys.RequiredDatasets(templateKey).Contains("OpenInterest");

    public static bool NeedsFunding(string? templateKey) =>
        StrategyTemplateKeys.CanonicalId(templateKey) is StrategyTemplateKeys.SqueezeWatch
        || StrategyTemplateKeys.RequiredDatasets(templateKey).Contains("Funding");

    public static bool NeedsAny(string? templateKey) =>
        NeedsHigherTimeframe(templateKey) || NeedsOpenInterest(templateKey) || NeedsFunding(templateKey);

    public static async Task<StrategyMarketInputs> LoadAsync(
        IPublicMarketDataClient market,
        string? templateKey,
        string symbol,
        Timeframe timeframe,
        IReadOnlyList<MarketCandle> candles,
        CancellationToken cancellationToken = default)
    {
        if (candles.Count == 0 || !NeedsAny(templateKey))
        {
            return StrategyMarketInputs.None;
        }

        var first = candles[0].OpenTime;
        var last = candles[^1].CloseTime;
        IReadOnlyList<MarketCandle>? htf = null;
        IReadOnlyList<TimedValue>? oi = null;
        IReadOnlyList<TimedValue>? funding = null;
        if (NeedsHigherTimeframe(templateKey))
        {
            var frame = HigherTimeframeFor(templateKey);
            var start = first - frame.ToDuration() * HigherTimeframeWarmupFor(templateKey);
            var bars = (int)Math.Ceiling((last - start) / frame.ToDuration()) + 2;
            htf = await market.GetClosedKlinesRangeAsync(symbol, frame, start, last, bars, cancellationToken);
        }

        if (NeedsOpenInterest(templateKey))
        {
            oi = await market.GetOpenInterestHistoryAsync(symbol, timeframe, first, last, cancellationToken);
        }

        if (NeedsFunding(templateKey))
        {
            funding = await market.GetFundingHistoryAsync(symbol, first - TimeSpan.FromDays(1), last, cancellationToken);
        }

        return new StrategyMarketInputs(htf, oi, funding);
    }

    public static CausalIndicatorCache? HigherTimeframeCache(StrategyMarketInputs inputs)
    {
        if (inputs.HigherTimeframe is not { Count: > 0 } rows)
        {
            return null;
        }

        var closed = rows.Where(row => row.IsClosed).OrderBy(row => row.OpenTime).ToList();
        return closed.Count == 0 ? null : new CausalIndicatorCache(closed);
    }

    /// <summary>Open interest and funding aligned one-to-one with <paramref name="candles"/>.</summary>
    public static StrategyFuturesSeries Futures(IReadOnlyList<MarketCandle> candles, StrategyMarketInputs inputs) => new()
    {
        OpenInterest = Align(inputs.OpenInterest, candles),
        FundingRate = Align(inputs.Funding, candles)
    };

    private static IReadOnlyList<decimal?>? Align(IReadOnlyList<TimedValue>? rows, IReadOnlyList<MarketCandle> candles) =>
        rows is { Count: > 0 }
            ? AlignedMarketSeries.Align(rows.Select(row => (row.Time, row.Value)).ToList(), candles)
            : null;
}
