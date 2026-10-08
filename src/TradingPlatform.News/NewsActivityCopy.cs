namespace TradingPlatform.News;

/// <summary>The model is paid per call. A story still inside the age limit is sent until an analysis succeeds. An earlier ingest tick does not count as analysis.</summary>
public static class NewsAiGate
{
    public static bool ShouldAnalyze(DateTimeOffset publishedAt, DateTimeOffset now, int maxAgeMinutes, bool alreadyAnalyzed)
    {
        if (alreadyAnalyzed)
        {
            return false;
        }

        return maxAgeMinutes <= 0 || (now - publishedAt).TotalMinutes <= maxAgeMinutes;
    }
}

/// <summary>Plain sentences for the news desk. The page should say what happened and whether an order was sent.</summary>
public static class NewsActivityCopy
{
    public static string Why(string verdict, string? rejection, string? storedReason, string orderState)
    {
        if (orderState == "Filled")
        {
            return verdict == "SHORT"
                ? "A short order was filled on Binance."
                : "A long order was filled on Binance.";
        }

        if (orderState == "Sent")
        {
            return verdict == "SHORT"
                ? "A short order was sent to Binance."
                : "A long order was sent to Binance.";
        }

        if (orderState == "Closed")
        {
            return "The order was sent, and that position is already closed.";
        }

        if (orderState == "Rejected")
        {
            return Known(rejection) ?? Humanize(storedReason) ?? "Risk blocked the order. Nothing was sent.";
        }

        return Known(rejection) ?? Humanize(storedReason) ?? "No order was sent.";
    }

    public static string OrderState(bool becameTrade, string? exchangeOrderId, DateTimeOffset? filledAt, DateTimeOffset? closedAt, string? riskDecision, string? orderDecision)
    {
        if (closedAt is not null)
        {
            return "Closed";
        }

        if (filledAt is not null)
        {
            return "Filled";
        }

        if (becameTrade || !string.IsNullOrWhiteSpace(exchangeOrderId))
        {
            return "Sent";
        }

        if (string.Equals(riskDecision, "Rejected", StringComparison.OrdinalIgnoreCase)
            || string.Equals(orderDecision, "REJECTED", StringComparison.OrdinalIgnoreCase))
        {
            return "Rejected";
        }

        return "NotSent";
    }

    public static string? OrderLine(string orderState, decimal entry, decimal quantity, decimal stop, decimal target, string? exchangeOrderId)
    {
        if (orderState is "NotSent" or "Rejected" || quantity <= 0m)
        {
            return null;
        }

        var line = "Entry " + Trim(entry) + " · size " + Trim(quantity) + " · stop " + Trim(stop) + " · target " + Trim(target);
        return string.IsNullOrWhiteSpace(exchangeOrderId) ? line : line + " · Binance " + exchangeOrderId.Trim();
    }

    public static int? Percent(double? value)
    {
        if (value is not { } number || number <= 0)
        {
            return null;
        }

        var scaled = number <= 1d ? number * 100d : number;
        return (int)Math.Round(Math.Clamp(scaled, 0d, 100d));
    }

    public static string? Known(string? rejection) => (rejection ?? string.Empty).Trim() switch
    {
        NewsRejection.NewsTooOld => "The news is too old. No order was sent.",
        NewsRejection.LowConfidence => "Confidence is too low. No order was sent.",
        NewsRejection.UnverifiedSource => "The source is not reliable enough. No order was sent.",
        NewsRejection.DuplicateEvent => "This is a duplicate of news we already saw. No second order was sent.",
        NewsRejection.AlreadyPricedIn => "The market has already moved on this news. No order was sent.",
        NewsRejection.MarketConflict => "The live market disagrees with the news. No order was sent.",
        NewsRejection.InsufficientMarketData => "Market data was missing. No order was sent.",
        NewsRejection.StaleMarketData => "Market data was stale. No order was sent.",
        NewsRejection.LowLiquidity => "There is not enough liquidity. No order was sent.",
        NewsRejection.WideSpread => "The spread is too wide. No order was sent.",
        NewsRejection.ExpectedEdgeTooSmall => "The expected move is smaller than fees and slippage. No order was sent.",
        NewsRejection.RiskLimit => "The risk engine blocked the order. Nothing was sent.",
        NewsRejection.ExistingPosition => "This coin already has an open position. No new order was sent.",
        NewsRejection.Cooldown => "This coin is in a cooldown. No order was sent.",
        NewsRejection.UnknownAsset => "This news is not about a Binance coin. No order was sent.",
        NewsRejection.AiAnalysisFailed => "The model did not finish the analysis. No order was sent.",
        NewsRejection.InvalidAiResponse => "The model reply was not usable. No order was sent.",
        NewsRejection.NoClearDirection => "The direction is not clear. No order was sent.",
        NewsRejection.EventAlreadyTraded => "This news was already traded. No second order was sent.",
        NewsRejection.LookAhead => "The timestamps were inconsistent. No order was sent.",
        NewsRejection.LowImpact => "The news is not important enough. No order was sent.",
        _ => null
    };

    public static string? Humanize(string? storedReason)
    {
        var text = NewsStop.Brief(storedReason);
        if (text == "No trade.")
        {
            return null;
        }

        var age = System.Text.RegularExpressions.Regex.Match(text, @"age:\s*(\d+)m exceeds (\d+)m");
        if (age.Success)
        {
            return "The news is " + age.Groups[1].Value + " minutes old. Trades stop after " + age.Groups[2].Value + " minutes, so no order was sent.";
        }

        if (text.StartsWith("Stopped at confidence", StringComparison.Ordinal))
        {
            return "Confidence is below the minimum. No order was sent.";
        }

        if (text.StartsWith("Stopped at impact", StringComparison.Ordinal))
        {
            return "Impact is below the minimum. No order was sent.";
        }

        if (text.StartsWith("Stopped at direction", StringComparison.Ordinal))
        {
            return "The direction is not long or short. No order was sent.";
        }

        if (text.StartsWith("Stopped at coin", StringComparison.Ordinal))
        {
            return "No coin was identified. No order was sent.";
        }

        if (text.StartsWith("Stopped before scoring", StringComparison.Ordinal))
        {
            return "The article was not tied to a news event. No order was sent.";
        }

        if (text.StartsWith("Stopped before the market", StringComparison.Ordinal))
        {
            return "The market was not checked yet. No order was sent.";
        }

        if (!text.Contains("order", StringComparison.OrdinalIgnoreCase))
        {
            return text.TrimEnd('.') + ". No order was sent.";
        }

        return text;
    }

    private static string Trim(decimal value)
    {
        var text = value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture);
        return text.Contains('.') ? text.TrimEnd('0').TrimEnd('.') : text;
    }
}
