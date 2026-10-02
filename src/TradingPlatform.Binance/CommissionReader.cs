using System.Text.Json;

namespace TradingPlatform.Binance;

/// <summary>
/// Binance commission is an amount plus an asset. Order payload commission and user-trade
/// commission are cumulative for the queried order when every row names the same asset.
/// A missing amount or asset is unknown. Different assets are not added together and are not
/// converted to USDT.
/// </summary>
public static class CommissionReader
{
    public readonly record struct Result(decimal Amount, string? Asset, bool Known, string? Problem);

    public static Result FromOrderPayload(JsonElement payload)
    {
        var rows = new List<(decimal Amount, string? Asset, bool HasAmount)>();
        if (payload.TryGetProperty("commission", out var commissionEl))
        {
            var asset = payload.TryGetProperty("commissionAsset", out var assetEl) ? assetEl.GetString() : null;
            rows.Add((Read(commissionEl), asset, true));
        }

        if (payload.TryGetProperty("fills", out var fills) && fills.ValueKind == JsonValueKind.Array)
        {
            foreach (var fill in fills.EnumerateArray())
            {
                if (!fill.TryGetProperty("commission", out var fillCommission))
                {
                    continue;
                }

                var asset = fill.TryGetProperty("commissionAsset", out var fillAsset) ? fillAsset.GetString() : null;
                rows.Add((Read(fillCommission), asset, true));
            }
        }

        return Combine(rows);
    }

    public static Result FromUserTrades(JsonElement trades)
    {
        if (trades.ValueKind != JsonValueKind.Array)
        {
            return new Result(0m, null, false, "User trades were not an array.");
        }

        var rows = new List<(decimal Amount, string? Asset, bool HasAmount)>();
        foreach (var trade in trades.EnumerateArray())
        {
            if (!trade.TryGetProperty("commission", out var commissionEl))
            {
                rows.Add((0m, null, false));
                continue;
            }

            var asset = trade.TryGetProperty("commissionAsset", out var assetEl) ? assetEl.GetString() : null;
            rows.Add((Read(commissionEl), asset, true));
        }

        return Combine(rows);
    }

    private static Result Combine(List<(decimal Amount, string? Asset, bool HasAmount)> rows)
    {
        if (rows.Count == 0)
        {
            return new Result(0m, null, false, "Commission was not present.");
        }

        if (rows.Any(row => !row.HasAmount))
        {
            return new Result(0m, null, false, "A trade is missing its commission amount.");
        }

        if (rows.Any(row => string.IsNullOrWhiteSpace(row.Asset)))
        {
            return new Result(0m, null, false, "Commission asset is missing.");
        }

        var assets = rows.Select(row => row.Asset!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (assets.Length != 1)
        {
            return new Result(0m, null, false, "Commission assets disagree. They were not added together.");
        }

        return new Result(rows.Sum(row => row.Amount), assets[0], true, null);
    }

    private static decimal Read(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.Parse(element.GetString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
    }
}
