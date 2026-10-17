using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Signal generation for the six -new strategies. Orders are not sent from here.
/// A flat book emits Buy, Sell, or NoAction. An open book emits Hold or Exit and does not flip.
/// </summary>
public static class ProductionNewEvaluator
{
    public static StrategySignalDetail Evaluate(
        StrategyTemplateParams parameters,
        IReadOnlyList<MarketCandle> candles,
        int index,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        if (index < 1 || index >= candles.Count)
        {
            return No(null, "NO_TRADE: not enough closed candles.");
        }

        var bar = candles[index];
        var key = StrategyTemplateKeys.CanonicalId(parameters.TemplateKey);
        if (!bar.IsClosed)
        {
            return context.HasOpenPosition
                ? Hold(bar, "Unclosed candle is not an entry or an exit.")
                : No(bar, "NO_TRADE: unclosed candle is not a signal.");
        }

        if (!ProductionNewMath.BarUsable(bar))
        {
            return No(bar, "NO_TRADE: candle prices or volume are not usable.");
        }

        if (ProductionNewMath.HasGap(candles, index, parameters.Timeframe))
        {
            return context.HasOpenPosition
                ? Exit(bar, "Missing candle. The open position is flattened at the next modeled price and no new entry is taken.")
                : No(bar, "NO_TRADE: missing candle. The gap is not treated as a normal bar.");
        }

        return key switch
        {
            StrategyTemplateKeys.TrendFollowingNew => Trend(parameters, candles, index, context, cache),
            StrategyTemplateKeys.BreakoutVolumeNew => Breakout(parameters, candles, index, context, cache),
            StrategyTemplateKeys.MarketRegimeNew => Regime(parameters, candles, index, context, cache),
            StrategyTemplateKeys.MomentumRelativeStrengthNew => Momentum(parameters, candles, index, context, cache),
            StrategyTemplateKeys.MeanReversionRsiBollingerNew => MeanReversion(parameters, candles, index, context, cache),
            StrategyTemplateKeys.FundingBasisNew => Funding(parameters, candles, index, context, cache),
            _ => No(bar, "NO_TRADE: unknown -new strategy.")
        };
    }

    private static StrategySignalDetail Trend(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var bar = candles[i];
        var fastSeries = cache.Ema(p.EmaFast);
        var slowSeries = cache.Ema(p.EmaSlow);
        var adxSeries = cache.Adx(p.AdxPeriod);
        var atrSeries = cache.Atr(p.AtrPeriod);
        if (fastSeries[i] is not { } fast
            || slowSeries[i] is not { } slow
            || adxSeries[i] is not { } adx
            || atrSeries[i] is not { } atr
            || atr <= 0m
            || fastSeries[i - 1] is not { } previousFast)
        {
            return No(bar, "NO_TRADE: EMA, ADX, or ATR warm-up is incomplete.");
        }

        var regime = MarketRegimeClassifier.Confirmed(candles, i, cache, p, context);
        var snap = Snapshot(fast, slow, adx, atr, regime);
        var longTrend = fast > slow && bar.Close > fast && adx > p.MinimumAdx;
        var shortTrend = fast < slow && bar.Close < fast && adx > p.MinimumAdx;
        if (p.EmaSlopeFilter)
        {
            longTrend = longTrend && fast > previousFast;
            shortTrend = shortTrend && fast < previousFast;
        }

        if (context.HasOpenPosition)
        {
            return TrendExit(p, candles, i, context, bar, fast, slow, atr, longTrend, shortTrend, snap);
        }

        if (!MarketRegimeClassifier.Allows(regime, StrategyTemplateKeys.TrendFollowingNew))
        {
            return No(bar, $"NO_TRADE: regime {regime} does not permit trend-following-new. ADX measures strength, not direction.", snap);
        }

        if (longTrend && StrategySides.AllowsLong(p.AllowedSide))
        {
            return Entry(StrategyTemplateKeys.TrendFollowingNew, bar, context, true, atr, p, "EMA(fast) is above EMA(slow), close is above EMA(fast), and ADX is above the threshold.", snap);
        }

        if (shortTrend && StrategySides.AllowsShort(p.AllowedSide))
        {
            return Entry(StrategyTemplateKeys.TrendFollowingNew, bar, context, false, atr, p, "EMA(fast) is below EMA(slow), close is below EMA(fast), and ADX is above the threshold.", snap);
        }

        return No(bar, "NO_TRADE: trend-following-new entry conditions are not met.", snap);
    }

    private static StrategySignalDetail TrendExit(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        MarketCandle bar,
        decimal fast,
        decimal slow,
        decimal atr,
        bool longTrend,
        bool shortTrend,
        Dictionary<string, decimal?> snap)
    {
        var longPosition = IsLong(context);
        if (p.OppositeTrendExit && ((longPosition && fast < slow) || (!longPosition && fast > slow)))
        {
            return Exit(bar, "Opposite EMA relationship. The position is closed and not reversed.", snap);
        }

        if (p.EmaTrendBreakExit && ((longPosition && bar.Close < fast) || (!longPosition && bar.Close > fast)))
        {
            return Exit(bar, "Close broke EMA(fast). The position is closed and not reversed.", snap);
        }

        if (HeldTooLong(candles, i, context, p.MaxHoldBars))
        {
            return Exit(bar, "Maximum holding duration reached.", snap);
        }

        var stop = ProtectiveStop(context, longPosition, atr, p.AtrStopMultiplier, p.AtrTrailEnabled, bar.Close);
        if (longPosition && longTrend)
        {
            return Hold(bar, "Long trend remains valid.", stop, snap);
        }

        if (!longPosition && shortTrend)
        {
            return Hold(bar, "Short trend remains valid.", stop, snap);
        }

        return Hold(bar, "Position remains open. Baseline exit is the protective stop or an EMA-order flip.", stop, snap);
    }

    private static StrategySignalDetail Breakout(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var bar = candles[i];
        var lookback = p.DonchianLength;
        var high = ProductionNewMath.PriorHigh(candles, i, lookback);
        var low = ProductionNewMath.PriorLow(candles, i, lookback);
        var rvol = ProductionNewMath.RelativeVolume(candles, i, p.RelativeVolumePeriod);
        var atrSeries = cache.Atr(p.AtrPeriod);
        if (high is not { } rangeHigh || low is not { } rangeLow || rvol is not { } relative || atrSeries[i] is not { } atr || atr <= 0m)
        {
            return No(bar, "NO_TRADE: breakout warm-up, relative volume, or ATR is not usable.");
        }

        var regime = MarketRegimeClassifier.Confirmed(candles, i, cache, p, context);
        var snap = new Dictionary<string, decimal?>
        {
            ["rangeHigh"] = rangeHigh,
            ["rangeLow"] = rangeLow,
            ["rvol"] = relative,
            ["atr"] = atr
        };
        var brokeLong = bar.Close > rangeHigh && relative > p.BreakoutRelativeVolume && !Broke(candles, i - 1, lookback, longSide: true);
        var brokeShort = bar.Close < rangeLow && relative > p.BreakoutRelativeVolume && !Broke(candles, i - 1, lookback, longSide: false);
        if (context.HasOpenPosition)
        {
            if (p.RangeInvalidationExit)
            {
                var backInside = IsLong(context) ? bar.Close < rangeHigh : bar.Close > rangeLow;
                if (backInside)
                {
                    return Exit(bar, "Close returned into the broken range. The position is not reversed.", snap);
                }
            }

            if (HeldTooLong(candles, i, context, p.MaxHoldBars))
            {
                return Exit(bar, "Maximum holding duration reached.", snap);
            }

            return Hold(bar, "Breakout position remains open under the protective stop.", ProtectiveStop(context, IsLong(context), atr, p.AtrStopMultiplier, p.AtrTrailEnabled, bar.Close), snap);
        }

        if (!MarketRegimeClassifier.Allows(regime, StrategyTemplateKeys.BreakoutVolumeNew))
        {
            return No(bar, $"NO_TRADE: regime {regime} does not permit breakout-volume-new.", snap);
        }

        if (brokeLong || brokeShort)
        {
            var longSide = brokeLong;
            var level = longSide ? rangeHigh : rangeLow;
            var extension = longSide ? bar.Close - level : level - bar.Close;
            if (extension > p.PriceDisplacementAtr * atr)
            {
                return No(bar, "NO_TRADE: breakout is too far from the broken level. The entry is not chased.", snap);
            }

            snap["level"] = level;
            return Entry(
                StrategyTemplateKeys.BreakoutVolumeNew,
                bar,
                context,
                longSide,
                atr,
                p,
                longSide
                    ? "Close is above the prior range high and relative volume is above the threshold. The current candle is excluded from the range."
                    : "Close is below the prior range low and relative volume is above the threshold. The current candle is excluded from the range.",
                snap);
        }

        return No(bar, "NO_TRADE: breakout-volume-new entry conditions are not met.", snap);
    }

    private static StrategySignalDetail Regime(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var bar = candles[i];
        var regime = MarketRegimeClassifier.Confirmed(candles, i, cache, p, context);
        var adx = cache.Adx(p.AdxPeriod)[i];
        var natr = cache.AtrPercent(p.AtrPeriod)[i];
        var percentile = ProductionNewMath.FullPercentile(cache.AtrPercent(p.AtrPeriod), i, p.VolatilityLookback);
        var snap = new Dictionary<string, decimal?>
        {
            ["adx"] = adx,
            ["natr"] = natr,
            ["natrPercentile"] = percentile
        };
        var text =
            $"NO_TRADE: market-regime-new is a filter. regime={regime}. ADX is strength, not direction. Permitted={MarketRegimeClassifier.Permitted(regime)}.";
        return context.HasOpenPosition
            ? Hold(bar, "Regime state does not close an open position by itself. " + text, context.ProtectiveStopPrice, snap)
            : No(bar, text, snap);
    }

    private static StrategySignalDetail Momentum(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var bar = candles[i];
        if (context.PeerCandles is not { Count: >= 2 } peers)
        {
            return No(bar, "NO_TRADE: momentum-relative-strength-new requires a timestamp-aligned peer universe. None was supplied, and none is invented.");
        }

        var regime = MarketRegimeClassifier.Confirmed(candles, i, cache, p, context);
        if (context.HasOpenPosition)
        {
            if (HeldTooLong(candles, i, context, p.MaxHoldBars))
            {
                return Exit(bar, "Maximum holding duration reached.");
            }

            return Hold(bar, "Momentum position stays open. A new rank does not add a second position or reverse this one.", context.ProtectiveStopPrice);
        }

        if (!MarketRegimeClassifier.Allows(regime, StrategyTemplateKeys.MomentumRelativeStrengthNew))
        {
            return No(bar, $"NO_TRADE: regime {regime} does not permit momentum-relative-strength-new.");
        }

        if (p.MomentumWeight24h + p.MomentumWeight7d <= 0m)
        {
            return No(bar, "NO_TRADE: momentum weights sum to zero.");
        }

        var decision = bar.CloseTime;
        var frame = TimeframeExtensions.TryParseInterval(p.Timeframe, out var parsed) ? parsed.ToDuration() : TimeSpan.FromHours(1);
        var ranked = ScoresAt(context, peers, decision, frame, p);
        if (ranked.Count < 2)
        {
            return No(bar, "NO_TRADE: fewer than two symbols have aligned 24h and 7d momentum.");
        }

        var symbol = string.IsNullOrWhiteSpace(context.Symbol) ? "" : context.Symbol;
        var mine = ranked.FindIndex(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (mine < 0)
        {
            return No(bar, "NO_TRADE: this coin is not eligible in the contemporaneous momentum universe.");
        }

        var previous = ScoresAt(context, peers, candles[i - 1].CloseTime, frame, p);
        var top = Math.Clamp(p.TopRankCount, 1, ranked.Count);
        var bottom = Math.Clamp(p.BottomRankCount, 1, ranked.Count);
        var inTop = mine < top && ranked[mine].Score > 0m;
        var inBottom = mine >= ranked.Count - bottom && ranked[mine].Score < 0m;
        var wasTop = previous.FindIndex(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) is var old && old >= 0 && old < top;
        var wasBottom = previous.Count > 0
            && previous.FindIndex(row => string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)) is var oldShort
            && oldShort >= previous.Count - bottom;
        var snap = new Dictionary<string, decimal?>
        {
            ["score"] = ranked[mine].Score,
            ["rank"] = mine + 1,
            ["universe"] = ranked.Count
        };
        if (inTop && !wasTop && StrategySides.AllowsLong(p.AllowedSide))
        {
            var atr = cache.Atr(p.AtrPeriod)[i];
            if (atr is not > 0m)
            {
                return No(bar, "NO_TRADE: ATR is not ready for a momentum stop.", snap);
            }

            return Entry(StrategyTemplateKeys.MomentumRelativeStrengthNew, bar, context, true, atr.Value, p, "Coin entered the top momentum group on this timestamp. Survivorship of listings is not corrected.", snap);
        }

        if (inBottom && !wasBottom && StrategySides.AllowsShort(p.AllowedSide))
        {
            var atr = cache.Atr(p.AtrPeriod)[i];
            if (atr is not > 0m)
            {
                return No(bar, "NO_TRADE: ATR is not ready for a momentum stop.", snap);
            }

            return Entry(StrategyTemplateKeys.MomentumRelativeStrengthNew, bar, context, false, atr.Value, p, "Coin entered the bottom momentum group on this timestamp. Short side is not assumed to be profitable.", snap);
        }

        return No(bar, "NO_TRADE: momentum rank did not newly enter the long or short group.", snap);
    }

    private static StrategySignalDetail MeanReversion(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var bar = candles[i];
        var bands = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var rsiSeries = cache.Rsi(p.RsiPeriod);
        var atrSeries = cache.Atr(p.AtrPeriod);
        if (bands.Lower[i] is not { } lower
            || bands.Upper[i] is not { } upper
            || bands.Mid[i] is not { } mid
            || rsiSeries[i] is not { } rsi
            || atrSeries[i] is not { } atr
            || atr <= 0m)
        {
            return No(bar, "NO_TRADE: Bollinger, RSI, or ATR warm-up is incomplete.");
        }

        var regime = MarketRegimeClassifier.Confirmed(candles, i, cache, p, context);
        var snap = new Dictionary<string, decimal?>
        {
            ["lower"] = lower,
            ["upper"] = upper,
            ["mid"] = mid,
            ["rsi"] = rsi,
            ["atr"] = atr
        };
        if (context.HasOpenPosition)
        {
            var longPosition = IsLong(context);
            if ((longPosition && bar.Close >= mid) || (!longPosition && bar.Close <= mid))
            {
                return Exit(bar, "Close reached the Bollinger middle band.", snap);
            }

            if (HeldTooLong(candles, i, context, p.MaxHoldBars))
            {
                return Exit(bar, "Maximum holding duration reached.", snap);
            }

            return Hold(bar, "Mean-reversion position remains open. Regime changes do not remove the protective stop.", ProtectiveStop(context, longPosition, atr, p.AtrStopMultiplier, false, bar.Close), snap);
        }

        if (!MarketRegimeClassifier.Allows(regime, StrategyTemplateKeys.MeanReversionRsiBollingerNew))
        {
            return No(bar, $"NO_TRADE: regime {regime} does not permit mean-reversion-rsi-bollinger-new. RSI alone is not an entry.", snap);
        }

        var longSetup = p.ReentryConfirmation
            ? ConfirmedReentry(candles, cache, i, p, longSide: true)
            : FreshExtreme(candles, cache, i, p, longSide: true);
        var shortSetup = p.ReentryConfirmation
            ? ConfirmedReentry(candles, cache, i, p, longSide: false)
            : FreshExtreme(candles, cache, i, p, longSide: false);
        if (longSetup && StrategySides.AllowsLong(p.AllowedSide))
        {
            return MeanEntry(p, bar, context, true, atr, mid, snap);
        }

        if (shortSetup && StrategySides.AllowsShort(p.AllowedSide))
        {
            return MeanEntry(p, bar, context, false, atr, mid, snap);
        }

        return No(bar, "NO_TRADE: mean-reversion-rsi-bollinger-new has no confirmed re-entry.", snap);
    }

    private static StrategySignalDetail Funding(
        StrategyTemplateParams p,
        IReadOnlyList<MarketCandle> candles,
        int i,
        StrategyContext context,
        CausalIndicatorCache cache)
    {
        var bar = candles[i];
        if (p.HedgedMode)
        {
            return No(bar, "NO_TRADE: funding-basis-new hedged mode is disabled. Spot execution, dual-leg fills, and hedge recovery are not available. Directional mode is not substituted.");
        }

        if (context.FundingRate is null || context.NormalizedBasis is null)
        {
            return No(bar, "NO_TRADE: funding or basis is missing. A missing rate is not treated as zero.");
        }

        var window = Math.Max(20, p.FundingLookback);
        var fundingZ = ProductionNewMath.PopulationZScore(context.FundingRate, i, window);
        var basisZ = ProductionNewMath.PopulationZScore(context.NormalizedBasis, i, window);
        var rate = i < context.FundingRate.Count ? context.FundingRate[i] : null;
        if (fundingZ is not { } fz || basisZ is not { } bz || rate is not { } fundingRate)
        {
            return No(bar, "NO_TRADE: funding or basis deviation cannot be estimated. No trade.");
        }

        var regime = MarketRegimeClassifier.Confirmed(candles, i, cache, p, context);
        var snap = new Dictionary<string, decimal?>
        {
            ["funding"] = fundingRate,
            ["fundingZ"] = fz,
            ["basisZ"] = bz
        };
        if (context.HasOpenPosition)
        {
            if (HeldTooLong(candles, i, context, p.MaxHoldBars))
            {
                return Exit(bar, "Maximum holding duration reached.", snap);
            }

            var flipped = IsLong(context) ? fundingRate > 0m : fundingRate < 0m;
            if (flipped)
            {
                return Exit(bar, "Funding sign no longer pays this side. The position is closed and not reversed.", snap);
            }

            return Hold(bar, "Funding position remains open.", context.ProtectiveStopPrice, snap);
        }

        if (!MarketRegimeClassifier.Allows(regime, StrategyTemplateKeys.FundingBasisNew))
        {
            return No(bar, $"NO_TRADE: regime {regime} does not permit funding-basis-new.", snap);
        }

        if (Math.Abs(fundingRate) <= ProductionNewMath.RoundTripCostRate)
        {
            return No(bar, "NO_TRADE: one funding payment does not cover the default round-trip fee and slippage. No edge is assumed.", snap);
        }

        var atr = cache.Atr(p.AtrPeriod)[i];
        if (atr is not > 0m)
        {
            return No(bar, "NO_TRADE: ATR is not ready for a funding stop.", snap);
        }

        if (fundingRate < 0m && fz <= -p.FundingZEntry && bz <= -p.BasisZEntry && StrategySides.AllowsLong(p.AllowedSide))
        {
            return Entry(StrategyTemplateKeys.FundingBasisNew, bar, context, true, atr.Value, p, "Negative funding and a cheap basis deviate together. This is not a claim that funding reverses price.", snap);
        }

        if (fundingRate > 0m && fz >= p.FundingZEntry && bz >= p.BasisZEntry && StrategySides.AllowsShort(p.AllowedSide))
        {
            return Entry(StrategyTemplateKeys.FundingBasisNew, bar, context, false, atr.Value, p, "Positive funding and a rich basis deviate together. Short-side profitability is not assumed.", snap);
        }

        return No(bar, "NO_TRADE: funding and basis do not agree on a directional candidate.", snap);
    }

    public static IReadOnlyList<MomentumScore> RankUniverse(
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> peers,
        DateTimeOffset decision,
        TimeSpan bar,
        StrategyTemplateParams parameters) =>
        Rank(peers, decision, bar, parameters);

    private static List<MomentumScore> ScoresAt(
        StrategyContext context,
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> peers,
        DateTimeOffset decision,
        TimeSpan bar,
        StrategyTemplateParams parameters)
    {
        if (context.MomentumScorebook is { } book && book.TryGetValue(decision, out var cached))
        {
            return cached.ToList();
        }

        return Rank(peers, decision, bar, parameters);
    }

    private static List<MomentumScore> Rank(
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> peers,
        DateTimeOffset decision,
        TimeSpan bar,
        StrategyTemplateParams p)
    {
        var rows = new List<MomentumScore>();
        foreach (var pair in peers)
        {
            var end = ProductionNewMath.LastClosedIndex(pair.Value, decision);
            if (end < 2 || !ProductionNewMath.BarUsable(pair.Value[end]))
            {
                continue;
            }

            if (decision - pair.Value[end].CloseTime > bar * 1.5)
            {
                continue;
            }

            var day = ProductionNewMath.ReturnTo(pair.Value, end, TimeSpan.FromHours(24), bar * 1.5);
            var week = ProductionNewMath.ReturnTo(pair.Value, end, TimeSpan.FromDays(7), bar * 1.5);
            if (day is not { } d || week is not { } w)
            {
                continue;
            }

            rows.Add(new MomentumScore(pair.Key, p.MomentumWeight24h * d + p.MomentumWeight7d * w));
        }

        rows.Sort(static (a, b) =>
        {
            var score = b.Score.CompareTo(a.Score);
            return score != 0 ? score : string.Compare(a.Symbol, b.Symbol, StringComparison.Ordinal);
        });
        return rows;
    }

    private static bool Broke(IReadOnlyList<MarketCandle> candles, int index, int lookback, bool longSide)
    {
        if (index < lookback)
        {
            return false;
        }

        var level = longSide
            ? ProductionNewMath.PriorHigh(candles, index, lookback)
            : ProductionNewMath.PriorLow(candles, index, lookback);
        return level is { } price && (longSide ? candles[index].Close > price : candles[index].Close < price);
    }

    private static bool ConfirmedReentry(
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int index,
        StrategyTemplateParams p,
        bool longSide)
    {
        var bands = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var rsi = cache.Rsi(p.RsiPeriod);
        if (!BackInside(bands, candles[index], index, longSide))
        {
            return false;
        }

        var start = -1;
        var window = Math.Max(2, p.ExitLookback);
        for (var k = Math.Max(1, index - window); k < index; k++)
        {
            if (Extreme(bands, rsi, candles[k], k, p, longSide))
            {
                start = k;
            }
        }

        if (start < 0)
        {
            return false;
        }

        for (var k = start + 1; k < index; k++)
        {
            if (BackInside(bands, candles[k], k, longSide))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FreshExtreme(
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int index,
        StrategyTemplateParams p,
        bool longSide)
    {
        var bands = cache.Bollinger(p.BbPeriod, p.BbStdDev);
        var rsi = cache.Rsi(p.RsiPeriod);
        return Extreme(bands, rsi, candles[index], index, p, longSide)
            && !Extreme(bands, rsi, candles[index - 1], index - 1, p, longSide);
    }

    private static bool Extreme(
        (decimal?[] Mid, decimal?[] Upper, decimal?[] Lower) bands,
        IReadOnlyList<decimal?> rsi,
        MarketCandle candle,
        int index,
        StrategyTemplateParams p,
        bool longSide)
    {
        if (index < 0 || rsi[index] is not { } value)
        {
            return false;
        }

        return longSide
            ? bands.Lower[index] is { } lower && candle.Close < lower && value < p.RsiOversold
            : bands.Upper[index] is { } upper && candle.Close > upper && value > p.RsiOverbought;
    }

    private static bool BackInside(
        (decimal?[] Mid, decimal?[] Upper, decimal?[] Lower) bands,
        MarketCandle candle,
        int index,
        bool longSide) =>
        longSide
            ? bands.Lower[index] is { } lower && candle.Close >= lower
            : bands.Upper[index] is { } upper && candle.Close <= upper;

    private static StrategySignalDetail MeanEntry(
        StrategyTemplateParams p,
        MarketCandle bar,
        StrategyContext context,
        bool longSide,
        decimal atr,
        decimal middle,
        Dictionary<string, decimal?> snap)
    {
        var stop = longSide ? bar.Close - p.AtrStopMultiplier * atr : bar.Close + p.AtrStopMultiplier * atr;
        var target = p.TakeProfitR > 0m
            ? longSide
                ? bar.Close + p.TakeProfitR * Math.Abs(bar.Close - stop)
                : bar.Close - p.TakeProfitR * Math.Abs(bar.Close - stop)
            : middle;
        var distance = Math.Abs(target - bar.Close) / bar.Close;
        if (distance <= ProductionNewMath.RoundTripCostRate)
        {
            return No(bar, "NO_TRADE: the mean-reversion target does not clear fees, slippage, and a funding buffer.", snap);
        }

        if (longSide ? target <= bar.Close || stop >= bar.Close : target >= bar.Close || stop <= bar.Close)
        {
            return No(bar, "NO_TRADE: stop or target is on the wrong side.", snap);
        }

        snap["stop"] = stop;
        snap["target"] = target;
        var id = ProductionNewMath.SignalId(StrategyTemplateKeys.MeanReversionRsiBollingerNew, context.Symbol, bar.CloseTime, longSide ? "LONG" : "SHORT");
        return new StrategySignalDetail(
            longSide ? SignalType.Buy : SignalType.Sell,
            $"mean-reversion-rsi-bollinger-new {id}. Confirmed re-entry toward the middle band. RSI is not an automatic order.",
            bar.CloseTime,
            stop,
            target,
            snap,
            ProductionNewDefaults.Evidence);
    }

    private static StrategySignalDetail Entry(
        string strategyId,
        MarketCandle bar,
        StrategyContext context,
        bool longSide,
        decimal atr,
        StrategyTemplateParams p,
        string because,
        Dictionary<string, decimal?> snap)
    {
        var stop = longSide ? bar.Close - p.AtrStopMultiplier * atr : bar.Close + p.AtrStopMultiplier * atr;
        if (atr <= 0m || stop <= 0m || (longSide ? stop >= bar.Close : stop <= bar.Close))
        {
            return No(bar, "NO_TRADE: protective stop is invalid or too close.", snap);
        }

        decimal? target = null;
        if (p.TakeProfitR > 0m)
        {
            var risk = Math.Abs(bar.Close - stop);
            target = longSide ? bar.Close + p.TakeProfitR * risk : bar.Close - p.TakeProfitR * risk;
        }

        snap["stop"] = stop;
        snap["atr"] = atr;
        var id = ProductionNewMath.SignalId(strategyId, context.Symbol, bar.CloseTime, longSide ? "LONG" : "SHORT");
        return new StrategySignalDetail(
            longSide ? SignalType.Buy : SignalType.Sell,
            $"{strategyId} {id}. {because}",
            bar.CloseTime,
            stop,
            target,
            snap,
            ProductionNewDefaults.Evidence);
    }

    private static decimal? ProtectiveStop(
        StrategyContext context,
        bool longPosition,
        decimal atr,
        decimal multiplier,
        bool trail,
        decimal close)
    {
        if (!trail && context.ProtectiveStopPrice is > 0m)
        {
            return context.ProtectiveStopPrice;
        }

        var entry = context.AverageEntryPrice is > 0m ? context.AverageEntryPrice.Value : close;
        var raw = longPosition ? entry - multiplier * atr : entry + multiplier * atr;
        if (!trail || context.ProtectiveStopPrice is not { } current)
        {
            return raw;
        }

        if (longPosition)
        {
            return raw > current && raw < close ? raw : current;
        }

        return raw < current && raw > close ? raw : current;
    }

    private static bool HeldTooLong(IReadOnlyList<MarketCandle> candles, int index, StrategyContext context, int maxHoldBars)
    {
        if (maxHoldBars <= 0 || context.PositionOpenedAt is not { } opened)
        {
            return false;
        }

        var held = 0;
        for (var k = index; k >= 0 && candles[k].CloseTime > opened; k--)
        {
            held++;
        }

        return held >= maxHoldBars;
    }

    private static bool IsLong(StrategyContext context) => context.PositionSide != PositionSide.Short;

    private static Dictionary<string, decimal?> Snapshot(decimal fast, decimal slow, decimal adx, decimal atr, string regime)
    {
        var snap = new Dictionary<string, decimal?>
        {
            ["emaFast"] = fast,
            ["emaSlow"] = slow,
            ["adx"] = adx,
            ["atr"] = atr
        };
        _ = regime;
        return snap;
    }

    private static StrategySignalDetail No(MarketCandle? bar, string reason, Dictionary<string, decimal?>? snapshot = null) =>
        new(SignalType.NoAction, reason, bar?.CloseTime, Snapshot: snapshot, Status: ProductionNewDefaults.Evidence);

    private static StrategySignalDetail Hold(MarketCandle bar, string reason, decimal? stop = null, Dictionary<string, decimal?>? snapshot = null) =>
        new(SignalType.Hold, reason, bar.CloseTime, stop, Snapshot: snapshot, Status: ProductionNewDefaults.Evidence);

    private static StrategySignalDetail Exit(MarketCandle bar, string reason, Dictionary<string, decimal?>? snapshot = null) =>
        new(SignalType.Exit, reason, bar.CloseTime, Snapshot: snapshot, Status: ProductionNewDefaults.Evidence);

}

public readonly record struct MomentumScore(string Symbol, decimal Score);
