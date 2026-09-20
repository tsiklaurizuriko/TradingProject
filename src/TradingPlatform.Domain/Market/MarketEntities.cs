using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Market;

public sealed class Symbol : Entity
{
    public string Name { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
    public decimal TickSize { get; set; }
    public decimal StepSize { get; set; }
    public decimal MinQuantity { get; set; }
    public decimal MinNotional { get; set; }
    public int PricePrecision { get; set; }
    public int QuantityPrecision { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class TimeframeRecord : Entity
{
    public Timeframe Timeframe { get; set; }
    public string Code { get; set; } = string.Empty;
    public int DurationSeconds { get; set; }
}

public sealed class MarketCandle : Entity
{
    public Guid SymbolId { get; set; }
    public Symbol Symbol { get; set; } = null!;
    public Timeframe Timeframe { get; set; }
    public DateTimeOffset OpenTime { get; set; }
    public DateTimeOffset CloseTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public int? TradeCount { get; set; }
    public bool IsClosed { get; set; }
    public DateTimeOffset ExchangeTimestamp { get; set; }
    /// <summary>Binance kline taker buy base volume when parsed. Not stored in EF. 0 means unavailable.</summary>
    public decimal TakerBuyVolume { get; set; }
}

public sealed class MarketTrade : Entity
{
    public Guid SymbolId { get; set; }
    public Symbol Symbol { get; set; } = null!;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public bool IsBuyerMaker { get; set; }
    public DateTimeOffset ExchangeTimestamp { get; set; }
    public long? ExchangeTradeId { get; set; }
}
