using System.Globalization;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Strategies.Engine;

/// <summary>
/// Live forms of the three least-negative phase 2 alpha configurations (ALPHA_DISCOVERY_REPORT.md section 19).
/// None passed the IS gate; they exist for small-size forward observation. Formulas mirror
/// <c>TradingPlatform.Research.Alpha.PanelFeatures</c> on closed 1h bars. Research had no stop or target; the stop
/// here is a catastrophic rail (2.5 σ of the hold horizon, 2–15%) and the target sits at 3× that distance.
/// Quote volume is base volume × close because the kline parser keeps base volume only.
/// </summary>
public static class ObservationStrategies
{
    public const int HistoryBars = 1000;
    public const int VolWindow = 720;
    public const int RangeWindow = 168;
    public const int FastVolWindow = 24;
    public const int CompressionStepHours = 4;
    public const int CompressionHoldHours = 72;
    public const double CompressionCut = 0.5;
    public const int ShockHoldHours = 24;
    public const double ShockRangeMultiple = 5;
    public const double ShockVolumeMultiple = 3;
    public const int TopTraderStepHours = 24;
    public const int TopTraderHoldHours = 72;
    public const double TopTraderQuantile = 0.2;
    public const int TopTraderMinimumCoins = 20;
    public const int TopTraderZDays = 30;
    public const int UniverseDays = 30;
    public const int UniverseMinDays = 20;
    public const int UniverseMinBarsPerDay = 20;
    public const decimal UniverseMinMedianQuoteVolume = 5_000_000m;
    public const decimal StopSigma = 2.5m;
    public const decimal MinStopPercent = 2m;
    public const decimal MaxStopPercent = 15m;
    public const decimal TakeMultiple = 3m;
    public const decimal MaxEntryMarginUsdt = 8m;

    public static int MaxHoldHours(string? key) => StrategyTemplateKeys.Normalize(key) switch
    {
        StrategyTemplateKeys.ObsCompressionBreakout => CompressionHoldHours,
        StrategyTemplateKeys.ObsShockFade => ShockHoldHours,
        StrategyTemplateKeys.ObsTopTraderContrarian => TopTraderHoldHours,
        _ => 0
    };

    public static StrategySignalDetail Evaluate(string key, IReadOnlyList<MarketCandle> candles, int i, StrategyContext context)
    {
        if (i < 1 || i >= candles.Count)
        {
            return new StrategySignalDetail(SignalType.NoAction, "Not enough closed candles.");
        }

        var bar = candles[i];
        if (context.HasOpenPosition)
        {
            return HoldOrExit(key, bar, context.PositionOpenedAt);
        }

        return StrategyTemplateKeys.Normalize(key) switch
        {
            StrategyTemplateKeys.ObsCompressionBreakout => CompressionBreakout(candles, i),
            StrategyTemplateKeys.ObsShockFade => ShockFade(candles, i),
            _ => Detail(SignalType.NoAction, "Top-trader contrarian ranks the whole universe. A single-coin preview does not emit an order.", bar)
        };
    }

    public static StrategySignalDetail HoldOrExit(string key, MarketCandle bar, DateTimeOffset? openedAt)
    {
        var hold = MaxHoldHours(key);
        if (openedAt is { } open && hold > 0 && bar.CloseTime >= open.AddHours(hold).AddMinutes(-1))
        {
            return Detail(SignalType.Exit, $"Time exit after {hold} hours, as in the research hold.", bar);
        }

        return Detail(SignalType.Hold, $"Holding until the {hold}-hour time exit. The exchange stop and take are rails only.", bar);
    }

    /// <summary>Research F.compression-breakout.c0.5.hold72.step4.</summary>
    public static StrategySignalDetail CompressionBreakout(IReadOnlyList<MarketCandle> candles, int i)
    {
        var bar = candles[i];
        if (!IsDecisionHour(bar, CompressionStepHours))
        {
            return Detail(SignalType.NoAction, "Compression breakout decides on 4-hour UTC closes only.", bar);
        }

        if (NotReady(candles, i) is { } why)
        {
            return Detail(SignalType.NoAction, why, bar);
        }

        var fast = Vol(candles, i - 1, FastVolWindow);
        var slow = Vol(candles, i - 1, VolWindow);
        if (double.IsNaN(fast) || double.IsNaN(slow) || slow <= 0)
        {
            return Detail(SignalType.NoAction, "Volatility is not defined.", bar);
        }

        var ratio = fast / slow;
        var high = PriorHigh(candles, i, RangeWindow);
        var low = PriorLow(candles, i, RangeWindow);
        var snapshot = new Dictionary<string, decimal?>
        {
            ["volRatio"] = (decimal)ratio,
            ["priorHigh"] = high,
            ["priorLow"] = low
        };
        if (ratio >= CompressionCut)
        {
            return Detail(SignalType.NoAction, $"24h vol is {ratio.ToString("0.00", CultureInfo.InvariantCulture)}× the 30-day vol. Needs < 0.50.", bar, snapshot: snapshot);
        }

        var side = bar.Close > high ? SignalType.Buy : bar.Close < low ? SignalType.Sell : SignalType.NoAction;
        if (side == SignalType.NoAction)
        {
            return Detail(SignalType.NoAction, "Compressed, but the close is inside the prior 7-day range.", bar, snapshot: snapshot);
        }

        return Entry(side, bar, slow, CompressionHoldHours,
            $"Compression breakout: 24h vol {ratio.ToString("0.00", CultureInfo.InvariantCulture)}× 30-day vol and the close broke the prior 7-day {(side == SignalType.Buy ? "high" : "low")}. Time exit 72h.",
            snapshot);
    }

    /// <summary>Research E.fade.hold24.k5.step1.</summary>
    public static StrategySignalDetail ShockFade(IReadOnlyList<MarketCandle> candles, int i)
    {
        var bar = candles[i];
        if (NotReady(candles, i) is { } why)
        {
            return Detail(SignalType.NoAction, why, bar);
        }

        var avgRange = AverageRange(candles, i - 1, RangeWindow);
        var range = bar.Close > 0m ? (double)((bar.High - bar.Low) / bar.Close) : double.NaN;
        if (double.IsNaN(avgRange) || double.IsNaN(range) || range < ShockRangeMultiple * avgRange)
        {
            return Detail(SignalType.NoAction, "No shock: the bar range is below 5× the 7-day average range.", bar);
        }

        var volumeRatio = VolumeRatio(candles, i, RangeWindow);
        if (double.IsNaN(volumeRatio) || volumeRatio < ShockVolumeMultiple)
        {
            return Detail(SignalType.NoAction, "Range shock without a 3× volume spike.", bar);
        }

        var prior = candles[i - 1].Close;
        if (prior <= 0m || bar.Close == prior)
        {
            return Detail(SignalType.NoAction, "Shock bar has no direction.", bar);
        }

        var side = bar.Close > prior ? SignalType.Sell : SignalType.Buy;
        var slow = Vol(candles, i - 1, VolWindow);
        var snapshot = new Dictionary<string, decimal?>
        {
            ["rangeMultiple"] = (decimal)(range / avgRange),
            ["volumeRatio"] = (decimal)volumeRatio
        };
        return Entry(side, bar, slow, ShockHoldHours,
            $"Shock fade: range {(range / avgRange).ToString("0.0", CultureInfo.InvariantCulture)}× and volume {volumeRatio.ToString("0.0", CultureInfo.InvariantCulture)}× the 7-day average. Fades the {(side == SignalType.Sell ? "up" : "down")} move. Time exit 24h.",
            snapshot);
    }

    private static string? NotReady(IReadOnlyList<MarketCandle> candles, int i)
    {
        if (i < VolWindow + 1)
        {
            return "Needs 721 closed hours.";
        }

        var why = Universe(candles, i);
        return why.Length > 0 ? why : null;
    }

    /// <summary>
    /// Research universe for day d: days d-30..d-1 with at least 20 bars, at least 20 such days, median daily quote volume
    /// ≥ $5M. Returns an empty string when eligible, otherwise the reason.
    /// </summary>
    public static string Universe(IReadOnlyList<MarketCandle> candles, int i)
    {
        var day = candles[i].OpenTime.UtcDateTime.Date;
        var from = new DateTimeOffset(day.AddDays(-UniverseDays), TimeSpan.Zero);
        var until = new DateTimeOffset(day, TimeSpan.Zero);
        var sums = new Dictionary<DateTime, (decimal Quote, int Bars)>();
        for (var k = i; k >= 0; k--)
        {
            var open = candles[k].OpenTime;
            if (open < from)
            {
                break;
            }

            if (open >= until)
            {
                continue;
            }

            var key = open.UtcDateTime.Date;
            var row = sums.TryGetValue(key, out var existing) ? existing : (0m, 0);
            sums[key] = (row.Item1 + candles[k].Volume * candles[k].Close, row.Item2 + 1);
        }

        var days = sums.Values.Where(v => v.Bars >= UniverseMinBarsPerDay).Select(v => v.Quote).OrderBy(v => v).ToList();
        if (days.Count < UniverseMinDays)
        {
            return "Fewer than 20 full days in the last 30. Not in the research universe.";
        }

        var median = days.Count % 2 == 1 ? days[days.Count / 2] : (days[days.Count / 2 - 1] + days[days.Count / 2]) / 2m;
        return median >= UniverseMinMedianQuoteVolume
            ? ""
            : $"Median daily volume ${median / 1_000_000m:0.0}M is below $5M. Not in the research universe.";
    }

    /// <summary>True when the bar closes on a UTC hour that is a multiple of <paramref name="step"/> (research <c>(t+1) % step == 0</c>).</summary>
    public static bool IsDecisionHour(MarketCandle bar, int step) => (bar.OpenTime.UtcDateTime.Hour + 1) % step == 0;

    /// <summary>Sample std of simple hourly returns of bars end-L+1..end.</summary>
    public static double Vol(IReadOnlyList<MarketCandle> candles, int end, int lookback)
    {
        if (end - lookback < 0 || end >= candles.Count)
        {
            return double.NaN;
        }

        double s = 0, s2 = 0;
        var n = 0;
        for (var k = end - lookback + 1; k <= end; k++)
        {
            var a = candles[k - 1].Close;
            var b = candles[k].Close;
            if (a <= 0m || b <= 0m)
            {
                continue;
            }

            var x = (double)(b / a) - 1d;
            s += x;
            s2 += x * x;
            n++;
        }

        if (n < 2)
        {
            return double.NaN;
        }

        var variance = (s2 - s * s / n) / (n - 1);
        return variance > 0 ? Math.Sqrt(variance) : double.NaN;
    }

    public static decimal PriorHigh(IReadOnlyList<MarketCandle> candles, int i, int lookback)
    {
        var high = decimal.MinValue;
        for (var k = Math.Max(0, i - lookback); k < i; k++)
        {
            high = Math.Max(high, candles[k].High);
        }

        return high;
    }

    public static decimal PriorLow(IReadOnlyList<MarketCandle> candles, int i, int lookback)
    {
        var low = decimal.MaxValue;
        for (var k = Math.Max(0, i - lookback); k < i; k++)
        {
            low = Math.Min(low, candles[k].Low);
        }

        return low;
    }

    /// <summary>Mean of (high-low)/close over bars end-L+1..end.</summary>
    public static double AverageRange(IReadOnlyList<MarketCandle> candles, int end, int lookback)
    {
        if (end - lookback + 1 < 0)
        {
            return double.NaN;
        }

        var sum = 0d;
        var n = 0;
        for (var k = end - lookback + 1; k <= end; k++)
        {
            if (candles[k].Close > 0m)
            {
                sum += (double)((candles[k].High - candles[k].Low) / candles[k].Close);
                n++;
            }
        }

        return n == 0 ? double.NaN : sum / n;
    }

    /// <summary>Quote volume of bar i over the mean quote volume of bars i-L..i-1.</summary>
    public static double VolumeRatio(IReadOnlyList<MarketCandle> candles, int i, int baseline)
    {
        if (i - baseline < 0)
        {
            return double.NaN;
        }

        var before = 0m;
        for (var k = i - baseline; k < i; k++)
        {
            before += candles[k].Volume * candles[k].Close;
        }

        return before <= 0m ? double.NaN : (double)(candles[i].Volume * candles[i].Close / (before / baseline));
    }

    /// <summary>Stop distance in percent: 2.5 × hourly σ × √hold, clamped to 2–15%.</summary>
    public static decimal StopPercent(double hourlyVol, int holdHours)
    {
        if (double.IsNaN(hourlyVol) || hourlyVol <= 0)
        {
            return MaxStopPercent;
        }

        var raw = StopSigma * (decimal)(hourlyVol * Math.Sqrt(holdHours)) * 100m;
        return Math.Clamp(raw, MinStopPercent, MaxStopPercent);
    }

    public static StrategySignalDetail Entry(
        SignalType side,
        MarketCandle bar,
        double hourlyVol,
        int holdHours,
        string reason,
        Dictionary<string, decimal?>? snapshot = null)
    {
        var stopPct = StopPercent(hourlyVol, holdHours);
        var close = bar.Close;
        var stopDistance = close * stopPct / 100m;
        var stop = side == SignalType.Buy ? close - stopDistance : close + stopDistance;
        var take = side == SignalType.Buy ? close + TakeMultiple * stopDistance : close - TakeMultiple * stopDistance;
        if (stop <= 0m || take <= 0m)
        {
            return Detail(SignalType.NoAction, "Stop or take would be at or below zero.", bar, snapshot: snapshot);
        }

        snapshot ??= new Dictionary<string, decimal?>();
        snapshot["stopPercent"] = stopPct;
        snapshot["takePercent"] = stopPct * TakeMultiple;
        return Detail(side, reason, bar, stop, take, snapshot);
    }

    /// <summary>
    /// z of the latest value against the values 24h·k earlier (k = 1..30), at least 24 present. NaN when undefined.
    /// </summary>
    public static double TopTraderZ(IReadOnlyList<(DateTimeOffset Time, decimal Value)> series, DateTimeOffset at)
    {
        var byTime = new Dictionary<DateTimeOffset, decimal>();
        foreach (var row in series)
        {
            byTime[row.Time] = row.Value;
        }

        if (!byTime.TryGetValue(at, out var now))
        {
            return double.NaN;
        }

        var values = new List<double>(TopTraderZDays);
        for (var k = 1; k <= TopTraderZDays; k++)
        {
            if (byTime.TryGetValue(at.AddHours(-24 * k), out var v))
            {
                values.Add((double)v);
            }
        }

        if (values.Count < TopTraderZDays * 0.8)
        {
            return double.NaN;
        }

        var mean = values.Average();
        var sd = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1));
        return sd > 1e-9 ? ((double)now - mean) / sd : double.NaN;
    }

    /// <summary>
    /// Research D.toptrader-contrarian: signal = −z, long the top 20% of signal (top traders unusually short), short the
    /// bottom 20%. Ties break on coin name. Empty when fewer than 20 coins have a defined signal.
    /// </summary>
    public static TopTraderRanking RankTopTrader(DateTimeOffset at, DateTimeOffset dataAt, IReadOnlyDictionary<string, double> zByCoin)
    {
        var rows = zByCoin
            .Where(kv => double.IsFinite(kv.Value))
            .Select(kv => (Coin: kv.Key, Signal: -kv.Value))
            .OrderBy(r => r.Signal)
            .ThenBy(r => r.Coin, StringComparer.Ordinal)
            .ToList();
        if (rows.Count < TopTraderMinimumCoins)
        {
            return new TopTraderRanking(at, dataAt, rows.Count, new HashSet<string>(), new HashSet<string>());
        }

        var k = Math.Max(1, (int)Math.Floor(rows.Count * TopTraderQuantile));
        var shorts = rows.Take(k).Select(r => r.Coin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var longs = rows.Skip(rows.Count - k).Select(r => r.Coin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new TopTraderRanking(at, dataAt, rows.Count, longs, shorts);
    }

    public static StrategySignalDetail TopTraderDecision(
        string symbol,
        IReadOnlyList<MarketCandle> candles,
        bool hasPosition,
        DateTimeOffset? openedAt,
        TopTraderRanking? ranking)
    {
        if (candles.Count == 0)
        {
            return new StrategySignalDetail(SignalType.NoAction, "Not enough closed candles.");
        }

        var bar = candles[^1];
        if (hasPosition)
        {
            return HoldOrExit(StrategyTemplateKeys.ObsTopTraderContrarian, bar, openedAt);
        }

        if (!IsDecisionHour(bar, TopTraderStepHours))
        {
            return Detail(SignalType.NoAction, "Top-trader contrarian rebalances on the 00:00 UTC close only.", bar);
        }

        var at = bar.CloseTime.AddMilliseconds(1);
        if (ranking is null || ranking.At != at)
        {
            return Detail(SignalType.NoAction, "The top-trader ranking for this close is not ready yet.", bar);
        }

        if (ranking.Coins < TopTraderMinimumCoins)
        {
            return Detail(SignalType.NoAction, $"Only {ranking.Coins} coins have a defined top-trader z. Needs 20.", bar);
        }

        var side = ranking.Longs.Contains(symbol) ? SignalType.Buy : ranking.Shorts.Contains(symbol) ? SignalType.Sell : SignalType.NoAction;
        if (side == SignalType.NoAction)
        {
            return Detail(SignalType.NoAction, $"Coin is outside the top and bottom 20% of {ranking.Coins} ranked coins.", bar);
        }

        var vol = Vol(candles, candles.Count - 2, Math.Min(VolWindow, candles.Count - 2));
        return Entry(side, bar, vol, TopTraderHoldHours,
            side == SignalType.Buy
                ? $"Top traders are unusually short versus their 30-day history (bottom 20% of {ranking.Coins} coins). Contrarian long. Time exit 72h."
                : $"Top traders are unusually long versus their 30-day history (top 20% of {ranking.Coins} coins). Contrarian short. Time exit 72h.");
    }

    private static StrategySignalDetail Detail(
        SignalType signal,
        string reason,
        MarketCandle bar,
        decimal? stop = null,
        decimal? take = null,
        IReadOnlyDictionary<string, decimal?>? snapshot = null) =>
        new(signal, reason, bar.CloseTime, stop, take, snapshot, Status: StrategyValidationStatuses.Researching);
}

/// <summary>Ranking for the decision close <paramref name="At"/> built from ratio prints stamped <paramref name="DataAt"/> (≤ At).</summary>
public sealed record TopTraderRanking(DateTimeOffset At, DateTimeOffset DataAt, int Coins, IReadOnlySet<string> Longs, IReadOnlySet<string> Shorts);
