using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Research;

public sealed class Wave5Event
{
    public required string Strategy { get; init; }
    public required string Family { get; init; }
    public required string Symbol { get; init; }
    public required string Timeframe { get; init; }
    public required DateTimeOffset SignalTime { get; init; }
    public required DateTimeOffset FillTime { get; init; }
    public required DateTimeOffset ExitTime { get; init; }
    public required int Direction { get; init; }
    public required decimal Entry { get; init; }
    public required decimal Exit { get; init; }
    public required double GrossRet { get; init; }
    public required double AtrPct { get; init; }
    public required int VolRegime { get; init; }
    public required int TrendRegime { get; init; }
    public required int BtcRegime { get; init; }
    public required double Breadth { get; init; }
    public required double RelStrength { get; init; }
    public required double RelVolume { get; init; }
    public required double VwapDist { get; init; }
    public required double RecentRet { get; init; }
    public int HoldBars { get; init; }
    public bool Win { get; init; }
}

public sealed record Wave5MarketFrame(
    DateTimeOffset OpenTime,
    int VolRegime,
    int BtcTrend,
    double Breadth);

public static class Wave5Harvest
{
    public const int Warmup = 120;
    public const int RelLookback = 24;
    public const decimal Fee = 0.0004m;
    public const decimal Slip = 0.0002m;
    public const decimal StopPct = 0.02m;
    public const decimal TakePct = 0.04m;

    public static double NetReturn(double gross, decimal costMult) =>
        gross - 2.0 * (double)(costMult * (Fee + Slip));

    public static List<Wave5Event> Harvest(
        IReadOnlyList<ResearchCandidate> candidates,
        IReadOnlyDictionary<(string Symbol, string Timeframe), IReadOnlyList<MarketCandle>> series,
        string timeframe,
        IReadOnlyList<string> symbols)
    {
        if (!series.TryGetValue(("BTCUSDT", timeframe), out var btcBars) || btcBars.Count < Warmup + 30)
        {
            return [];
        }

        var caches = new Dictionary<string, CausalIndicatorCache>(StringComparer.OrdinalIgnoreCase);
        foreach (var symbol in symbols)
        {
            if (!series.TryGetValue((symbol, timeframe), out var bars) || bars.Count < Warmup + 30)
            {
                continue;
            }

            caches[symbol] = new CausalIndicatorCache(bars.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
        }

        if (!caches.TryGetValue("BTCUSDT", out var btcCache))
        {
            return [];
        }

        var market = BuildMarket(caches, btcCache);
        var htfName = ResearchRegistry.NextHigherTimeframe(timeframe);
        CausalIndicatorCache? btcHtf = null;
        if (!string.IsNullOrWhiteSpace(htfName) && series.TryGetValue(("BTCUSDT", htfName), out var htfBars))
        {
            btcHtf = new CausalIndicatorCache(htfBars.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
        }

        var btcIndex = new Dictionary<DateTimeOffset, int>(btcCache.Candles.Count);
        for (var bi = 0; bi < btcCache.Candles.Count; bi++)
        {
            btcIndex[btcCache.Candles[bi].OpenTime] = bi;
        }

        var btcRecent = new double[btcCache.Candles.Count];
        Array.Fill(btcRecent, double.NaN);
        for (var bi = RelLookback; bi < btcCache.Candles.Count; bi++)
        {
            var bp = btcCache.Candles[bi - RelLookback].Close;
            if (bp > 0m)
            {
                btcRecent[bi] = (double)((btcCache.Candles[bi].Close - bp) / bp);
            }
        }

        var events = new List<Wave5Event>();
        foreach (var symbol in caches.Keys.ToArray())
        {
            var cache = caches[symbol];
            var candles = cache.Candles;
            var atr = cache.AtrPercent(14);
            var relVol = cache.RelativeVolume(20);
            var ema20 = cache.Ema(20);
            var ema50 = cache.Ema(50);
            var vwap = cache.SessionVwap();
            CausalIndicatorCache? htf = null;
            if (!string.IsNullOrWhiteSpace(htfName) && series.TryGetValue((symbol, htfName), out var sh))
            {
                htf = new CausalIndicatorCache(sh.Where(c => c.IsClosed).OrderBy(c => c.OpenTime).ToList());
            }

            foreach (var candidate in candidates)
            {
                if (!candidate.SupportedTimeframes.Contains(timeframe, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                ResearchStrategyEngine engine;
                StrategyDefinition definition;
                try
                {
                    engine = new ResearchStrategyEngine(candidate);
                    definition = ResearchRunner.DefinitionFor(candidate, timeframe);
                }
                catch
                {
                    continue;
                }
                var context = new StrategyContext
                {
                    ClosedCandles = candles,
                    CurrentPrice = candles[^1].Close,
                    HasOpenPosition = false,
                    PositionSide = PositionSide.Long,
                    HigherTimeframeCache = htf ?? btcHtf
                };

                var occupiedUntil = DateTimeOffset.MinValue;
                Console.WriteLine($"  {timeframe} {symbol} {candidate.CandidateId}");
                for (var i = Warmup; i < candles.Count - 2; i++)
                {
                    if (candles[i].CloseTime < occupiedUntil)
                    {
                        continue;
                    }

                    StrategySignalDetail detail;
                    try
                    {
                        detail = engine.EvaluateDetailAt(definition, context, cache, i);
                    }
                    catch
                    {
                        continue;
                    }
                    if (detail.Signal is not (SignalType.Buy or SignalType.Sell))
                    {
                        continue;
                    }

                    var outcome = Simulate(candles, i, detail.Signal == SignalType.Buy);
                    if (outcome is null)
                    {
                        continue;
                    }

                    var open = candles[i].OpenTime;
                    market.TryGetValue(open, out var frame);
                    var close = candles[i].Close;
                    var prev = i >= RelLookback ? candles[i - RelLookback].Close : 0m;
                    var recent = prev > 0m ? (double)((close - prev) / prev) : double.NaN;
                    var btcRel = btcIndex.TryGetValue(open, out var btcIdx) && btcIdx >= 0 && btcIdx < btcRecent.Length
                        ? btcRecent[btcIdx]
                        : double.NaN;

                    var trend = 0;
                    if (ema20[i] is { } f && ema50[i] is { } s)
                    {
                        trend = f > s ? 1 : f < s ? -1 : 0;
                    }

                    var atrNow = atr[i] is { } ap ? (double)ap : double.NaN;
                    var rv = relVol[i] is { } r ? (double)r : double.NaN;
                    var vwapDist = vwap[i] is { } vw && atr[i] is { } a && a > 0m
                        ? (double)((close - vw) / (close * a / 100m + 0.0000001m))
                        : double.NaN;

                    events.Add(new Wave5Event
                    {
                        Strategy = candidate.CandidateId,
                        Family = Wave5Catalog.Family(candidate),
                        Symbol = symbol,
                        Timeframe = timeframe,
                        SignalTime = candles[i].CloseTime,
                        FillTime = outcome.Value.FillTime,
                        ExitTime = outcome.Value.ExitTime,
                        Direction = detail.Signal == SignalType.Buy ? 1 : -1,
                        Entry = outcome.Value.Entry,
                        Exit = outcome.Value.Exit,
                        GrossRet = outcome.Value.Gross,
                        AtrPct = atrNow,
                        VolRegime = frame?.VolRegime ?? 0,
                        TrendRegime = trend,
                        BtcRegime = frame?.BtcTrend ?? 0,
                        Breadth = frame?.Breadth ?? double.NaN,
                        RelStrength = double.IsNaN(recent) || double.IsNaN(btcRel) ? double.NaN : recent - btcRel,
                        RelVolume = rv,
                        VwapDist = vwapDist,
                        RecentRet = recent,
                        HoldBars = outcome.Value.Hold,
                        Win = outcome.Value.Gross > 0
                    });
                    occupiedUntil = outcome.Value.ExitTime;
                }
            }
        }

        return events;
    }

    public static (DateTimeOffset FillTime, DateTimeOffset ExitTime, decimal Entry, decimal Exit, double Gross, int Hold)? Simulate(
        IReadOnlyList<MarketCandle> candles,
        int signalIndex,
        bool isLong)
    {
        var fill = signalIndex + 1;
        if (fill >= candles.Count)
        {
            return null;
        }

        var open = candles[fill].Open;
        if (open <= 0m)
        {
            return null;
        }

        var side = isLong ? 1m : -1m;
        var entry = open * (1m + side * Slip);
        var stop = entry * (1m - side * StopPct);
        var take = entry * (1m + side * TakePct);
        for (var j = fill; j < candles.Count; j++)
        {
            var bar = candles[j];
            var hitStop = isLong ? bar.Low <= stop : bar.High >= stop;
            var hitTake = isLong ? bar.High >= take : bar.Low <= take;
            decimal exitPx;
            if (hitStop)
            {
                exitPx = stop * (1m - side * Slip);
            }
            else if (hitTake)
            {
                exitPx = take * (1m - side * Slip);
            }
            else if (j == candles.Count - 1)
            {
                exitPx = bar.Close * (1m - side * Slip);
            }
            else
            {
                continue;
            }

            var gross = (double)(side * (exitPx - entry) / entry);
            return (candles[fill].OpenTime, bar.CloseTime, entry, exitPx, gross, j - fill + 1);
        }

        return null;
    }

    private static Dictionary<DateTimeOffset, Wave5MarketFrame> BuildMarket(
        Dictionary<string, CausalIndicatorCache> caches,
        CausalIndicatorCache btc)
    {
        var btcAtr = btc.AtrPercent(14);
        var btcEma20 = btc.Ema(20);
        var btcEma50 = btc.Ema(50);
        var frames = new Dictionary<DateTimeOffset, Wave5MarketFrame>();
        var above = new Dictionary<DateTimeOffset, int>();
        var count = new Dictionary<DateTimeOffset, int>();
        foreach (var cache in caches.Values)
        {
            var ema = cache.Ema(20);
            var candles = cache.Candles;
            for (var i = 0; i < candles.Count; i++)
            {
                var t = candles[i].OpenTime;
                count[t] = count.GetValueOrDefault(t) + 1;
                if (ema[i] is { } e && candles[i].Close > e)
                {
                    above[t] = above.GetValueOrDefault(t) + 1;
                }
            }
        }

        var atrWindow = new Queue<double>();
        for (var i = 0; i < btc.Candles.Count; i++)
        {
            var t = btc.Candles[i].OpenTime;
            var atr = btcAtr[i] is { } a ? (double)a : double.NaN;
            if (!double.IsNaN(atr))
            {
                atrWindow.Enqueue(atr);
                if (atrWindow.Count > 50)
                {
                    atrWindow.Dequeue();
                }
            }

            var vol = 0;
            if (!double.IsNaN(atr) && atrWindow.Count >= 10)
            {
                var arr = atrWindow.ToArray();
                Array.Sort(arr);
                var rank = Array.BinarySearch(arr, atr);
                if (rank < 0)
                {
                    rank = ~rank;
                }

                var p = rank / (double)Math.Max(1, arr.Length - 1);
                vol = p < 1.0 / 3.0 ? -1 : p > 2.0 / 3.0 ? 1 : 0;
            }

            var btcTrend = 0;
            if (btcEma20[i] is { } f && btcEma50[i] is { } s)
            {
                btcTrend = f > s ? 1 : f < s ? -1 : 0;
            }

            var n = count.GetValueOrDefault(t);
            var br = n == 0 ? double.NaN : above.GetValueOrDefault(t) / (double)n;
            frames[t] = new Wave5MarketFrame(t, vol, btcTrend, br);
        }

        return frames;
    }
}
