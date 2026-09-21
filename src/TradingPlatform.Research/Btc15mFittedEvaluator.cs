using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

/// <summary>
/// Exact BTCUSDT 15m historically fitted natives from Btc15mFit.BuildRecipes.
/// Parameters are frozen. Do not retune.
/// </summary>
public static class Btc15mFittedEvaluator
{
    public const string VolSpikeEmaTrend = "vol_spike_ema_trend";
    public const string Bb202Break = "bb20_2_break";

    public static bool Handles(string? nativeKey) =>
        string.Equals(nativeKey, VolSpikeEmaTrend, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nativeKey, Bb202Break, StringComparison.OrdinalIgnoreCase);

    public static StrategySignalDetail Evaluate(
        ResearchCandidate candidate,
        IReadOnlyList<MarketCandle> candles,
        CausalIndicatorCache cache,
        int i,
        StrategyContext context)
    {
        if (context.HasOpenPosition)
        {
            return Detail(SignalType.Hold, "Position open; Isolated book owns SL/TP/time-exit.", candles, i);
        }

        if (i < 1 || i >= candles.Count)
        {
            return Detail(SignalType.NoAction, "Not enough closed candles.", candles, i);
        }

        return candidate.NativeKey switch
        {
            VolSpikeEmaTrend => VolSpike(candles, cache, i),
            Bb202Break => BbBreak(candles, cache, i),
            _ => Detail(SignalType.NoAction, "Unknown BTC 15m fitted native key.", candles, i)
        };
    }

    private static StrategySignalDetail VolSpike(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        var rel = cache.RelativeVolume(20);
        var ema21 = cache.Ema(21);
        if (rel[i] is not { } rvNow || rel[i - 1] is not { } rvPrev || ema21[i] is not { } ema)
        {
            return Detail(SignalType.NoAction, "Relative volume / EMA21 not ready.", candles, i);
        }

        var spike = rvNow > 1.5m && rvPrev <= 1.5m;
        if (!spike)
        {
            return Detail(SignalType.NoAction, "No first-bar relative-volume spike.", candles, i);
        }

        var close = candles[i].Close;
        if (close > ema)
        {
            return Detail(SignalType.Buy, "Relative volume spike with close above EMA21.", candles, i);
        }

        if (close < ema)
        {
            return Detail(SignalType.Sell, "Relative volume spike with close below EMA21.", candles, i);
        }

        return Detail(SignalType.NoAction, "Volume spike but close equals EMA21.", candles, i);
    }

    private static StrategySignalDetail BbBreak(IReadOnlyList<MarketCandle> candles, CausalIndicatorCache cache, int i)
    {
        var (_, upper, lower) = cache.Bollinger(20, 2m);
        if (upper[i] is not { } up || lower[i] is not { } lo || upper[i - 1] is not { } prevUp || lower[i - 1] is not { } prevLo)
        {
            return Detail(SignalType.NoAction, "Bollinger (20,2) not ready.", candles, i);
        }

        var close = candles[i].Close;
        var prevClose = candles[i - 1].Close;
        if (close > up && prevClose <= prevUp)
        {
            return Detail(SignalType.Buy, "Close crossed above upper Bollinger (20,2).", candles, i);
        }

        if (close < lo && prevClose >= prevLo)
        {
            return Detail(SignalType.Sell, "Close crossed below lower Bollinger (20,2).", candles, i);
        }

        return Detail(SignalType.NoAction, "No Bollinger (20,2) break.", candles, i);
    }

    private static StrategySignalDetail Detail(SignalType signal, string reason, IReadOnlyList<MarketCandle> candles, int i) =>
        new(signal, reason, candles[Math.Clamp(i, 0, candles.Count - 1)].CloseTime);
}
