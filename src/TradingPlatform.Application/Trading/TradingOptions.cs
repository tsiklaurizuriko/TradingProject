using TradingPlatform.Application.Abstractions.MarketData;

namespace TradingPlatform.Application.Trading;

public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public bool LiveTradingEnabled { get; set; }

    /// <summary>Live, Shadow, or Testnet. See <see cref="TradingVenue"/>.</summary>
    public string Venue { get; set; } = nameof(TradingVenueKind.Live);

    /// <summary>Entry switch for a Shadow process. It never reaches Binance's signed API.</summary>
    public bool ShadowTradingEnabled { get; set; }

    /// <summary>Entry switch for a Testnet process. It is independent of <see cref="LiveTradingEnabled"/>.</summary>
    public bool TestnetTradingEnabled { get; set; }

    public TradingVenueKind VenueKind => TradingVenue.Parse(Venue);

    /// <summary>
    /// Whether new entries may be sent on this process's venue. Only <see cref="LiveTradingEnabled"/> opens real-money entries,
    /// and only on the Live venue.
    /// </summary>
    public bool EntriesEnabled => VenueKind switch
    {
        TradingVenueKind.Shadow => ShadowTradingEnabled,
        TradingVenueKind.Testnet => TestnetTradingEnabled,
        _ => LiveTradingEnabled
    };

    public bool KillSwitchEnabled { get; set; }
    public bool HostBotEngine { get; set; } = true;
    public int BotEngineIntervalSeconds { get; set; } = 15;
    public int KlineLimit { get; set; } = 120;
    public int ReconciliationMaxAgeSeconds { get; set; } = 90;

    /// <summary>Entries are skipped when the book is wider than this. Zero turns the check off.</summary>
    public decimal MaxEntrySpreadBps { get; set; } = 15m;

    /// <summary>Entries are skipped when the signal bar's true range is this many times the recent median. Zero turns it off.</summary>
    public decimal MaxEntryRangeShock { get; set; } = 5m;

    /// <summary>How far back the equity peak for the drawdown halt is taken.</summary>
    public int EquityPeakLookbackDays { get; set; } = 30;
    /// <summary>Close a live position with a reduce-only market order when no stop can be placed or restored.</summary>
    public bool FlattenOnProtectionFailure { get; set; } = true;
    public int ProtectionRetryAttempts { get; set; } = 3;
    public int ProtectionRetryDelayMs { get; set; } = 500;
    /// <summary>Consecutive cycles an existing (adopted or restored) position may stay without a stop before it is flattened.</summary>
    public int UnprotectedCyclesBeforeFlatten { get; set; } = 3;
    public int UniverseRefreshMinutes { get; set; } = 15;
    public string UniverseCachePath { get; set; } = "data/futures-universe.json";
    public ScannerOptions Scanner { get; set; } = new();
    public ScalpingOptions Scalping { get; set; } = new();
    public PriceActionOptions PriceAction { get; set; } = new();
    public CrossSectionalReversalOptions CrossSectionalReversal { get; set; } = new();
}

public sealed class CrossSectionalReversalOptions
{
    public bool Enabled { get; set; }
    public bool PaperEnabled { get; set; }
    public bool LiveEnabled { get; set; }
    public bool Return15mEnabled { get; set; }
    public bool Return1hEnabled { get; set; }
    public int MaxLeverage { get; set; } = 3;
    public int MaxTotalPositions { get; set; } = 10;
    public int MaxLongPositions { get; set; } = 5;
    public int MaxShortPositions { get; set; } = 5;
    public decimal MaxCrossSectionalRiskPercent { get; set; } = 2.0m;
    public decimal MaxPerPositionRiskPercent { get; set; } = 0.25m;
    public int MinimumEligibleSymbols { get; set; } = 30;
    public int HistoryBarsRequired { get; set; } = 96;
    public int MinimumUniverseSize { get; set; } = 30;
    public int RebalanceIntervalMinutes { get; set; } = 15;
    public int MinimumPositionAgeMinutes { get; set; } = 15;
    public int RebalanceCooldownMinutes { get; set; } = 15;
    public int MaxClusterPositions { get; set; } = 3;
    public decimal MaxClusterRiskPercent { get; set; } = 0.75m;
    public decimal MaxDirectionalRiskPercent { get; set; } = 1.5m;
    public decimal MaxHeat { get; set; } = 1.0m;
    public bool FundingRiskEnabled { get; set; }
    public bool VolatilitySafetyEnabled { get; set; }
    public bool EqualRiskSizing { get; set; } = true;
    public bool DynamicRiskWeighting { get; set; }
    public int TopDecilePercent { get; set; } = 10;
    public int BottomDecilePercent { get; set; } = 10;
    public string RankingClock { get; set; } = "BTCUSDT_15M";
    public bool AllowLong { get; set; } = true;
    public bool AllowShort { get; set; } = true;
    public string ProductionApproval { get; set; } = "INSUFFICIENT_EVIDENCE";
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
