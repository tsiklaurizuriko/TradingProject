using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

/// <summary>
/// USD-M conditional SL/TP must use POST /fapi/v1/algoOrder (Binance 2025-12-09).
/// Isolated close-position stops: closePosition=true, no quantity, no reduceOnly.
/// </summary>
public static class BinanceConditionalAlgoOrder
{
    public const string PlacePath = "fapi/v1/algoOrder";
    public const string CancelPath = "fapi/v1/algoOrder";
    public const string OpenPath = "fapi/v1/openAlgoOrders";

    public static Dictionary<string, string> PlaceFields(
        string symbol,
        OrderSide closeSide,
        string type,
        decimal triggerPrice,
        string clientAlgoId,
        bool priceProtect)
    {
        return new Dictionary<string, string>
        {
            ["algoType"] = "CONDITIONAL",
            ["symbol"] = symbol.ToUpperInvariant(),
            ["side"] = closeSide == OrderSide.Buy ? "BUY" : "SELL",
            ["type"] = type,
            ["triggerPrice"] = BinanceHmac.FormatDecimal(triggerPrice),
            ["closePosition"] = "true",
            ["workingType"] = "MARK_PRICE",
            ["priceProtect"] = priceProtect ? "TRUE" : "FALSE",
            ["clientAlgoId"] = clientAlgoId,
            ["newOrderRespType"] = "RESULT"
        };
    }

    public static Dictionary<string, string> CancelFields(string symbol, string clientAlgoId)
    {
        return new Dictionary<string, string>
        {
            ["symbol"] = symbol.ToUpperInvariant(),
            ["clientAlgoId"] = clientAlgoId
        };
    }

    public static bool IsWrongEndpoint(string message) =>
        message.Contains("-4120", StringComparison.Ordinal)
        || message.Contains("Algo Order API", StringComparison.OrdinalIgnoreCase);
}
