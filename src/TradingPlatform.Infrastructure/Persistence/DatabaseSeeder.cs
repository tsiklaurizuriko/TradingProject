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
        await SeedStrategiesAsync(cancellationToken);
    }

    private async Task SeedStrategiesAsync(CancellationToken cancellationToken)
    {
        var admin = await _db.Users.FirstAsync(cancellationToken);
        var existing = await _db.Strategies.ToListAsync(cancellationToken);
        foreach (var strategy in existing)
        {
            if (!strategy.AppliesToAllSymbols && string.IsNullOrWhiteSpace(strategy.AllowedSymbolsCsv))
            {
                strategy.AppliesToAllSymbols = true;
            }

            if (string.Equals(strategy.Name, "EMA RSI Strategy", StringComparison.OrdinalIgnoreCase))
            {
                strategy.TemplateKey = StrategyTemplateKeys.EmaRsiTrend;
                strategy.AllowedSide = StrategySides.Long;
            }
        }

        var known = existing
            .Select(s => s.TemplateKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (existing.Any(s => string.Equals(s.Name, "EMA RSI Strategy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s.Name, "EMA RSI Trend", StringComparison.OrdinalIgnoreCase)))
        {
            known.Add(StrategyTemplateKeys.EmaRsiTrend);
        }

        foreach (var row in Catalog)
        {
            if (known.Contains(row.Key))
            {
                continue;
            }

            var parameters = StrategyTemplates.DefaultsFor(row.Key, row.QualityOn) with
            {
                AllowedSide = StrategySides.Long,
                Timeframe = "5m"
            };
            var strategy = new Strategy
            {
                UserId = admin.Id,
                User = admin,
                Name = row.Name,
                Description = row.Description,
                AppliesToAllSymbols = true,
                TemplateKey = row.Key,
                AllowedSide = StrategySides.Long
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
            known.Add(row.Key);
        }
    }

    private static readonly (string Key, string Name, string Description, bool QualityOn)[] Catalog =
    [
        (StrategyTemplateKeys.EmaRsiTrend, "EMA RSI Trend",
            "Closed-candle EMA cross with an RSI band. Quality filters skip some noisy setups; they do not cap loss at Planned Risk.",
            false),
        (StrategyTemplateKeys.MacdTrend, "MACD Trend",
            "Closed-candle MACD cross with histogram and slow EMA confirmation. Filters skip some noisy setups; they are not a profit claim.",
            true),
        (StrategyTemplateKeys.RsiPullback, "RSI Pullback",
            "Trend-aligned RSI pullback. LONG only above the slow EMA at oversold. Filters skip some noisy setups; they are not a profit claim.",
            true),
        (StrategyTemplateKeys.BollingerReversion, "Bollinger Reversion",
            "Close returns inside the Bollinger band while still on the slow EMA side. Filters skip some noisy setups; they are not a profit claim.",
            true),
        (StrategyTemplateKeys.DonchianBreakout, "Donchian Breakout",
            "Close breaks the N-bar Donchian high or low. Filters skip some noisy setups; they are not a profit claim.",
            true)
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
            _db.RiskProfiles.Add(template);
            return;
        }

        existing.Name = name;
        existing.IsSystem = true;
        existing.RiskPerTradePercent = template.RiskPerTradePercent;
        existing.StopLossPercent = template.StopLossPercent;
        existing.TakeProfitPercent = template.TakeProfitPercent;
        existing.MaxLeverage = template.MaxLeverage;
        existing.MaxDailyLossPercent = template.MaxDailyLossPercent;
        existing.MaxPortfolioRiskPercent = template.MaxPortfolioRiskPercent;
        existing.MaxSimultaneousPositions = template.MaxSimultaneousPositions;
        existing.MaxConsecutiveLosses = template.MaxConsecutiveLosses;
        existing.CooldownMinutes = template.CooldownMinutes;
        existing.MinimumLiquidationSafetyBufferPercent = template.MinimumLiquidationSafetyBufferPercent;
        existing.AllowLive = template.AllowLive;
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
