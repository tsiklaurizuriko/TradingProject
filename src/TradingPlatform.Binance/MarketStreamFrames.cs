using System.Globalization;
using System.Text.Json;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Binance;

/// <summary>Parsing for USD-M combined market-stream frames (<c>{"stream":..,"data":..}</c>).</summary>
public static class MarketStreamFrames
{
    public const string MiniTickerStream = "!miniTicker@arr";

    private static ReadOnlySpan<byte> FinalMark => "\"x\":true"u8;
    private static ReadOnlySpan<byte> FinalMarkSpaced => "\"x\": true"u8;
    private static ReadOnlySpan<byte> KlineMark => "@kline_"u8;
    private static ReadOnlySpan<byte> MiniTickerMark => "24hrMiniTicker"u8;

    public static string KlineStream(string symbol, Timeframe timeframe) =>
        $"{symbol.ToLowerInvariant()}@kline_{timeframe.ToBinanceInterval()}";

    public static bool IsMiniTicker(ReadOnlySpan<byte> frame) => frame.IndexOf(MiniTickerMark) >= 0;

    /// <summary>A closed kline from a frame. Most kline frames are updates of the forming bar and are rejected before JSON parsing.</summary>
    public static bool TryParseClosedKline(ReadOnlySpan<byte> frame, out string symbol, out Timeframe timeframe, out MarketCandle candle)
    {
        symbol = "";
        timeframe = default;
        candle = null!;
        if (frame.IndexOf(KlineMark) < 0 || (frame.IndexOf(FinalMark) < 0 && frame.IndexOf(FinalMarkSpaced) < 0))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(frame.ToArray());
            var root = document.RootElement;
            var data = root.TryGetProperty("data", out var inner) ? inner : root;
            if (!data.TryGetProperty("k", out var k)
                || !k.TryGetProperty("x", out var final)
                || final.ValueKind != JsonValueKind.True
                || !TimeframeExtensions.TryParseInterval(k.GetProperty("i").GetString() ?? "", out timeframe))
            {
                return false;
            }

            symbol = (k.GetProperty("s").GetString() ?? "").ToUpperInvariant();
            if (symbol.Length == 0)
            {
                return false;
            }

            var closeTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("T").GetInt64());
            candle = new MarketCandle
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(k.GetProperty("t").GetInt64()),
                CloseTime = closeTime,
                Open = Dec(k.GetProperty("o")),
                High = Dec(k.GetProperty("h")),
                Low = Dec(k.GetProperty("l")),
                Close = Dec(k.GetProperty("c")),
                Volume = Dec(k.GetProperty("v")),
                TradeCount = k.TryGetProperty("n", out var trades) && trades.TryGetInt32(out var n) ? n : 0,
                TakerBuyVolume = k.TryGetProperty("V", out var taker) ? Dec(taker) : 0m,
                IsClosed = true,
                ExchangeTimestamp = closeTime
            };
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>Last prices from a <c>!miniTicker@arr</c> frame.</summary>
    public static IReadOnlyList<(string Symbol, decimal Price)> ParseMiniTickers(ReadOnlySpan<byte> frame)
    {
        var rows = new List<(string, decimal)>();
        try
        {
            using var document = JsonDocument.Parse(frame.ToArray());
            var root = document.RootElement;
            var data = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var inner) ? inner : root;
            if (data.ValueKind != JsonValueKind.Array)
            {
                return rows;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("s", out var s)
                    && item.TryGetProperty("c", out var c)
                    && s.GetString() is { Length: > 0 } name
                    && decimal.TryParse(c.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var price)
                    && price > 0m)
                {
                    rows.Add((name.ToUpperInvariant(), price));
                }
            }
        }
        catch (JsonException)
        {
            // malformed frame
        }

        return rows;
    }

    private static decimal Dec(JsonElement element) =>
        decimal.Parse(element.GetString() ?? element.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
