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
    decimal EntryPrice);

/// <summary>
/// 150% in the last 24 hours, no public catalyst, and the price has stopped printing the high.
/// The short then follows the drop. It has no target under the high.
/// </summary>
public static class UnexplainedPumpShort
{
    public static PumpMove? Measure(
        decimal high24h,
        decimal lastPrice,
        decimal priceChangePercent,
        decimal minRise)
    {
        if (lastPrice <= 0m || high24h <= 0m || priceChangePercent <= -99.9m || minRise <= 0m)
        {
            return null;
        }

        var base24 = lastPrice / (1m + priceChangePercent / 100m);
        if (base24 <= 0m)
        {
            return null;
        }

        var oneDay = high24h / base24 - 1m;
        if (oneDay < minRise)
        {
            return null;
        }

        return new PumpMove(oneDay, 0m, oneDay, 1, high24h);
    }

    public static bool Stalled(
        decimal lastPrice,
        decimal pumpHigh,
        IReadOnlyList<MarketCandle> closedEntryBars,
        decimal pullback)
    {
        if (lastPrice <= 0m || pumpHigh <= 0m || pullback < 0m || closedEntryBars.Count == 0)
        {
            return false;
        }

        if (lastPrice > pumpHigh * (1m - pullback))
        {
            return false;
        }

        var lastBar = closedEntryBars.OrderBy(candle => candle.OpenTime).Last();
        return lastBar.High < pumpHigh;
    }

    /// <summary>
    /// Where the protective buy sits. <see cref="PumpFollow.Following"/> is true once the drop has pulled
    /// that price under the original disaster stop. Until then the disaster stop stays, so the first squeeze
    /// cannot knock the short out.
    /// </summary>
    public readonly record struct PumpFollow(decimal Stop, bool Following);

    /// <summary>
    /// The short has no target under the pump high. The stop trails the lowest mark and only moves down.
    /// A drop of <paramref name="tightenAfterPercent"/> or more switches from the wide trail to the tight one,
    /// so a deep winner cannot be given all the way back. The result never sits above <paramref name="disasterStop"/>.
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

    /// <summary>The dump has ended when the mark trades up through a live follow stop.</summary>
    public static string? BounceExit(PumpFollow follow, decimal mark)
    {
        if (!follow.Following || follow.Stop <= 0m || mark < follow.Stop)
        {
            return null;
        }

        return "The drop stalled. Closed on the bounce.";
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

    /// <summary>The 15-minute bar that printed <paramref name="lowAt"/> has finished, so the follow stop may move.</summary>
    public static bool BarHasClosed(DateTimeOffset lowAt, DateTimeOffset now) =>
        QuarterHour(now) > QuarterHour(lowAt);

    public static DateTimeOffset QuarterHour(DateTimeOffset time)
    {
        var utc = time.ToUniversalTime();
        var minute = utc.Minute - (utc.Minute % 15);
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute, 0, TimeSpan.Zero);
    }

    public static UnexplainedPumpDecision Decide(
        string symbol,
        PumpMove? move,
        bool stalled,
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

        if (!stalled)
        {
            return None(symbol, news.Reason + " The high is still being printed, so there is no short yet.", move, lastPrice);
        }

        var riseText = (move.Rise * 100m).ToString("0", CultureInfo.InvariantCulture);
        return new UnexplainedPumpDecision(
            UnexplainedPumpSignals.Short,
            symbol,
            riseText + "% in the last 24 hours. " + news.Reason + " Price is off the high. SHORT.",
            move.Rise,
            move.DaySpan,
            move.PumpHigh,
            lastPrice);
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

    /// <summary>
    /// One short per coin per day. A single entry that was closed from outside this book may be sent once more.
    /// </summary>
    public static bool MaySendToday(int openToday, int closedByThisBook, int closedFromOutside, out int retry)
    {
        retry = closedFromOutside;
        return openToday == 0 && closedByThisBook == 0 && closedFromOutside <= 1;
    }

    /// <summary>10x Isolated leaves about 9% to liquidation. A wider exchange stop is rejected.</summary>
    public const decimal MaxStopPercent = 7.5m;
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
