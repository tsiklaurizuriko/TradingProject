using System.Text.Json;

namespace TradingPlatform.Application.Abstractions.MarketData;

/// <summary>
/// A. SYMBOL DISCOVERY — contract metadata from Binance USDⓈ-M exchangeInfo.
/// This is not a scanner, not eligibility, and not a validation universe shortcut.
/// </summary>
public sealed record DiscoveredFuturesContract(
    string Symbol,
    string BaseAsset,
    string QuoteAsset,
    string MarginAsset,
    string ContractType,
    string Status,
    decimal TickSize,
    decimal StepSize,
    decimal MinQuantity,
    decimal MinNotional,
    int PricePrecision,
    int QuantityPrecision);

/// <summary>
/// B. MARKET SCANNER — live ranking overlay for the entire discovered universe.
/// Watchable symbols are scanned even when they are not trade-eligible.
/// </summary>
public sealed record MarketScanRow(
    DiscoveredFuturesContract Contract,
    decimal LastPrice,
    decimal QuoteVolume24h,
    decimal PriceChangePercent,
    decimal High24h,
    decimal Low24h,
    int Trades24h,
    decimal Bid,
    decimal Ask,
    decimal SpreadBps,
    decimal FundingRate,
    decimal MarkPrice,
    decimal OpenInterest,
    decimal VolatilityPercent,
    decimal TrendQuality,
    decimal DataQuality,
    decimal ScanScore,
    int ScanRank);

/// <summary>
/// D. TRADE ELIGIBILITY — dynamic gate. A scanned/watched symbol may still be ineligible.
/// </summary>
public sealed record TradeEligibilityDecision(
    string Symbol,
    bool Watchable,
    bool Eligible,
    string Reason);

public sealed record FuturesBookTicker(string Symbol, decimal Bid, decimal Ask);

public sealed record FuturesPremiumIndex(string Symbol, decimal MarkPrice, decimal LastFundingRate);

public interface IFuturesUniverseCatalog
{
    Task<IReadOnlyList<DiscoveredFuturesContract>> GetDiscoveredAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveredFuturesContract>> RefreshAsync(CancellationToken cancellationToken = default);

    DateTimeOffset? LastRefreshedAt { get; }
}

public interface IMarketScanner
{
    Task<IReadOnlyList<MarketScanRow>> ScanAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<MarketScanRow> Current { get; }
}

public interface ITradeEligibility
{
    TradeEligibilityDecision Evaluate(MarketScanRow row);

    TradeEligibilityDecision Evaluate(RankedUsdtSpotSymbol row, decimal spreadBps = 0m, decimal fundingRate = 0m);
}

public static class UsdtPerpetualContractRules
{
    public static bool TryMap(JsonElement symbol, out DiscoveredFuturesContract? contract, out string reason)
    {
        contract = null;
        var name = Text(symbol, "symbol");
        if (string.IsNullOrWhiteSpace(name) || name.Contains('_', StringComparison.Ordinal))
        {
            reason = "delivery_or_dated_contract";
            return false;
        }

        if (!name.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
        {
            reason = "not_usdt_symbol";
            return false;
        }

        var status = Text(symbol, "status");
        if (string.IsNullOrWhiteSpace(status))
        {
            status = Text(symbol, "contractStatus");
        }

        if (!string.Equals(status, "TRADING", StringComparison.OrdinalIgnoreCase))
        {
            reason = "inactive_or_not_trading";
            return false;
        }

        var quote = Text(symbol, "quoteAsset");
        if (!string.Equals(quote, "USDT", StringComparison.OrdinalIgnoreCase))
        {
            reason = "quote_not_usdt";
            return false;
        }

        var contractType = Text(symbol, "contractType");
        if (!string.Equals(contractType, "PERPETUAL", StringComparison.OrdinalIgnoreCase))
        {
            reason = "unsupported_contract_type";
            return false;
        }

        var margin = Text(symbol, "marginAsset");
        if (!string.IsNullOrWhiteSpace(margin) && !string.Equals(margin, "USDT", StringComparison.OrdinalIgnoreCase))
        {
            reason = "margin_not_usdt";
            return false;
        }

        var tickSize = 0m;
        var stepSize = 0m;
        var minQty = 0m;
        var minNotional = 5m;
        if (symbol.TryGetProperty("filters", out var filters) && filters.ValueKind == JsonValueKind.Array)
        {
            foreach (var filter in filters.EnumerateArray())
            {
                var type = Text(filter, "filterType");
                if (type == "PRICE_FILTER")
                {
                    tickSize = Dec(filter, "tickSize");
                }
                else if (type is "LOT_SIZE" or "MARKET_LOT_SIZE")
                {
                    var filterStep = Dec(filter, "stepSize");
                    var filterMin = Dec(filter, "minQty");
                    if (filterStep > stepSize)
                    {
                        stepSize = filterStep;
                    }

                    if (filterMin > minQty)
                    {
                        minQty = filterMin;
                    }
                }
                else if (type is "MIN_NOTIONAL" or "NOTIONAL")
                {
                    if (filter.TryGetProperty("notional", out _))
                    {
                        minNotional = Dec(filter, "notional");
                    }
                    else
                    {
                        minNotional = Dec(filter, "minNotional");
                    }
                }
            }
        }

        var pricePrecision = CountDecimals(tickSize);
        var quantityPrecision = CountDecimals(stepSize);
        if (symbol.TryGetProperty("pricePrecision", out var pp) && pp.TryGetInt32(out var pricePrec) && pricePrec >= 0)
        {
            pricePrecision = pricePrec;
        }

        if (symbol.TryGetProperty("quantityPrecision", out var qp) && qp.TryGetInt32(out var qtyPrec) && qtyPrec >= 0)
        {
            quantityPrecision = qtyPrec;
            var fromPrecision = PrecisionStep(qtyPrec);
            if (fromPrecision > stepSize)
            {
                stepSize = fromPrecision;
            }
        }

        if (minQty < stepSize)
        {
            minQty = stepSize;
        }

        if (tickSize <= 0m || stepSize <= 0m || minQty <= 0m)
        {
            reason = "invalid_or_missing_market_metadata";
            return false;
        }

        var baseAsset = Text(symbol, "baseAsset");
        if (string.IsNullOrWhiteSpace(baseAsset))
        {
            reason = "invalid_or_missing_market_metadata";
            return false;
        }

        contract = new DiscoveredFuturesContract(
            name.ToUpperInvariant(),
            baseAsset,
            quote.ToUpperInvariant(),
            string.IsNullOrWhiteSpace(margin) ? "USDT" : margin.ToUpperInvariant(),
            "PERPETUAL",
            "TRADING",
            tickSize,
            stepSize,
            minQty,
            minNotional <= 0m ? 5m : minNotional,
            pricePrecision,
            quantityPrecision);
        reason = "";
        return true;
    }

    public static IReadOnlyList<DiscoveredFuturesContract> MapExchangeInfo(JsonElement info)
    {
        if (!info.TryGetProperty("symbols", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<DiscoveredFuturesContract>();
        foreach (var symbol in symbols.EnumerateArray())
        {
            if (TryMap(symbol, out var contract, out _) && contract is not null)
            {
                list.Add(contract);
            }
        }

        return list
            .OrderBy(c => c.Symbol, StringComparer.Ordinal)
            .ToList();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";

    private static decimal Dec(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return 0m;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return decimal.TryParse(value.GetString() ?? value.GetRawText(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;
    }

    private static int CountDecimals(decimal value)
    {
        var text = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var i = text.IndexOf('.');
        if (i < 0)
        {
            return 0;
        }

        return Math.Max(0, text.TrimEnd('0').Length - i - 1);
    }

    private static decimal PrecisionStep(int decimals)
    {
        if (decimals <= 0)
        {
            return 1m;
        }

        var step = 1m;
        for (var i = 0; i < decimals; i++)
        {
            step /= 10m;
        }

        return step;
    }
}
