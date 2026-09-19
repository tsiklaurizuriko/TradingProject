using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.MarketData;

public sealed class TradeEligibilityService : ITradeEligibility
{
    private readonly ScannerOptions _options;

    public TradeEligibilityService(IOptions<TradingOptions> options)
    {
        _options = options.Value.Scanner ?? new ScannerOptions();
    }

    public TradeEligibilityDecision Evaluate(MarketScanRow row) =>
        TradeEligibilityRules.Evaluate(row, _options);

    public TradeEligibilityDecision Evaluate(RankedUsdtSpotSymbol row, decimal spreadBps = 0m, decimal fundingRate = 0m)
    {
        var contract = new DiscoveredFuturesContract(
            row.Symbol,
            row.BaseAsset,
            row.QuoteAsset,
            "USDT",
            "PERPETUAL",
            "TRADING",
            row.TickSize,
            row.StepSize,
            row.MinQuantity,
            row.MinNotional,
            row.PricePrecision,
            row.QuantityPrecision);
        var scan = new MarketScanRow(
            contract,
            row.LastPrice,
            row.QuoteVolume,
            row.PriceChangePercent,
            row.HighPrice,
            row.LowPrice,
            row.Trades24h,
            0m,
            0m,
            spreadBps,
            fundingRate,
            0m,
            0m,
            MarketScanScoring.VolatilityPercent(row.HighPrice, row.LowPrice, row.LastPrice),
            Math.Min(100m, Math.Abs(row.PriceChangePercent)),
            MarketScanScoring.DataQuality(row.LastPrice, row.QuoteVolume, row.Trades24h, row.TickSize, row.StepSize, row.MinQuantity),
            ScanScore: 1m,
            ScanRank: 1);
        return Evaluate(scan);
    }
}
