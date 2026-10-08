using System.Globalization;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.News;

public static class NewsTradeDecisionEngine
{
    public static NewsMarketDecision Decide(
        NewsEvent item,
        NewsAssetContext asset,
        DateTimeOffset decisionTime,
        IReadOnlyDictionary<string, IReadOnlyList<MarketCandle>> candles,
        NewsMarketFacts facts,
        NewsOptions options,
        NewsDecisionContext? context = null)
    {
        context ??= new NewsDecisionContext();
        var symbol = string.IsNullOrWhiteSpace(asset.Symbol) ? "UNKNOWN" : asset.Symbol;
        if (string.Equals(item.AnalysisStatus, "NoCoin", StringComparison.OrdinalIgnoreCase))
        {
            return Stop(item, symbol, NewsRejection.UnknownAsset, string.IsNullOrWhiteSpace(item.AnalysisError)
                ? "This news is not about a Binance coin. It was not sent to the model."
                : item.AnalysisError);
        }

        if (options.Ai.Enabled && string.Equals(item.AnalysisStatus, "Skipped", StringComparison.OrdinalIgnoreCase))
        {
            return Stop(item, symbol, NewsRejection.NewsTooOld, string.IsNullOrWhiteSpace(item.AnalysisError)
                ? "This news was already stored. It was not sent to the model."
                : item.AnalysisError);
        }

        if (options.Ai.Enabled && !string.Equals(item.AnalysisStatus, "Ok", StringComparison.OrdinalIgnoreCase))
        {
            var failed = item.AnalysisStatus == "Invalid" ? NewsRejection.InvalidAiResponse : NewsRejection.AiAnalysisFailed;
            var why = string.IsNullOrWhiteSpace(item.AnalysisError)
                ? item.AnalysisStatus == "Timeout" ? "The model timed out." : "AI analysis failed."
                : item.AnalysisError;
            return Stop(item, symbol, failed, why);
        }

        if (item.ClassifiedAtUtc is { } classified
            && NewsLatency.HasLookAhead(item.PublishedAtUtc, item.DetectedAtUtc, classified, decisionTime))
        {
            return Stop(item, symbol, NewsRejection.LookAhead, "Timestamps are out of causal order.");
        }

        if (item.PublishedAtUtc > decisionTime)
        {
            return Stop(item, symbol, NewsRejection.LookAhead, "Future news is not visible at the decision time.");
        }

        if (context.DuplicateEvent)
        {
            return Stop(item, symbol, NewsRejection.DuplicateEvent, "This article is a copy of an event that was already analyzed.");
        }

        if (context.EventAlreadyTraded)
        {
            return Stop(item, symbol, NewsRejection.EventAlreadyTraded, "This event already has a decision for the coin.");
        }

        var age = (decisionTime - item.PublishedAtUtc).TotalMinutes;
        if (age > options.Strategy.MaxNewsAgeMinutes)
        {
            return Stop(item, symbol, NewsRejection.NewsTooOld, "The story is " + age.ToString("0", CultureInfo.InvariantCulture) + " minutes old.");
        }

        if (item.ExpectedHorizonMinutes > 0 && item.DetectedAtUtc > DateTimeOffset.MinValue
            && (decisionTime - item.DetectedAtUtc).TotalMinutes > item.ExpectedHorizonMinutes)
        {
            return Stop(item, symbol, NewsRejection.NewsTooOld, "The event is past its expected horizon.");
        }

        var link = item.AffectedAssets.FirstOrDefault(row =>
            string.Equals(row.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
            || string.Equals(row.BaseAsset, asset.BaseAsset, StringComparison.OrdinalIgnoreCase));
        if (link is null || string.IsNullOrWhiteSpace(link.Symbol))
        {
            return Stop(item, symbol, NewsRejection.UnknownAsset, "No Binance USD-M coin could be identified.");
        }

        if (string.Equals(link.Role, "MARKET_WIDE", StringComparison.OrdinalIgnoreCase))
        {
            return Stop(item, symbol, NewsRejection.NoClearDirection, "A market-wide effect is recorded and does not open an order by itself.");
        }

        if (!Confirmed(item.VerificationStatus) || item.SourceReliability < options.Ai.MinSourceReliability)
        {
            return Stop(item, symbol, NewsRejection.UnverifiedSource, "The source is not confirmed. Verification=" + item.VerificationStatus + ".");
        }

        if (item.ConfidenceScore < options.Strategy.MinNewsConfidence || link.Confidence < (int)Math.Round(options.Strategy.MinNewsConfidence * 100))
        {
            return Stop(item, symbol, NewsRejection.LowConfidence, "Confidence is below the minimum.");
        }

        if (item.ImpactScore < options.Strategy.MinNewsImpact)
        {
            return Stop(item, symbol, NewsRejection.LowImpact, "Impact is below the minimum.");
        }

        var direction = string.IsNullOrWhiteSpace(link.AssetDirection)
            ? item.Direction switch
            {
                EventDirection.Bullish => "LONG",
                EventDirection.Bearish => "SHORT",
                _ => "UNCERTAIN"
            }
            : link.AssetDirection;
        if (direction is not ("LONG" or "SHORT"))
        {
            return Stop(item, symbol, NewsRejection.NoClearDirection, "The event has no clear direction for this coin.");
        }

        if (string.Equals(link.Role, "SECONDARY", StringComparison.OrdinalIgnoreCase)
            && (link.Confidence < options.Ai.MinSecondaryConfidence || link.Impact < options.Ai.MinSecondaryImpact))
        {
            return Stop(item, symbol, NewsRejection.LowConfidence, "A secondary coin needs stronger evidence.");
        }

        if (!item.ShouldConsiderTrading)
        {
            return Stop(item, symbol, NewsRejection.NoClearDirection, "The analysis says this event should not be traded.");
        }

        if (item.AlreadyPricedIn >= options.Ai.MaxAlreadyPricedIn)
        {
            return Stop(item, symbol, NewsRejection.AlreadyPricedIn, "The analysis says the market has already priced the event.");
        }

        if (facts.Stale)
        {
            return Stop(item, symbol, NewsRejection.StaleMarketData, "The market data is stale.");
        }

        if (facts.MissingRequired || facts.Price <= 0m || facts.AtrPercent is null || facts.SpreadPercent is null
            || facts.QuoteVolume24h is null || facts.OpenInterest is null || facts.OpenInterestChange is null || facts.FundingRate is null)
        {
            return Stop(item, symbol, NewsRejection.InsufficientMarketData, "Required market data is missing.");
        }

        var bullish = direction == "LONG";
        var favorable = FavorableMove(facts, bullish);
        if (favorable >= options.Ai.MaxFavorableMovePercent
            || (favorable >= 1.5 && facts.OpenInterestChange < 0 && facts.RelativeVolume is >= 1.5))
        {
            return Stop(item, symbol, NewsRejection.AlreadyPricedIn, "Price has already moved " + favorable.ToString("0.00", CultureInfo.InvariantCulture) + "% with the news.");
        }

        if (facts.SpreadPercent > (decimal)options.Ai.MaxSpreadPercent)
        {
            return Stop(item, symbol, NewsRejection.WideSpread, "The spread is too wide.");
        }

        if (facts.QuoteVolume24h < options.Ai.MinQuoteVolumeUsdt)
        {
            return Stop(item, symbol, NewsRejection.LowLiquidity, "24h quote volume is below the minimum.");
        }

        if (!string.Equals(symbol, "BTCUSDT", StringComparison.OrdinalIgnoreCase) && facts.EvaluateBtc)
        {
            if (facts.BtcReturn15m is null || facts.BtcReturn1h is null)
            {
                return Stop(item, symbol, NewsRejection.InsufficientMarketData, "BTC market data is missing.");
            }

            var btcAgainst = bullish ? facts.BtcReturn1h <= -1.5 || facts.BtcReturn15m <= -1.0 : facts.BtcReturn1h >= 1.5 || facts.BtcReturn15m >= 1.0;
            var exceptional = item.ConfidenceScore >= 0.90 && item.ImpactScore >= 0.90 && item.SourceReliability >= 85 && Confirmed(item.VerificationStatus);
            if (btcAgainst && !exceptional)
            {
                return Stop(item, symbol, NewsRejection.MarketConflict, "BTC is moving against this altcoin.");
            }
        }

        var closed = NewsMarketFactsBuilder.Closed(candles, decisionTime);
        var originalDirection = item.Direction;
        item.Direction = bullish ? EventDirection.Bullish : EventDirection.Bearish;
        NewsMarketDecision confirmed;
        try
        {
            confirmed = NewsMarketConfirmation.Evaluate(item, asset, decisionTime, closed, options);
        }
        finally
        {
            item.Direction = originalDirection;
        }
        if (confirmed.Signal == NewsMarketSignals.NoTrade)
        {
            return confirmed with
            {
                RejectionCode = NewsRejection.FromConfirmation(confirmed.Reason),
                EventId = item.EventId
            };
        }

        var expected = facts.AtrPercent.Value * item.ImpactScore * (1d - Math.Clamp(item.AlreadyPricedIn / 100d, 0d, 1d)) * Math.Max(item.NoveltyScore, 0.25) * 2d;
        var cost = RoundTripPercent(facts, options.Ai, item.ExpectedHorizonMinutes, bullish);
        if (expected - cost < options.Ai.MinExpectedEdgePercent)
        {
            return confirmed with
            {
                Signal = NewsMarketSignals.NoTrade,
                Record = null,
                RejectionCode = NewsRejection.ExpectedEdgeTooSmall,
                EventId = item.EventId,
                Reason = confirmed.Reason + "\nREJECTED:\n" + NewsRejection.ExpectedEdgeTooSmall
                    + " expected " + expected.ToString("0.00", CultureInfo.InvariantCulture)
                    + "% against cost " + cost.ToString("0.00", CultureInfo.InvariantCulture) + "%."
            };
        }

        if (context.SymbolOccupied)
        {
            return Stop(item, symbol, NewsRejection.ExistingPosition, "This coin already has a position or an open order.");
        }

        if (context.InCooldown)
        {
            return Stop(item, symbol, NewsRejection.Cooldown, "This coin is in the news cooldown.");
        }

        return confirmed with { EventId = item.EventId };
    }

    public static double RoundTripPercent(NewsMarketFacts facts, NewsAiOptions ai, int horizonMinutes, bool bullish)
    {
        var spread = (double)(facts.SpreadPercent ?? 0m);
        var fees = 2d * (ai.TakerFeePercent + ai.SlippagePercent);
        var funding = 0d;
        if (facts.FundingRate is { } rate)
        {
            var against = bullish ? rate > 0m : rate < 0m;
            if (against)
            {
                var hours = Math.Max(1, horizonMinutes) / 60d;
                funding = (double)Math.Abs(rate) * 100d * (hours / 8d);
            }
        }

        return fees + spread + funding;
    }

    public static double FavorableMove(NewsMarketFacts facts, bool bullish)
    {
        var best = 0d;
        foreach (var value in new[] { facts.ReturnSinceDetection, facts.Return1m, facts.Return5m, facts.Return15m, facts.Return1h })
        {
            if (value is not { } move)
            {
                continue;
            }

            var aligned = bullish ? move : -move;
            if (aligned > best)
            {
                best = aligned;
            }
        }

        return best;
    }

    private static bool Confirmed(string status) =>
        status is "confirmed" or "official" or "verified";

    private static NewsMarketDecision Stop(NewsEvent item, string symbol, string code, string reason) =>
        new(
            NewsMarketSignals.NoTrade,
            symbol,
            reason,
            0,
            0,
            0,
            new StrategySignalDetail(SignalType.NoAction, reason, item.PublishedAtUtc, Status: "RESEARCHING"),
            null,
            code,
            item.EventId);
}
