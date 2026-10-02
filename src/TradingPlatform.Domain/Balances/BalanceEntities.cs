using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Domain.Balances;

public sealed class Balance : Entity
{
    public Guid ExchangeAccountId { get; set; }
    public ExchangeAccount ExchangeAccount { get; set; } = null!;
    public Guid? BotId { get; set; }
    public Bot? Bot { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public TradingMode Mode { get; set; } = TradingMode.Live;
    public uint RowVersion { get; set; }
}

public sealed class BalanceSnapshot : Entity
{
    public Guid ExchangeAccountId { get; set; }
    public ExchangeAccount ExchangeAccount { get; set; } = null!;
    public Guid? BotId { get; set; }
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal PortfolioValueQuote { get; set; }
    public TradingMode Mode { get; set; }
    public DateTimeOffset SnapshotAt { get; set; } = DateTimeOffset.UtcNow;
}
