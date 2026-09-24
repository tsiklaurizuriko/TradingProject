using TradingPlatform.Domain.Market;
using TradingPlatform.Strategies.Indicators;
using TradingPlatform.Strategies.PriceAction;

namespace TradingPlatform.Research;

/// <summary>
/// Pre-registered causal bins for edge discovery. These thresholds are not fit to PnL.
/// ATR percentile in this codebase is a fraction in [0, 1], not a 0–100 score.
/// </summary>
public static class EdgeFeatureCatalog
{
    public const string Version = "edge-discovery-v1";
    public const decimal VolLowMax = 0.33m;
    public const decimal VolHighMin = 0.67m;
    public const decimal AdxRangeMax = 20m;
    public const decimal AdxTrendMin = 25m;
    public const decimal ContractionAtrRatio = 0.5m;
    public const int CompressionBars = 8;
    public const int ReturnBars = 20;
    public const decimal AltReturnGap = 0.01m;
    public const int MinimumInterestingTrades = 80;
    public const int MinimumGroupTrades = 25;
    public const int MinimumSymbolTrades = 20;
    public const int MinimumBlockTrades = 15;
    public const int MinimumIsTrades = 30;
    public const int MinimumValidationTrades = 20;

    public const string InterestingRule =
        "Frozen before aggregation. A condition is INTERESTING only when the in-set has n>=80, profit factor>1, net>0, the out-set has n>=80 and profit factor<1, at least two independent family groups each have in-set n>=25 and net>0, at least two of BTC ETH BNB each have in-set n>=20 and net>0, at least three chronological blocks each have in-set n>=15 and net>0, and both IS (n>=30) and VALIDATION (n>=20) have in-set net>0. OOS is not an input to this label. INTERESTING is still EXPLORATORY, never VALIDATED_FOR_PAPER. A cell that misses the rule is NOISE or INSUFFICIENT_EVIDENCE.";

    public static readonly string[] IndependentGroups =
    [
        "FROZEN_TREND",
        "FROZEN_MEAN",
        "PA_SWEEP",
        "PA_FAILED",
        "PA_CONTINUATION",
        "PA_STRUCTURE",
        "FUNDING"
    ];
}

public sealed record EdgeObservation(
    string Group,
    string Family,
    string CandidateId,
    string Symbol,
    string Timeframe,
    string Side,
    string Phase,
    int Block,
    DateTimeOffset ClosedAt,
    decimal Pnl,
    decimal Fees,
    decimal Slippage,
    bool Ready,
    bool? VolLow,
    bool? VolNormal,
    bool? VolHigh,
    bool? AdxRange,
    bool? AdxTrend,
    bool? Expansion,
    bool? Contraction,
    bool? CompressionRelease,
    bool? HtfAligned,
    bool? HtfOpposed,
    bool? BtcAligned,
    bool? BtcOpposed,
    bool? BtcVolHigh,
    bool? BtcVolLow,
    bool? AltStronger,
    bool? AltWeaker,
    bool? Asia,
    bool? Europe,
    bool? Us,
    DayOfWeek? Dow);

public static class EdgeFeatureBuilder
{
    public static EdgeObservation Build(
        string group,
        string family,
        string candidateId,
        string symbol,
        string timeframe,
        string side,
        string phase,
        int block,
        DateTimeOffset closedAt,
        decimal pnl,
        decimal fees,
        decimal slippage,
        CausalIndicatorCache entry,
        CausalIndicatorCache hourly,
        CausalIndicatorCache btcHourly,
        int index)
    {
        var candles = entry.Candles;
        var atr = entry.Atr(14);
        var percentile = entry.AtrPercentile(14, 50);
        var adx = entry.Adx(14);
        var ready = index >= 0 && index < candles.Count
            && percentile[index] is not null
            && adx[index] is not null
            && atr[index] is not null;
        var vol = ready ? VolBucket(percentile[index]!.Value) : null;
        var trend = ready ? TrendBucket(adx[index]!.Value) : null;
        var bar = ready ? BarBucket(candles, atr, index) : null;
        var release = ready ? CompressionRelease(candles, atr, index) : (bool?)null;
        var bias = ready ? FinalFiveSignals.BiasAt(hourly, hourly.PriceAction(), candles[index].CloseTime) : 0;
        var longSide = string.Equals(side, "LONG", StringComparison.OrdinalIgnoreCase);
        bool? aligned = ready && bias != 0 ? bias > 0 == longSide : null;
        bool? opposed = aligned is null ? null : !aligned;
        var btcIndex = ready ? LastClosed(btcHourly, candles[index].CloseTime) : -1;
        var btcPercentile = btcHourly.AtrPercentile(14, 50);
        var btcAdx = btcHourly.Adx(14);
        bool? btcVolHigh = null;
        bool? btcVolLow = null;
        if (btcIndex >= 0 && btcPercentile[btcIndex] is { } btcVol)
        {
            btcVolHigh = btcVol >= EdgeFeatureCatalog.VolHighMin;
            btcVolLow = btcVol < EdgeFeatureCatalog.VolLowMax;
        }

        var entryReturn = ready ? ReturnOver(candles, index, EdgeFeatureCatalog.ReturnBars) : null;
        var btcReturn = btcIndex >= 0 ? ReturnOver(btcHourly.Candles, btcIndex, EdgeFeatureCatalog.ReturnBars) : null;
        bool? btcAligned = null;
        bool? btcOpposed = null;
        if (btcIndex >= 0 && btcAdx[btcIndex] is { } btcTrend && btcTrend >= EdgeFeatureCatalog.AdxTrendMin && btcReturn is { } btcMove && btcMove != 0m)
        {
            var btcUp = btcMove > 0m;
            btcAligned = btcUp == longSide;
            btcOpposed = !btcAligned;
        }

        bool? altStronger = null;
        bool? altWeaker = null;
        if (ready
            && !string.Equals(symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase)
            && entryReturn is { } local
            && SameClockBtcReturn(btcHourly, candles, index) is { } clockBtc)
        {
            altStronger = local > clockBtc + EdgeFeatureCatalog.AltReturnGap;
            altWeaker = local < clockBtc - EdgeFeatureCatalog.AltReturnGap;
        }

        var hour = candles[index].CloseTime.UtcDateTime.Hour;
        var dow = candles[index].CloseTime.UtcDateTime.DayOfWeek;
        return new EdgeObservation(
            group, family, candidateId, symbol, timeframe, longSide ? "LONG" : "SHORT", phase, block, closedAt, pnl, fees, slippage, ready,
            vol is null ? null : vol == "LOW",
            vol is null ? null : vol == "NORMAL",
            vol is null ? null : vol == "HIGH",
            trend is null ? null : trend == "RANGE",
            trend is null ? null : trend == "TREND",
            bar is null ? null : bar == "EXPANSION",
            bar is null ? null : bar == "CONTRACTION",
            release,
            aligned, opposed,
            btcAligned, btcOpposed,
            btcVolHigh, btcVolLow,
            altStronger, altWeaker,
            ready ? hour < 7 : null,
            ready ? hour >= 7 && hour < 15 : null,
            ready ? hour >= 15 : null,
            ready ? dow : null);
    }

    public static int LastClosed(CausalIndicatorCache series, DateTimeOffset close)
    {
        var candles = series.Candles;
        var lo = 0;
        var hi = candles.Count - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (candles[mid].CloseTime <= close)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return found;
    }

    public static string? VolBucket(decimal percentile) =>
        percentile < EdgeFeatureCatalog.VolLowMax ? "LOW"
        : percentile >= EdgeFeatureCatalog.VolHighMin ? "HIGH"
        : "NORMAL";

    public static string? TrendBucket(decimal adx) =>
        adx < EdgeFeatureCatalog.AdxRangeMax ? "RANGE"
        : adx >= EdgeFeatureCatalog.AdxTrendMin ? "TREND"
        : "TRANSITION";

    private static string? BarBucket(IReadOnlyList<MarketCandle> candles, IReadOnlyList<decimal?> atr, int index)
    {
        if (atr[index] is not { } volatility || volatility <= 0m)
        {
            return null;
        }

        var range = candles[index].High - candles[index].Low;
        if (range >= volatility)
        {
            return "EXPANSION";
        }

        if (range < volatility * EdgeFeatureCatalog.ContractionAtrRatio)
        {
            return "CONTRACTION";
        }

        return "NEUTRAL";
    }

    private static bool? CompressionRelease(IReadOnlyList<MarketCandle> candles, IReadOnlyList<decimal?> atr, int index)
    {
        if (index < EdgeFeatureCatalog.CompressionBars || atr[index] is not { } current || current <= 0m)
        {
            return null;
        }

        if (candles[index].High - candles[index].Low < current)
        {
            return false;
        }

        for (var j = index - EdgeFeatureCatalog.CompressionBars; j < index; j++)
        {
            if (atr[j] is not { } prior || prior <= 0m || candles[j].High - candles[j].Low >= prior)
            {
                return false;
            }
        }

        return true;
    }

    private static decimal? ReturnOver(IReadOnlyList<MarketCandle> candles, int index, int bars)
    {
        var prior = index - bars;
        if (prior < 0 || candles[prior].Close <= 0m)
        {
            return null;
        }

        return (candles[index].Close - candles[prior].Close) / candles[prior].Close;
    }

    private static decimal? SameClockBtcReturn(CausalIndicatorCache btcHourly, IReadOnlyList<MarketCandle> entry, int index)
    {
        var bars = EdgeFeatureCatalog.ReturnBars;
        if (index < bars)
        {
            return null;
        }

        var start = LastClosed(btcHourly, entry[index - bars].CloseTime);
        var end = LastClosed(btcHourly, entry[index].CloseTime);
        if (start < 0 || end < 0 || btcHourly.Candles[start].Close <= 0m)
        {
            return null;
        }

        return (btcHourly.Candles[end].Close - btcHourly.Candles[start].Close) / btcHourly.Candles[start].Close;
    }
}
