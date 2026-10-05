using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Trading;
using TradingPlatform.News;
using TradingPlatform.Risk;

namespace TradingPlatform.Trading;

public sealed record NewsRiskHandoff(
    string Direction,
    string RiskDecision,
    string RiskReason,
    decimal Quantity,
    decimal Notional,
    decimal Leverage,
    decimal Margin,
    decimal StopLossPrice,
    decimal TakeProfitPrice,
    string OrderDecision,
    PlaceOrderRequest? Request);

public static class NewsTradeAdapter
{
    public const string StrategyName = "news_market_confirmation";

    public static string Decision(string candidate) => candidate switch
    {
        NewsMarketSignals.LongCandidate => "LONG",
        NewsMarketSignals.ShortCandidate => "SHORT",
        _ => "NO_TRADE"
    };

    /// <summary>
    /// Equity and free margin for news sizing, read from the live futures book. A book that is missing,
    /// not futures-authoritative, or older than <paramref name="maxAge"/> gives a block reason instead of a number.
    /// </summary>
    public static (decimal Equity, decimal Available, string? Block) LiveEquity(LiveAccountSnapshot live, DateTimeOffset utcNow, TimeSpan maxAge)
    {
        if (!live.HasKeys || !live.FuturesBookFresh || live.UpdatedAt is not { } at || utcNow - at > maxAge)
        {
            return (0m, 0m, "The live Binance futures balance is not fresh. News sizing needs it. No order.");
        }

        var available = live.UsdtFree ?? live.FuturesUsdt;
        var equity = live.FuturesEquity > 0m ? live.FuturesEquity : available + live.OpenPositions.Sum(p => p.UnrealizedPnL);
        return equity > 0m && available > 0m
            ? (equity, available, null)
            : (0m, 0m, "The live Binance futures balance is zero. No order.");
    }

    /// <summary>Same event and coin always give the same Binance client id, so a retried cycle cannot double-submit.</summary>
    public static string ClientOrderId(string eventId, string symbol)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{eventId}|{symbol.ToUpperInvariant()}"));
        return "news-" + Convert.ToHexString(hash)[..20].ToLowerInvariant();
    }

    public static NewsRiskHandoff Rejected(string direction, string reason) =>
        new(direction, "Rejected", reason, 0, 0, 0, 0, 0, 0, "REJECTED", null);

    public static NewsRiskHandoff Handoff(
        string direction,
        decimal price,
        decimal tickSize,
        RiskProfile profile,
        RiskSnapshot snapshot,
        IRiskEngine risk,
        DateTimeOffset utcNow,
        bool liveTradingEnabled,
        bool sessionRunning,
        string? clientOrderId = null)
    {
        if (!sessionRunning || direction is not ("LONG" or "SHORT"))
        {
            return new NewsRiskHandoff(direction, "NotEvaluated", direction == "NO_TRADE" ? "No trade." : "News trading is not running.", 0, 0, 0, 0, 0, 0, "NOT_RUNNING", null);
        }

        var signal = direction == "SHORT" ? SignalType.Sell : SignalType.Buy;
        var evaluation = risk.Evaluate(signal, profile, snapshot, utcNow);
        if (evaluation.Decision != RiskDecision.Approved || evaluation.Plan is not { } plan)
        {
            return new NewsRiskHandoff(direction, "Rejected", evaluation.Reason, 0, 0, 0, 0, 0, 0, "REJECTED", null);
        }

        var side = direction == "SHORT" ? PositionSide.Short : PositionSide.Long;
        var (stop, take) = LiveProtectivePrices.FromEntry(price, profile.StopLossPercent, profile.TakeProfitPercent, tickSize, side);
        var clientId = clientOrderId ?? "news-" + Guid.NewGuid().ToString("N")[..16];
        var request = new PlaceOrderRequest(
            clientId,
            snapshot.Symbol,
            direction == "SHORT" ? OrderSide.Sell : OrderSide.Buy,
            OrderType.Market,
            plan.Quantity,
            null,
            TimeSpan.FromSeconds(5));
        var orderDecision = liveTradingEnabled ? "READY" : "NOT_SENT";
        var reason = liveTradingEnabled ? plan.Reason : "Trading.LiveTradingEnabled=false";
        return new NewsRiskHandoff(
            direction,
            "Approved",
            reason,
            plan.Quantity,
            plan.PositionNotional,
            plan.Leverage,
            plan.IsolatedMargin,
            stop,
            take,
            orderDecision,
            request);
    }

    public static async Task<ExchangeOrder?> SubmitAsync(
        IExchangeConnector connector,
        PlaceOrderRequest request,
        LiveEntryFacts facts,
        CancellationToken cancellationToken)
    {
        var block = LiveEntryGate.Block(facts);
        if (block is not null)
        {
            return null;
        }

        return await connector.PlaceOrderAsync(request, cancellationToken);
    }

    public static async Task<ExchangeOrder?> SubmitAsync(
        IExchangeConnector connector,
        PlaceOrderRequest request,
        bool liveTradingEnabled,
        CancellationToken cancellationToken)
    {
        var block = LiveEntryGate.Block(new LiveEntryFacts(
            TradingMode.Live,
            liveTradingEnabled,
            false,
            true,
            false,
            true,
            true,
            true,
            false));
        if (block is not null)
        {
            return null;
        }

        return await connector.PlaceOrderAsync(request, cancellationToken);
    }
}
