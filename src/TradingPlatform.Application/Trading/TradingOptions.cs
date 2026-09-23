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
    public int UniverseRefreshMinutes { get; set; } = 15;
    public string UniverseCachePath { get; set; } = "data/futures-universe.json";
    public ScannerOptions Scanner { get; set; } = new();
    public ScalpingOptions Scalping { get; set; } = new();
    public PriceActionOptions PriceAction { get; set; } = new();
}

public sealed class ScalpingOptions
{
    public bool Enabled { get; set; }
    public bool AllowLive { get; set; }
    public string ArtifactDirectory { get; set; } = "";
    public Dictionary<string, int> MaxHoldBars { get; set; } = new()
    {
        ["1m"] = 15,
        ["3m"] = 12,
        ["5m"] = 8,
        ["15m"] = 6
    };
}

public sealed class PriceActionOptions
{
    public bool Enabled { get; set; }
    public bool AllowLive { get; set; }
    public bool PaperEnabled { get; set; }
    public bool LiveEnabled { get; set; }
    public string ArtifactDirectory { get; set; } = "";
    public Dictionary<string, NearMissCandidateOptions> Candidates { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class NearMissCandidateOptions
{
    public bool Enabled { get; set; }
}

public sealed class NullTradingRealtimePublisher : ITradingRealtimePublisher
{
    public Task PublishTickerAsync(string symbol, decimal price, DateTimeOffset timestamp, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishOverviewAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task PublishBotAsync(Guid botId, string status, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
