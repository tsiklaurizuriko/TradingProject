using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Operations;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Infrastructure.Persistence;

public sealed class DatabaseSeeder
{
    public const string SampleEmaRsiDefinition = """
        {
          "name": "EMA RSI Strategy",
          "version": 1,
          "symbol": "BTCUSDT",
          "timeframe": "5m",
          "entry": {
            "operator": "AND",
            "conditions": [
              {
                "indicator": "EMA",
                "period": 20,
                "comparison": "CROSSES_ABOVE",
                "value": { "indicator": "EMA", "period": 50 }
              },
              {
                "indicator": "RSI",
                "period": 14,
                "comparison": "GREATER_THAN",
                "value": 50
              }
            ]
          },
          "exit": {
            "operator": "OR",
            "conditions": [
              {
                "indicator": "EMA",
                "period": 20,
                "comparison": "CROSSES_BELOW",
                "value": { "indicator": "EMA", "period": 50 }
              },
              { "type": "STOP_LOSS", "percent": 1.5 },
              { "type": "TAKE_PROFIT", "percent": 3 }
            ]
          }
        }
        """;

    private readonly TradingDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        TradingDbContext db,
        IPasswordHasher passwordHasher,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await SeedRolesAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await SeedLookupsAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await SeedTradingDefaultsAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedRolesAsync(CancellationToken cancellationToken)
    {
        if (await _db.Roles.AnyAsync(cancellationToken))
        {
            return;
        }

        var permissions = new[]
        {
            new Permission { Code = PermissionCodes.View, Description = "View data" },
            new Permission { Code = PermissionCodes.ManagePaperBots, Description = "Manage paper bots" },
            new Permission { Code = PermissionCodes.ManageTestnetBots, Description = "Manage testnet bots" },
            new Permission { Code = PermissionCodes.ManageLiveBots, Description = "Manage live bots" },
            new Permission { Code = PermissionCodes.ManageStrategies, Description = "Manage strategies" },
            new Permission { Code = PermissionCodes.ManageExchanges, Description = "Manage exchange accounts" },
            new Permission { Code = PermissionCodes.EnableLiveTrading, Description = "Enable live trading" },
            new Permission { Code = PermissionCodes.EmergencyStop, Description = "Emergency stop" },
            new Permission { Code = PermissionCodes.ManageUsers, Description = "Manage users" },
            new Permission { Code = PermissionCodes.ViewAudit, Description = "View audit logs" }
        };
        _db.Permissions.AddRange(permissions);

        var admin = new Role { Name = RoleNames.Admin, Description = "System administrator" };
        var trader = new Role { Name = RoleNames.Trader, Description = "Paper and testnet trader" };
        var viewer = new Role { Name = RoleNames.Viewer, Description = "Read-only" };
        _db.Roles.AddRange(admin, trader, viewer);

        foreach (var permission in permissions)
        {
            admin.RolePermissions.Add(new RolePermission { Role = admin, Permission = permission });
        }

        foreach (var code in new[]
                 {
                     PermissionCodes.View, PermissionCodes.ManagePaperBots, PermissionCodes.ManageTestnetBots,
                     PermissionCodes.ManageStrategies, PermissionCodes.ManageExchanges, PermissionCodes.EmergencyStop
                 })
        {
            trader.RolePermissions.Add(new RolePermission { Role = trader, Permission = permissions.First(p => p.Code == code) });
        }

        viewer.RolePermissions.Add(new RolePermission { Role = viewer, Permission = permissions.First(p => p.Code == PermissionCodes.View) });
    }

    private async Task SeedLookupsAsync(CancellationToken cancellationToken)
    {
        if (!await _db.Timeframes.AnyAsync(cancellationToken))
        {
            foreach (var tf in Enum.GetValues<Timeframe>())
            {
                _db.Timeframes.Add(new TimeframeRecord
                {
                    Timeframe = tf,
                    Code = tf.ToBinanceInterval(),
                    DurationSeconds = (int)tf.ToDuration().TotalSeconds
                });
            }
        }

        if (!await _db.Symbols.AnyAsync(cancellationToken))
        {
            _db.Symbols.Add(new Symbol
            {
                Name = "BTCUSDT",
                BaseAsset = "BTC",
                QuoteAsset = "USDT",
                TickSize = 0.01m,
                StepSize = 0.00001m,
                MinQuantity = 0.00001m,
                MinNotional = 5m,
                PricePrecision = 2,
                QuantityPrecision = 5
            });
        }

        if (!await _db.SystemSettings.AnyAsync(cancellationToken))
        {
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = "Trading.LiveTradingEnabled",
                Value = "false",
                Description = "Global live trading switch. Default off."
            });
            _db.SystemSettings.Add(new SystemSetting
            {
                Key = "Trading.KillSwitch",
                Value = "false",
                Description = "Global kill switch."
            });
        }
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var email = _configuration["Seed:AdminEmail"] ?? "admin@localhost";
        if (await _db.Users.IgnoreQueryFilters().AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant(), cancellationToken))
        {
            return;
        }

        var adminRole = await _db.Roles.FirstAsync(r => r.Name == RoleNames.Admin, cancellationToken);
        var password = _configuration["Seed:AdminPassword"] ?? "ChangeMe_Admin_123!";
        var user = new User
        {
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            DisplayName = "Administrator",
            PasswordHash = _passwordHasher.Hash(password),
            EmailConfirmed = true
        };
        user.UserRoles.Add(new UserRole { User = user, Role = adminRole });
        _db.Users.Add(user);
        _logger.LogInformation("Seeded admin user {Email}. Change the password immediately.", email);
    }

    private async Task SeedTradingDefaultsAsync(CancellationToken cancellationToken)
    {
        await UpsertSystemRiskAsync("LOW", ["Low Risk", "Conservative"], LowBook(), cancellationToken);
        await UpsertSystemRiskAsync("MEDIUM", ["Medium Risk", "Moderate"], MediumBook(), cancellationToken);
        await UpsertSystemRiskAsync("HIGH", ["High Risk", "Aggressive"], HighBook(), cancellationToken);
        await EnsureOneActiveAsync(cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        await SeedStrategiesAsync(cancellationToken);
    }

    private async Task SeedStrategiesAsync(CancellationToken cancellationToken)
    {
        var admin = await _db.Users.FirstAsync(cancellationToken);
        var existing = await _db.Strategies.Include(s => s.Versions).ToListAsync(cancellationToken);
        foreach (var row in Catalog)
        {
            var strategy = existing.FirstOrDefault(s => MatchesCatalog(s, row.Key, row.Name));
            var parameters = StrategyTemplates.DefaultsFor(row.Key, qualityOn: !row.Research) with
            {
                AllowedSide = StrategySides.Both,
                Timeframe = "5m"
            };
            if (strategy is null)
            {
                strategy = new Strategy
                {
                    UserId = admin.Id,
                    User = admin,
                    Name = row.Name,
                    Description = row.Description,
                    AppliesToAllSymbols = true,
                    TemplateKey = row.Key,
                    AllowedSide = StrategySides.Both,
                    IsEnabled = !row.Research,
                    ValidationStatus = row.Research
                        ? StrategyTemplates.ResearchStatus(row.Key)
                        : StrategyValidationStatuses.ValidationPending
                };
                strategy.Versions.Add(new StrategyVersion
                {
                    Strategy = strategy,
                    VersionNumber = 1,
                    DefinitionJson = StrategyTemplates.Build(strategy.Name, 1, parameters),
                    Symbol = "BTCUSDT",
                    Timeframe = Timeframe.FiveMinutes
                });
                _db.Strategies.Add(strategy);
                existing.Add(strategy);
                continue;
            }

            AlignCatalogStrategy(strategy, row, parameters);
        }
    }

    private static bool MatchesCatalog(Strategy strategy, string templateKey, string name) =>
        string.Equals(strategy.TemplateKey, templateKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategy.Name, name, StringComparison.OrdinalIgnoreCase)
        || (templateKey == StrategyTemplateKeys.EmaRsiTrend
            && string.Equals(strategy.Name, "EMA RSI Strategy", StringComparison.OrdinalIgnoreCase));

    private static void AlignCatalogStrategy(
        Strategy strategy,
        (string Key, string Name, string Description, bool Research) row,
        StrategyTemplateParams parameters)
    {
        strategy.TemplateKey = row.Key;
        strategy.AllowedSide = StrategySides.Both;
        strategy.AppliesToAllSymbols = true;
        if (!row.Research)
        {
            strategy.IsEnabled = true;
            strategy.ValidationStatus = StrategyValidationStatuses.ValidationPending;
        }
        else if (string.IsNullOrWhiteSpace(strategy.ValidationStatus)
            || strategy.ValidationStatus == StrategyValidationStatuses.ValidationPending)
        {
            strategy.IsEnabled = false;
            strategy.ValidationStatus = StrategyTemplates.ResearchStatus(row.Key);
        }
        if (string.IsNullOrWhiteSpace(strategy.Description))
        {
            strategy.Description = row.Description;
        }

        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var current = latest is null ? null : StrategyTemplates.Read(latest.DefinitionJson);
        var legacy = latest is not null && LooksLegacyDefinition(latest.DefinitionJson);
        var mismatch = latest is null
            || legacy
            || current is null
            || current.TemplateKey != parameters.TemplateKey
            || current.AllowedSide != parameters.AllowedSide
            || current.Timeframe != parameters.Timeframe
            || current.Quality?.RequireVolume != true
            || current.Quality?.VolumeLookback != 20
            || current.Quality?.MinAtrPercent != 0.15m
            || current.Quality?.MaxAtrPercent != 4m
            || current.EmaFast != parameters.EmaFast
            || current.EmaSlow != parameters.EmaSlow
            || current.RsiPeriod != parameters.RsiPeriod
            || current.RsiMinimum != parameters.RsiMinimum
            || current.RsiLongMax != parameters.RsiLongMax
            || current.RsiOversold != parameters.RsiOversold
            || current.RsiOverbought != parameters.RsiOverbought
            || current.MacdFast != parameters.MacdFast
            || current.MacdSlow != parameters.MacdSlow
            || current.MacdSignal != parameters.MacdSignal
            || current.BbPeriod != parameters.BbPeriod
            || current.BbStdDev != parameters.BbStdDev
            || current.DonchianLength != parameters.DonchianLength;
        if (!mismatch)
        {
            return;
        }

        if (latest is null || latest.IsImmutable)
        {
            var versionNumber = (latest?.VersionNumber ?? 0) + 1;
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = versionNumber,
                DefinitionJson = StrategyTemplates.Build(strategy.Name, versionNumber, parameters),
                Symbol = "BTCUSDT",
                Timeframe = Timeframe.FiveMinutes
            });
            return;
        }

        latest.DefinitionJson = StrategyTemplates.Build(strategy.Name, latest.VersionNumber, parameters);
        latest.Timeframe = Timeframe.FiveMinutes;
        latest.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool LooksLegacyDefinition(string json) =>
        json.Contains("\"entry\"", StringComparison.OrdinalIgnoreCase)
        && !json.Contains("\"template\"", StringComparison.OrdinalIgnoreCase);

    private static readonly (string Key, string Name, string Description, bool Research)[] Catalog =
    [
        (StrategyTemplateKeys.EmaRsiTrend, "EMA RSI Trend",
            "Closed-candle EMA cross with an RSI band. Quality filters skip some noisy setups; they do not cap loss at Planned Risk.", false),
        (StrategyTemplateKeys.MacdTrend, "MACD Trend",
            "Closed-candle MACD cross with histogram and slow EMA confirmation. Filters skip some noisy setups; they are not a profit claim.", false),
        (StrategyTemplateKeys.RsiPullback, "RSI Pullback",
            "Trend-aligned RSI pullback. LONG above the slow EMA at oversold, SHORT below at overbought. Filters skip some noisy setups; they are not a profit claim.", false),
        (StrategyTemplateKeys.BollingerReversion, "Bollinger Reversion",
            "Close returns inside the Bollinger band while still on the slow EMA side. Filters skip some noisy setups; they are not a profit claim.", false),
        (StrategyTemplateKeys.DonchianBreakout, "Donchian Breakout",
            "Close breaks the N-bar Donchian high or low. Filters skip some noisy setups; they are not a profit claim.", false),
        (StrategyTemplateKeys.TurtleTsm, "Turtle Time-Series Momentum",
            "Systematic trend-following strategy using prior-range breakouts, EMA trend confirmation and ATR-based volatility control.", true),
        (StrategyTemplateKeys.VwapPullbackTrend, "VWAP Pullback Trend",
            "Trend-following pullback strategy using VWAP, EMA structure, RSI confirmation and volatility-aware stops.", true),
        (StrategyTemplateKeys.VolatilityBreakout, "Volatility Breakout",
            "Volatility-compression breakout strategy using Bollinger width, ATR expansion and relative volume.", true),
        (StrategyTemplateKeys.SupertrendEmaTrend, "Supertrend EMA Trend",
            "Trend-following strategy using Supertrend direction, EMA structure and ADX trend-strength confirmation.", true),
        (StrategyTemplateKeys.OiPriceMomentum, "Open Interest Price Momentum",
            "Futures-specific strategy researching conditional relationships between price movement, open interest, volume and trend.", true),
        (StrategyTemplateKeys.FundingOiRegime, "Funding Rate Price OI Regime",
            "Perpetual-futures strategy researching funding extremes together with price momentum and open-interest regimes.", true),
        (StrategyTemplateKeys.VpVwapReversion, "Volume Profile VWAP Mean Reversion",
            "Research whether VAL/VAH rejections revert toward POC/VWAP outside strong-trend regimes.", true),
        (StrategyTemplateKeys.LiqSweepReversal, "Liquidity Sweep Reversal",
            "Research failed breaks of causally confirmed swing highs/lows followed by a close back through the level.", true),
        (StrategyTemplateKeys.LiqSweepContinuation, "Liquidity Sweep Breakout Continuation",
            "Research sweeps that hold beyond the level with volume as breakout continuation, separate from reversal.", true),
        (StrategyTemplateKeys.FundingBasisRv, "Funding Basis Carry Relative Value",
            "Research funding and basis extremes. Requires aligned funding/index. Not fabricated.", true),
        (StrategyTemplateKeys.FundingOiReversal, "Funding Extreme OI Price Reversal",
            "Research extreme funding plus OI and price displacement as a reversal hypothesis.", true),
        (StrategyTemplateKeys.TakerFlowMomentum, "Taker Flow Volume Imbalance Momentum",
            "Research persistent taker buy/sell imbalance with price and volume confirmation.", true),
        (StrategyTemplateKeys.OiPriceVolumeRegime, "OI Price Volume Regime",
            "Research conditional expectancy of price/OI/volume states without pre-assigned labels.", true),
        (StrategyTemplateKeys.VwapDeviationReversion, "VWAP Deviation Reversion",
            "Research ATR-scaled VWAP deviations with rejection and a trend-regime filter.", true),
        (StrategyTemplateKeys.VwapBreakoutVolume, "VWAP Breakout Volume",
            "Research VWAP-aligned local breakouts with relative volume, on transition only.", true),
        (StrategyTemplateKeys.FailedBreakoutReversal, "Failed Breakout Reversal",
            "Research Donchian breakouts that fail to hold and close back inside the range.", true),
        (StrategyTemplateKeys.VolSqueezeStructure, "Volatility Squeeze Structure Break",
            "Research Bollinger/Keltner compression then expansion with a structure break and volume.", true),
        (StrategyTemplateKeys.MarketStructureTrend, "Market Structure Trend Continuation",
            "Research causal HH/HL or LH/LL continuation on a new confirmed swing.", true),
        (StrategyTemplateKeys.MarketStructurePullback, "Market Structure Pullback",
            "Research pullbacks to EMA/VWAP while causal market structure stays intact.", true),
        (StrategyTemplateKeys.AtrNormalizedMomentum, "ATR-Normalized Momentum",
            "Research (Close[t]-Close[t-N])/ATR with trend and a persistence transition.", true),
        (StrategyTemplateKeys.MtfTrendStructure, "Multi-Timeframe Trend LTF Structure",
            "Research last-completed HTF EMA trend with LTF structure/pullback entry.", true),
        (StrategyTemplateKeys.ZscoreMeanReversion, "Z-Score Statistical Mean Reversion",
            "Research rolling close Z-score extremes with mean reversion disabled in strong ADX.", true),
        (StrategyTemplateKeys.CryptoPairsArb, "Crypto Pairs Statistical Arbitrage",
            "Research rolling cointegrated crypto spreads. Causal pair selection only.", true),
        (StrategyTemplateKeys.XsRelativeStrength, "Cross-Sectional Relative Strength Momentum",
            "Research cross-sectional momentum ranks. Requires a universe snapshot.", true),
        (StrategyTemplateKeys.RegimeStrategyRouter, "Regime-Adaptive Strategy Router",
            "Deferred interpretable router. Must not be fit on OOS.", true),
        (StrategyTemplateKeys.FundingPriceMomentum, "Funding Price Momentum",
            "Research funding with price momentum as continuation vs contrarian. Not LIVE.", true),
        (StrategyTemplateKeys.FundingExtremeMomentumExhaustion, "Funding Extreme Momentum Exhaustion",
            "Research funding extremes with weakening momentum. Not LIVE.", true),
        (StrategyTemplateKeys.BasisMeanReversion, "Basis Mean Reversion",
            "Research normalized basis z-score as reversion and continuation separately. Not LIVE.", true),
        (StrategyTemplateKeys.FundingBasisVwap, "Funding Basis VWAP",
            "Research funding + basis + VWAP deviation. Not LIVE.", true),
        (StrategyTemplateKeys.OiBreakoutConfirmation, "OI Breakout Confirmation",
            "Research whether OI expansion adds information to a volume breakout. OI_SAMPLE_LIMITED. Not LIVE.", true)
    ];

    private async Task UpsertSystemRiskAsync(
        string name,
        string[] aliases,
        RiskProfile template,
        CancellationToken cancellationToken)
    {
        var names = new HashSet<string>(aliases.Append(name), StringComparer.OrdinalIgnoreCase);
        var existing = (await _db.RiskProfiles.ToListAsync(cancellationToken))
            .FirstOrDefault(row => names.Contains(row.Name));
        if (existing is null)
        {
            template.Name = name;
            template.IsSystem = true;
            template.IsActive = string.Equals(name, "LOW", StringComparison.OrdinalIgnoreCase);
            _db.RiskProfiles.Add(template);
            return;
        }

        existing.Name = name;
        existing.IsSystem = true;
    }

    private async Task EnsureOneActiveAsync(CancellationToken cancellationToken)
    {
        var books = await _db.RiskProfiles.Where(r => r.IsSystem).ToListAsync(cancellationToken);
        if (books.Count == 0)
        {
            return;
        }

        if (books.Count(r => r.IsActive) == 1)
        {
            return;
        }

        foreach (var book in books)
        {
            book.IsActive = string.Equals(book.Name, "LOW", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static RiskProfile LowBook() =>
        new()
        {
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 2m,
            TakeProfitPercent = 4m,
            MaxLeverage = 3m,
            MaxDailyLossPercent = 3m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 2,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true,
            IsActive = true
        };

    private static RiskProfile MediumBook() =>
        new()
        {
            RiskPerTradePercent = 1m,
            StopLossPercent = 2.5m,
            TakeProfitPercent = 5m,
            MaxLeverage = 5m,
            MaxDailyLossPercent = 5m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 2,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true
        };

    private static RiskProfile HighBook() =>
        new()
        {
            RiskPerTradePercent = 2m,
            StopLossPercent = 3m,
            TakeProfitPercent = 6m,
            MaxLeverage = 8m,
            MaxDailyLossPercent = 7m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 2,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = false
        };
}
