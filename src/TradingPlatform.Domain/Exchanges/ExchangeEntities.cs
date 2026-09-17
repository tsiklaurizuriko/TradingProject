namespace TradingPlatform.Domain.Exchanges;

public enum ExchangeType
{
    BinanceSpot = 0
}

public sealed class ExchangeAccount : SoftDeletableEntity
{
    public Guid UserId { get; set; }
    public Identity.User User { get; set; } = null!;
    public ExchangeType Exchange { get; set; } = ExchangeType.BinanceSpot;
    public string Name { get; set; } = string.Empty;
    public bool IsTestnet { get; set; }
    public bool LiveEnabled { get; set; }
    public bool CanTrade { get; set; } = true;
    public string ApiKeyFingerprint { get; set; } = string.Empty;
    public ExchangeCredential? Credential { get; set; }
}

public sealed class ExchangeCredential : Entity
{
    public Guid ExchangeAccountId { get; set; }
    public ExchangeAccount ExchangeAccount { get; set; } = null!;
    public byte[] ApiKeyCipher { get; set; } = [];
    public byte[] ApiSecretCipher { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] Tag { get; set; } = [];
}
