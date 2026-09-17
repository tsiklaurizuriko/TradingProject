using TradingPlatform.Application.Abstractions.MarketData;

namespace TradingPlatform.Application.Trading;

public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public bool LiveTradingEnabled { get; set; }
    public string DefaultMode { get; set; } = "PAPER";
    public bool KillSwitchEnabled { get; set; }
    public decimal PaperDefaultBalance { get; set; } = 10_000m;
    public decimal PaperFeeBps { get; set; } = 10m;
    public decimal PaperSlippageBps { get; set; } = 5m;
    public bool HostBotEngine { get; set; } = true;
    public bool AutoStartSamplePaperBot { get; set; }
    public int BotEngineIntervalSeconds { get; set; } = 15;
    public int KlineLimit { get; set; } = 120;
    public int PaperUniverseSize { get; set; } = 15;
}

public sealed class NullTradingRealtimePublisher : ITradingRealtimePublisher
{
    public Task PublishTickerAsync(string symbol, decimal price, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishOverviewAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PublishBotAsync(Guid botId, string status, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
