using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TradingPlatform.Domain.Market;

namespace TradingPlatform.News;

public static class UnexplainedPumpSignals
{
    public const string Short = "SHORT";
    public const string NoTrade = "NO_TRADE";
}

public sealed record PumpMove(
    decimal OneDayRise,
    decimal TwoDayRise,
    decimal Rise,
    int DaySpan,
    decimal PumpHigh);

public sealed record PumpHeadline(string Title, string Body, string Source, DateTimeOffset PublishedAt);

public sealed record PumpNewsBundle(bool InternetOk, IReadOnlyList<PumpHeadline> Headlines, string Failure)
{
    public static PumpNewsBundle Failed(string reason) => new(false, [], reason);

    public static PumpNewsBundle Ready(IReadOnlyList<PumpHeadline> headlines) => new(true, headlines, string.Empty);
}

public sealed record PumpNewsVerdict(bool Searched, bool CatalystFound, string Reason, string? Headline = null)
{
    public static PumpNewsVerdict Incomplete(string reason) => new(false, false, reason);

    public static PumpNewsVerdict Clear(string reason) => new(true, false, reason);

    public static PumpNewsVerdict Catalyst(string headline) => new(true, true, "Catalyst: " + headline, headline);
}

public sealed record UnexplainedPumpDecision(
    string Signal,
    string Symbol,
    string Reason,
    decimal Rise,
    int DaySpan,
    decimal PumpHigh,
    decimal EntryPrice,
    decimal BreakHigh = 0m);

/// <summary>
/// The reversal is confirmed when the high's 15-minute bar has closed and price has settled
/// between 30% and 60% under that high. The protective buy is 7% above the fill, not the break bar.
/// </summary>
public readonly record struct DumpBreak(bool Confirmed, decimal BreakHigh, string Reason)
{
    public static DumpBreak Ready(decimal breakHigh) => new(true, breakHigh, string.Empty);

    public static DumpBreak Wait(string reason) => new(false, 0m, reason);
}

/// <summary>
/// 125% in the last 24 hours and no public catalyst. The short opens once price and a closed 15-minute bar
/// are 30% to 60% off that high; nearer the high most pumps print a new high first. The follow stop then uses the low
/// of the last closed 15-minute bar until the drop is 20% under the entry, and the last closed 5-minute bar after that.
/// Profit is taken when price turns up through that trail, and the short is closed after 48 hours either way.
/// After a win the coin waits until that 24-hour rise falls back under the pump line. After a loss the same pump may be
/// shorted again <see cref="MaxLossReentries"/> more times, as soon as the setup is back. A stop in the first 15 minutes,
/// before price has gone in favor, leaves the high open for one more short.
/// </summary>
public static class UnexplainedPumpShort
{
    public static bool TryOneDayRise(decimal high24h, decimal lastPrice, decimal priceChangePercent, out decimal rise)
    {
        rise = 0m;
        if (lastPrice <= 0m || high24h <= 0m || priceChangePercent <= -99.9m)
        {
            return false;
        }

        var base24 = lastPrice / (1m + priceChangePercent / 100m);
        if (base24 <= 0m)
        {
            return false;
        }

        rise = high24h / base24 - 1m;
        return true;
    }

    public static PumpMove? Measure(
        decimal high24h,
        decimal lastPrice,
        decimal priceChangePercent,
        decimal minRise)
    {
        if (minRise <= 0m || !TryOneDayRise(high24h, lastPrice, priceChangePercent, out var oneDay) || oneDay < minRise)
        {
            return null;
        }

        return new PumpMove(oneDay, 0m, oneDay, 1, high24h);
    }

    /// <summary>The 24-hour rise has printed back under the pump line. A missing price is not a cool-down.</summary>
    public static bool RiseHasEnded(decimal high24h, decimal lastPrice, decimal priceChangePercent, decimal minRise) =>
        minRise > 0m && TryOneDayRise(high24h, lastPrice, priceChangePercent, out var rise) && rise < minRise;

    /// <summary>
    /// The turn has started when the high's 15-minute bar is closed and both the last price and that bar's
    /// close sit between <paramref name="minDrop"/> and <paramref name="maxDrop"/> under the high.
    /// Closer than the minimum is still the pump. Farther than the maximum is a drop that already ran.
    /// </summary>
    public static DumpBreak Reversal(
        decimal pumpHigh,
        decimal lastPrice,
        IReadOnlyList<MarketCandle> closedBars,
        decimal minDrop,
        decimal maxDrop)
    {
        if (pumpHigh <= 0m || lastPrice <= 0m || minDrop <= 0m || maxDrop <= minDrop || closedBars.Count == 0)
        {
            return DumpBreak.Wait("There is no closed bar to confirm the turn.");
        }

        var ordered = closedBars.Where(bar => bar.High > 0m && bar.Close > 0m).OrderBy(bar => bar.OpenTime).ToList();
        if (ordered.Count == 0)
        {
            return DumpBreak.Wait("There is no closed bar to confirm the turn.");
        }

        var spike = ordered[0];
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].High >= spike.High)
            {
                spike = ordered[i];
            }
        }

        if (pumpHigh > spike.High * 1.0005m)
        {
            return DumpBreak.Wait("The high bar is still open.");
        }

        var drop = (pumpHigh - lastPrice) / pumpHigh;
        if (drop < minDrop)
        {
            return DumpBreak.Wait("Price is still within " + Percent(minDrop) + " of the high.");
        }

        if (drop > maxDrop)
        {
            return DumpBreak.Wait("Price is already " + Percent(drop) + " under the high. The drop has run.");
        }

        var closedDrop = (pumpHigh - ordered[^1].Close) / pumpHigh;
        if (closedDrop < minDrop)
        {
            return DumpBreak.Wait("The last closed bar has not confirmed the turn.");
        }

        if (closedDrop > maxDrop)
        {
            return DumpBreak.Wait("The last closed bar is already " + Percent(closedDrop) + " under the high.");
        }

        return DumpBreak.Ready(0m);
    }

    /// <summary>
    /// Where the protective buy sits. <see cref="PumpFollow.Following"/> is true once the stop has moved
    /// under the original disaster stop. A shallow dip leaves that disaster stop in place.
    /// </summary>
    public readonly record struct PumpFollow(decimal Stop, bool Following);

    /// <summary>Shown when the trail is hit. The drop gave back a real bounce off its low.</summary>
    public const string TurnedUp = "The drop turned up. Profit taken.";

    /// <summary>
    /// The short follows the lowest mark and the stop only moves down. Until the trail sits under the
    /// disaster stop, that disaster stop stays, so a bounce back toward the entry does not take the profit.
    /// A drop of <paramref name="tightenAfterPercent"/> or more switches to the tight trail, which is how
    /// far price may rise off the low before the drop counts as over.
    /// </summary>
    public static PumpFollow Follow(
        decimal entry,
        decimal bestMark,
        decimal disasterStop,
        decimal widePercent,
        decimal tightPercent,
        decimal tightenAfterPercent)
    {
        if (entry <= 0m || bestMark <= 0m || widePercent <= 0m)
        {
            return new PumpFollow(disasterStop, false);
        }

        var drop = (entry - bestMark) / entry * 100m;
        var trail = tightPercent > 0m && drop >= tightenAfterPercent ? tightPercent : widePercent;
        var line = bestMark * (1m + trail / 100m);
        if (disasterStop > 0m && line >= disasterStop)
        {
            return new PumpFollow(disasterStop, false);
        }

        return new PumpFollow(line, true);
    }

    /// <summary>The drop has turned up when the mark trades through a live follow stop.</summary>
    public static string? BounceExit(PumpFollow follow, decimal mark)
    {
        if (!follow.Following || follow.Stop <= 0m || mark < follow.Stop)
        {
            return null;
        }

        return TurnedUp;
    }

    /// <summary>The hold clock retires once the low is this far under the entry. A working dump is not closed on time.</summary>
    public static bool DumpHasStarted(decimal entry, decimal bestMark, decimal startedPercent)
    {
        if (entry <= 0m || bestMark <= 0m || startedPercent <= 0m)
        {
            return false;
        }

        return bestMark <= entry * (1m - startedPercent / 100m);
    }

    /// <summary>Fast follow clock, used once the drop from entry has reached <see cref="UnexplainedPumpOptions.FastTrailAfterPercent"/>.</summary>
    public static readonly TimeSpan TrailBar = TimeSpan.FromMinutes(5);

    /// <summary>Slow follow clock. A shallow dip keeps the disaster stop until this bar closes.</summary>
    public static readonly TimeSpan SlowTrailBar = TimeSpan.FromMinutes(15);

    /// <summary>
    /// A dip that is still under <paramref name="fastAfterPercent"/> is confirmed on the 15-minute bar.
    /// A deeper drop is confirmed on the 5-minute bar, so the turn up is locked inside a few minutes.
    /// </summary>
    public static TimeSpan TrailBarFor(decimal entry, decimal bestMark, decimal fastAfterPercent)
    {
        if (entry <= 0m || bestMark <= 0m || bestMark >= entry || fastAfterPercent <= 0m)
        {
            return SlowTrailBar;
        }

        var drop = (entry - bestMark) / entry * 100m;
        return drop >= fastAfterPercent ? TrailBar : SlowTrailBar;
    }

    /// <summary>The bar that printed <paramref name="lowAt"/> has finished, so the follow stop may move.</summary>
    public static bool BarHasClosed(DateTimeOffset lowAt, DateTimeOffset now, TimeSpan bar) =>
        BarOpen(now, bar) > BarOpen(lowAt, bar);

    public static DateTimeOffset BarOpen(DateTimeOffset time, TimeSpan bar)
    {
        var utc = time.ToUniversalTime();
        var minutes = (int)bar.TotalMinutes;
        if (minutes <= 0)
        {
            minutes = TrailBar.Minutes;
        }

        var minute = utc.Minute - (utc.Minute % minutes);
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, TimeSpan.Zero);
    }

    /// <summary>
    /// The trail low is the best mark whose bar has closed. A shallow low waits for the 15-minute bar.
    /// Once the drop is deep enough, it waits for the 5-minute bar. A lower print in the bar that is
    /// still open does not replace it, so the stop from the previous bar stays up and can lock the turn.
    /// </summary>
    public static bool PromoteClosedLow(
        decimal entry,
        decimal bestMark,
        DateTimeOffset bestAt,
        decimal closedMark,
        DateTimeOffset now,
        decimal fastAfterPercent,
        out decimal trailMark)
    {
        trailMark = closedMark;
        if (bestMark <= 0m || bestAt == default || !BarHasClosed(bestAt, now, TrailBarFor(entry, bestMark, fastAfterPercent)))
        {
            return false;
        }

        if (closedMark > 0m && bestMark >= closedMark)
        {
            return false;
        }

        trailMark = bestMark;
        return true;
    }

    /// <summary>
    /// Price is already through the confirmed trail, and the resting stop is still higher.
    /// The position is closed at the market instead of waiting for that older stop.
    /// </summary>
    public static bool TrailOverrun(PumpFollow follow, decimal mark, decimal restingStop)
    {
        if (!follow.Following || follow.Stop <= 0m || mark < follow.Stop)
        {
            return false;
        }

        return restingStop <= 0m || follow.Stop < restingStop;
    }

    public static UnexplainedPumpDecision Decide(
        string symbol,
        PumpMove? move,
        bool stalled,
        PumpNewsVerdict news,
        decimal lastPrice,
        decimal quoteVolume,
        UnexplainedPumpOptions options) =>
        Decide(
            symbol,
            move,
            stalled
                ? DumpBreak.Ready(0m)
                : DumpBreak.Wait("The high is still being printed, so there is no short yet."),
            news,
            lastPrice,
            quoteVolume,
            options);

    public static UnexplainedPumpDecision Decide(
        string symbol,
        PumpMove? move,
        DumpBreak dump,
        PumpNewsVerdict news,
        decimal lastPrice,
        decimal quoteVolume,
        UnexplainedPumpOptions options)
    {
        if (move is null)
        {
            return None(symbol, "Rise is below " + Percent(options.MinRise) + " in the last 24 hours.");
        }

        if (quoteVolume < options.MinQuoteVolume)
        {
            return None(symbol, "24h turnover " + quoteVolume.ToString("0", CultureInfo.InvariantCulture) + " is below " + options.MinQuoteVolume.ToString("0", CultureInfo.InvariantCulture) + ".", move, lastPrice);
        }

        if (!news.Searched)
        {
            return None(symbol, news.Reason, move, lastPrice);
        }

        if (news.CatalystFound)
        {
            return None(symbol, news.Reason, move, lastPrice);
        }

        if (!dump.Confirmed)
        {
            return None(symbol, news.Reason + " " + dump.Reason, move, lastPrice);
        }

        var riseText = (move.Rise * 100m).ToString("0", CultureInfo.InvariantCulture);
        var broke = "The turn off the high has closed. ";
        return new UnexplainedPumpDecision(
            UnexplainedPumpSignals.Short,
            symbol,
            riseText + "% in the last 24 hours. " + news.Reason + " " + broke + "SHORT.",
            move.Rise,
            move.DaySpan,
            move.PumpHigh,
            lastPrice,
            dump.BreakHigh);
    }

    public static string ClientOrderId(string symbol, DateTimeOffset day, int retry = 0)
    {
        var key = "pump|" + symbol.ToUpperInvariant() + "|" + day.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (retry > 0)
        {
            key += "|r" + retry.ToString(CultureInfo.InvariantCulture);
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return "pump-" + Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    /// <summary>A later pump is a new trade only when its high clears the last shorted high by this much.</summary>
    public const decimal NewPumpHighPercent = 10m;

    /// <summary>
    /// After a loss, the same pump may be shorted this many more times before the 24-hour rise cools.
    /// Shorting again after a win lost money, so a win still waits for the cool-down.
    /// </summary>
    public const int MaxLossReentries = 2;

    /// <summary>A stop this soon, with less than this much favorable movement, never started the drop.</summary>
    public const decimal WickFavorablePercent = 1m;

    public static readonly TimeSpan WickWindow = TimeSpan.FromMinutes(15);

    /// <summary>
    /// The first short on a coin is allowed. The same high, or a high only a little above it, is finished
    /// after one attempt. An open short blocks a second order.
    /// </summary>
    public static bool MayShortThisHigh(bool positionOpen, decimal pumpHigh, decimal? previousHigh)
    {
        if (positionOpen || pumpHigh <= 0m)
        {
            return false;
        }

        if (previousHigh is null || previousHigh <= 0m)
        {
            return true;
        }

        return pumpHigh >= previousHigh.Value * (1m + NewPumpHighPercent / 100m);
    }

    /// <summary>
    /// True when the short was stopped before the drop started. <paramref name="bestMark"/> of zero means no lower price was seen.
    /// </summary>
    public static bool WickStop(decimal entry, decimal bestMark, TimeSpan held)
    {
        if (entry <= 0m || held < TimeSpan.Zero || held > WickWindow)
        {
            return false;
        }

        if (bestMark <= 0m)
        {
            return true;
        }

        return (entry - bestMark) / entry * 100m < WickFavorablePercent;
    }

    /// <summary>
    /// A losing short may be followed in the same pump, before the rise cools, until
    /// <see cref="MaxLossReentries"/> such shorts have been taken.
    /// </summary>
    public static bool LossReentry(bool cooled, bool wickRetryOpen, bool lost, int lossReentries) =>
        !wickRetryOpen && !cooled && lost && lossReentries < MaxLossReentries;

    /// <summary>
    /// Null when a new short may open. A won short blocks the coin until the 24-hour rise has ended,
    /// and the next high then has to clear it by <see cref="NewPumpHighPercent"/>. After a loss the pump can be
    /// shorted again right away, <see cref="MaxLossReentries"/> times. A wick leaves
    /// <paramref name="wickRetryOpen"/> set, and that high is tried once more.
    /// <paramref name="consumedHigh"/> is the last high that was actually traded, not the wick.
    /// <paramref name="lossReentries"/> counts the shorts already taken after a loss in this pump.
    /// </summary>
    public static string? ReentryBlock(
        bool positionOpen,
        decimal pumpHigh,
        decimal? consumedHigh,
        bool cooled,
        bool wickRetryOpen,
        bool lost = false,
        int lossReentries = 0)
    {
        if (positionOpen)
        {
            return "This coin already has an open pump short.";
        }

        if (wickRetryOpen || consumedHigh is not > 0m)
        {
            return null;
        }

        if (!cooled)
        {
            if (LossReentry(cooled, wickRetryOpen, lost, lossReentries))
            {
                return null;
            }

            return lost
                ? "This pump already lost " + (lossReentries + 1).ToString(CultureInfo.InvariantCulture) + " shorts. It waits for the rise to cool."
                : "This pump is still running.";
        }

        return MayShortThisHigh(false, pumpHigh, consumedHigh)
            ? null
            : "This pump was already shorted.";
    }

    /// <summary>10x Isolated leaves about 9% to liquidation. A wider exchange stop is rejected.</summary>
    public const decimal MaxStopPercent = 7m;
    public const decimal OrderLeverage = 10m;
    public const decimal MaxEntryMarginUsdt = 20m;
    public const decimal RiskPerTradePercent = 8m;
    public const decimal MaxPortfolioRiskPercent = 40m;

    public static decimal StopPercent(decimal entry, decimal pumpHigh, decimal buffer)
    {
        if (entry <= 0m || pumpHigh <= 0m)
        {
            return 0m;
        }

        var distance = (pumpHigh * (1m + buffer) / entry - 1m) * 100m;
        return distance <= 0m ? 0m : Math.Round(distance, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Exchange stop. The 50% band above the high stays the software exit. This cap is what 10x can hold.</summary>
    public static decimal OrderStopPercent(decimal entry, decimal pumpHigh, decimal buffer)
    {
        var distance = StopPercent(entry, pumpHigh, buffer);
        return distance <= 0m ? 0m : Math.Min(distance, MaxStopPercent);
    }

    /// <summary>Buy stop 7% above the fill. Measured from the fill so a fast print cannot open the short through its stop.</summary>
    public static decimal DisasterStopPrice(decimal fill) =>
        fill <= 0m ? 0m : fill * (1m + MaxStopPercent / 100m);

    private static UnexplainedPumpDecision None(string symbol, string reason, PumpMove? move = null, decimal entry = 0m) =>
        new(UnexplainedPumpSignals.NoTrade, symbol, reason, move?.Rise ?? 0m, move?.DaySpan ?? 0, move?.PumpHigh ?? 0m, entry);

    private static string Percent(decimal rise) =>
        (rise * 100m).ToString("0", CultureInfo.InvariantCulture) + "%";
}

public static class PumpCatalyst
{
    public static bool Mentions(string text, string baseAsset, string symbol)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var upper = text.ToUpperInvariant();
        if (HasToken(upper, symbol.ToUpperInvariant()))
        {
            return true;
        }

        var token = baseAsset.ToUpperInvariant();
        return token.Length >= 3 && HasToken(upper, token);
    }

    private static bool HasToken(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            var before = index == 0 || !char.IsLetterOrDigit(text[index - 1]);
            var after = index + token.Length >= text.Length || !char.IsLetterOrDigit(text[index + token.Length]);
            if (before && after)
            {
                return true;
            }

            index += token.Length;
        }

        return false;
    }
}
