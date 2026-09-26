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

    public static NewsRiskHandoff Handoff(
        string direction,
        decimal price,
        decimal tickSize,
        RiskProfile profile,
        RiskSnapshot snapshot,
        IRiskEngine risk,
        DateTimeOffset utcNow,
        bool liveTradingEnabled,
        bool sessionRunning)
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
        var clientId = "news-" + Guid.NewGuid().ToString("N")[..16];
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
        bool liveTradingEnabled,
        CancellationToken cancellationToken)
    {
        if (!liveTradingEnabled)
        {
            return null;
        }

        return await connector.PlaceOrderAsync(request, cancellationToken);
    }
}
