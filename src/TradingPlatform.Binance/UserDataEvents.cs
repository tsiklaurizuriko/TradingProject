using System.Globalization;
using System.Text.Json;

namespace TradingPlatform.Binance;

public enum UserDataEventKind
{
    Ignored,
    OrderUpdate,
    AlgoUpdate,
    AccountUpdate,
    MarginCall,
    ListenKeyExpired
}

public sealed record UserDataOrderUpdate(
    string Symbol,
    string ClientOrderId,
    string? ExchangeOrderId,
    string Side,
    string OrderType,
    string ExecutionType,
    string Status,
    decimal LastFilledQuantity,
    decimal CumulativeQuantity,
    decimal LastFilledPrice,
    decimal? Commission,
    string? CommissionAsset,
    string? TradeId,
    bool ReduceOnly);

public sealed record UserDataPositionChange(string Symbol, decimal Amount, decimal EntryPrice);

public sealed record UserDataEvent(
    UserDataEventKind Kind,
    DateTimeOffset? EventTime,
    UserDataOrderUpdate? Order = null,
    IReadOnlyList<UserDataPositionChange>? Positions = null,
    string? Reason = null);

/// <summary>Reads Binance USD-M user-data stream frames. Unknown or malformed frames are ignored, never thrown.</summary>
public static class UserDataEvents
{
    public static UserDataEvent Parse(string frame)
    {
        JsonElement json;
        try
        {
            json = JsonSerializer.Deserialize<JsonElement>(frame);
        }
        catch (JsonException)
        {
            return new UserDataEvent(UserDataEventKind.Ignored, null, Reason: "Not JSON.");
        }

        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            json = data;
        }

        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("e", out var type))
        {
            return new UserDataEvent(UserDataEventKind.Ignored, null, Reason: "No event type.");
        }

        var at = Long(json, "E") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : (DateTimeOffset?)null;
        return type.GetString() switch
        {
            "ORDER_TRADE_UPDATE" when json.TryGetProperty("o", out var order) && order.ValueKind == JsonValueKind.Object =>
                new UserDataEvent(UserDataEventKind.OrderUpdate, at, ReadOrder(order)),
            "ALGO_UPDATE" => new UserDataEvent(UserDataEventKind.AlgoUpdate, at),
            "ACCOUNT_UPDATE" => new UserDataEvent(UserDataEventKind.AccountUpdate, at, Positions: ReadPositions(json)),
            "MARGIN_CALL" => new UserDataEvent(UserDataEventKind.MarginCall, at),
            "listenKeyExpired" => new UserDataEvent(UserDataEventKind.ListenKeyExpired, at),
            var other => new UserDataEvent(UserDataEventKind.Ignored, at, Reason: other)
        };
    }

    private static UserDataOrderUpdate ReadOrder(JsonElement o) => new(
        Text(o, "s") ?? string.Empty,
        Text(o, "c") ?? string.Empty,
        Long(o, "i")?.ToString(CultureInfo.InvariantCulture),
        Text(o, "S") ?? string.Empty,
        Text(o, "ot") ?? Text(o, "o") ?? string.Empty,
        Text(o, "x") ?? string.Empty,
        Text(o, "X") ?? string.Empty,
        Number(o, "l") ?? 0m,
        Number(o, "z") ?? 0m,
        Number(o, "L") ?? 0m,
        Number(o, "n"),
        Text(o, "N"),
        Long(o, "t") is { } trade and > 0 ? trade.ToString(CultureInfo.InvariantCulture) : null,
        o.TryGetProperty("R", out var reduce) && reduce.ValueKind == JsonValueKind.True);

    private static IReadOnlyList<UserDataPositionChange> ReadPositions(JsonElement json)
    {
        if (!json.TryGetProperty("a", out var account)
            || account.ValueKind != JsonValueKind.Object
            || !account.TryGetProperty("P", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return rows.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Object)
            .Select(row => new UserDataPositionChange(Text(row, "s") ?? string.Empty, Number(row, "pa") ?? 0m, Number(row, "ep") ?? 0m))
            .Where(row => row.Symbol.Length > 0)
            .ToList();
    }

    private static string? Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? Long(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static decimal? Number(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            _ => null
        };
    }
}
