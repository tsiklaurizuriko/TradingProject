using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Domain.Bots;
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

    public const string DevelopmentAdminPassword = "ChangeMe_Admin_123!";

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
                TickSize = 0.1m,
                StepSize = 0.001m,
                MinQuantity = 0.001m,
                MinNotional = 5m,
                PricePrecision = 2,
                QuantityPrecision = 3
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

        var password = _configuration["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("No admin user exists and Seed:AdminPassword is not set. No admin was seeded.");
            return;
        }

        var adminRole = await _db.Roles.FirstAsync(r => r.Name == RoleNames.Admin, cancellationToken);
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
        await UpsertSystemRiskAsync(NewsRiskBook.Name, [], NewsRiskBook.Create(), cancellationToken);
        await UpsertSystemRiskAsync("MEDIUM", ["Medium Risk", "Moderate"], MediumBook(), cancellationToken);
        await UpsertSystemRiskAsync("HIGH", ["High Risk", "Aggressive"], HighBook(), cancellationToken);
        await UpsertSystemRiskAsync("BTC 15m Vol Spike", ["FITTED-VOL-SPIKE", "vol_spike_ema_trend"], FittedVolSpikeBook(), cancellationToken);
        await UpsertSystemRiskAsync("BTC 15m BB Break", ["FITTED-BB-BREAK", "bb20_2_break"], FittedBbBreakBook(), cancellationToken);
        await UpsertSystemRiskAsync("30m EMA Cross", ["BTC 30m EMA Cross", "BTC-30M-EMA-CROSS", "btc_ema20_ema50_long"], FittedEmaCrossBook(), cancellationToken);
        await UpsertSystemRiskAsync("1d Time-Series Momentum", ["ts_momentum_28_5", "TS-MOMENTUM-28-5"], TsMomentumBook(), cancellationToken);
        await UpsertSystemRiskAsync("1d BTC 10-day High", ["btc_daily_max_10", "BTC-DAILY-MAX-10"], TsMomentumBook(), cancellationToken);
        await UpsertSystemRiskAsync("Flow Zone", ["flow_zone", "FLOW-ZONE"], TsMomentumBook(), cancellationToken);
        await UpsertSystemRiskAsync("Impulse Catch", ["impulse_catch", "IMPULSE-CATCH"], ImpulseCatchBook(), cancellationToken);
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
            if (!StrategyTemplateKeys.IsOperatorCatalog(row.Key))
            {
                continue;
            }

            var strategy = existing.FirstOrDefault(s => MatchesCatalog(s, row.Key, row.Name));
            var crossSection = StrategyTemplateKeys.IsCrossSectionalReversal(row.Key);
            var emaCross = row.Key == StrategyTemplateKeys.BtcEma20Ema50Long;
            var tsMomentum = row.Key is StrategyTemplateKeys.TsMomentum285 or StrategyTemplateKeys.BtcDailyMax10;
            var flat = row.Key == StrategyTemplateKeys.FlatRange;
            var flow = row.Key == StrategyTemplateKeys.FlowZone;
            var squeeze = row.Key == StrategyTemplateKeys.SqueezeWatch;
            var impulse = row.Key == StrategyTemplateKeys.ImpulseCatch;
            var zigzag = row.Key == StrategyTemplateKeys.ZigZagFade;
            var donchianV2 = row.Key == StrategyTemplateKeys.DonchianV2;
            var binhv = row.Key == StrategyTemplateKeys.BinHv45;
            var hlhb = row.Key == StrategyTemplateKeys.Hlhb;
            var hour = row.Key is StrategyTemplateKeys.FAdxSma or StrategyTemplateKeys.TripleSupertrend;
            var freqtradeLong = binhv || hlhb || row.Key is StrategyTemplateKeys.ClucMay72018 or StrategyTemplateKeys.CombinedBinHCluc;
            var longOnly = emaCross || tsMomentum || freqtradeLong || impulse;
                var parameters = StrategyTemplates.DefaultsFor(row.Key, qualityOn: !row.Research && !flat && !flow && !squeeze && !impulse && !StrategyTemplateKeys.IsObservation(row.Key) && !StrategyTemplateKeys.IsHistoricallyFitted(row.Key)) with
            {
                AllowedSide = longOnly ? StrategySides.Long : StrategySides.Both,
                Timeframe = SeedTimeframe(row.Key)
            };
            if (strategy is null)
            {
                var fitted = StrategyTemplateKeys.IsHistoricallyFitted(row.Key);
                strategy = new Strategy
                {
                    UserId = admin.Id,
                    User = admin,
                    Name = row.Name,
                    Description = row.Description,
                    AppliesToAllSymbols = emaCross || !fitted,
                    AllowedSymbolsCsv = emaCross || !fitted ? null : "BTCUSDT",
                    TemplateKey = row.Key,
                    AllowedSide = longOnly ? StrategySides.Long : StrategySides.Both,
                    IsEnabled = true,
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
                    Timeframe = TimeframeExtensions.TryParseInterval(parameters.Timeframe, out var seeded) ? seeded : Timeframe.FiveMinutes
                });
                _db.Strategies.Add(strategy);
                existing.Add(strategy);
                await _db.SaveChangesAsync(cancellationToken);
                continue;
            }

            AlignCatalogStrategy(strategy, row, parameters);
        }

        await SeedRefactoredResearchAsync(existing, admin, cancellationToken);
        await AlignBotTimeframesAsync(cancellationToken);
        await RetireHiddenStrategiesAsync(existing, cancellationToken);
        await AttachStrategyRiskAsync(cancellationToken);
        await ApplyPublishedRiskAsync(cancellationToken);
    }

    private async Task SeedRefactoredResearchAsync(List<Strategy> existing, User admin, CancellationToken cancellationToken)
    {
        foreach (var key in StrategyTemplateKeys.Refactored)
        {
            if (StrategyTemplateKeys.ContainsTemplate(existing.Select(strategy => strategy.TemplateKey), key))
            {
                continue;
            }

            var parameters = StrategyTemplates.DefaultsFor(key, qualityOn: false);
            var timeframe = TimeframeExtensions.TryParseInterval(parameters.Timeframe, out var parsed)
                ? parsed
                : Timeframe.FifteenMinutes;
            var strategy = new Strategy
            {
                UserId = admin.Id,
                User = admin,
                Name = StrategyTemplates.DisplayName(key),
                Description = "Research v2. NOT_VALIDATED. Selectable for a LIVE or PAPER bot. Seeding does not start bots and does not send orders.",
                AppliesToAllSymbols = true,
                TemplateKey = key,
                AllowedSide = parameters.AllowedSide,
                IsEnabled = true,
                IsArchived = false,
                ValidationStatus = StrategyExecutionRules.VersionStatus
            };
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = 1,
                DefinitionJson = StrategyTemplates.Build(strategy.Name, 1, parameters),
                Symbol = "BTCUSDT",
                Timeframe = timeframe
            });
            _db.Strategies.Add(strategy);
            existing.Add(strategy);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task RetireHiddenStrategiesAsync(List<Strategy> existing, CancellationToken cancellationToken)
    {
        var used = (await _db.Bots
                .Select(bot => bot.StrategyVersion.StrategyId)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var strategy in existing.OrderByDescending(row => row.IsEnabled))
        {
            var key = strategy.TemplateKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                var latest = strategy.Versions.OrderByDescending(row => row.VersionNumber).FirstOrDefault();
                key = latest is null ? string.Empty : StrategyTemplates.Read(latest.DefinitionJson).TemplateKey;
            }

            if (StrategyTemplateKeys.IsObsoleteAlias(key))
            {
                strategy.IsEnabled = false;
                strategy.IsArchived = true;
                continue;
            }

            if (StrategyTemplateKeys.IsKnown(key) && !StrategyTemplateKeys.IsOperatorCatalog(key))
            {
                strategy.IsEnabled = false;
                strategy.IsArchived = true;
                if (!used.Contains(strategy.Id) && strategy.DeletedAt is null)
                {
                    strategy.DeletedAt = now;
                }

                continue;
            }

            if (StrategyTemplateKeys.IsKnown(key))
            {
                if (StrategyTemplateKeys.IsCanonical(key) && !claimed.Add(StrategyTemplateKeys.Normalize(key)))
                {
                    strategy.IsEnabled = false;
                    strategy.IsArchived = true;
                    continue;
                }

                strategy.IsEnabled = true;
                strategy.IsArchived = false;
                strategy.DeletedAt = null;
                if (StrategyTemplateKeys.IsResearchOnlyFamily(key)
                    && !StrategyTemplateKeys.IsCrossSectionalReversal(key)
                    && !StrategyTemplateKeys.IsScalping(key))
                {
                    if (string.IsNullOrWhiteSpace(strategy.ValidationStatus)
                        || strategy.ValidationStatus == StrategyValidationStatuses.ValidationPending)
                    {
                        strategy.ValidationStatus = StrategyValidationStatuses.Researching;
                    }
                }
                else if (StrategyTemplateKeys.IsNearMiss(key))
                {
                    strategy.IsArchived = false;
                    strategy.DeletedAt = null;
                    if (string.IsNullOrWhiteSpace(strategy.ValidationStatus)
                        || strategy.ValidationStatus == StrategyValidationStatuses.ValidationPending)
                    {
                        strategy.ValidationStatus = StrategyValidationStatuses.NearMiss;
                    }
                }

                continue;
            }

            strategy.IsEnabled = false;
            strategy.IsArchived = true;
            if (!used.Contains(strategy.Id) && strategy.DeletedAt is null)
            {
                strategy.DeletedAt = now;
            }
        }
    }

    private static bool MatchesCatalog(Strategy strategy, string templateKey, string name) =>
        string.Equals(strategy.TemplateKey, templateKey, StringComparison.OrdinalIgnoreCase)
        || string.Equals(strategy.Name, name, StringComparison.OrdinalIgnoreCase)
        || (templateKey == StrategyTemplateKeys.EmaRsiTrend
            && string.Equals(strategy.Name, "EMA RSI Strategy", StringComparison.OrdinalIgnoreCase));

    private async Task AlignBotTimeframesAsync(CancellationToken cancellationToken)
    {
        var bots = await _db.Bots
            .Include(bot => bot.StrategyVersion)
            .ThenInclude(version => version.Strategy)
            .Where(bot => bot.DeletedAt == null && bot.StrategyVersion != null && bot.StrategyVersion.Strategy != null)
            .ToListAsync(cancellationToken);
        foreach (var bot in bots)
        {
            var frames = StrategyTemplateKeys.TimeframesFor(bot.StrategyVersion.Strategy.TemplateKey);
            if (frames.Count != 1 || !TimeframeExtensions.TryParseInterval(frames[0], out var timeframe))
            {
                continue;
            }

            if (bot.Timeframe != timeframe)
            {
                bot.Timeframe = timeframe;
            }

            if (bot.StrategyVersion.Timeframe != timeframe)
            {
                bot.StrategyVersion.Timeframe = timeframe;
                bot.StrategyVersion.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        var strategies = await _db.Strategies.Include(strategy => strategy.Versions)
            .Where(strategy => strategy.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var strategy in strategies)
        {
            var frames = StrategyTemplateKeys.TimeframesFor(strategy.TemplateKey);
            if (frames.Count != 1 || !TimeframeExtensions.TryParseInterval(frames[0], out var timeframe))
            {
                continue;
            }

            var latest = strategy.Versions.OrderByDescending(version => version.VersionNumber).FirstOrDefault();
            if (latest is not null && latest.Timeframe != timeframe)
            {
                latest.Timeframe = timeframe;
                latest.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }
    }

    /// <summary>
    /// The timeframe a catalog row is seeded on. It must equal what <see cref="AlignBotTimeframesAsync"/> enforces,
    /// or the version column and the definition JSON disagree after the next start.
    /// </summary>
    public static string SeedTimeframe(string key) => StrategyTemplateKeys.TimeframesFor(key)[0];

    private static void AlignCatalogStrategy(
        Strategy strategy,
        (string Key, string Name, string Description, bool Research) row,
        StrategyTemplateParams parameters)
    {
        if (StrategyTemplateKeys.IsCanonical(row.Key))
        {
            return;
        }

        if (row.Key == StrategyTemplateKeys.BtcEma20Ema50Long)
        {
            AlignEmaCross(strategy, row, parameters);
            return;
        }

        if (row.Key is StrategyTemplateKeys.TsMomentum285 or StrategyTemplateKeys.BtcDailyMax10)
        {
            AlignTsMomentum(strategy, row, parameters);
            return;
        }

        if (row.Key == StrategyTemplateKeys.ImpulseCatch)
        {
            strategy.TemplateKey = row.Key;
            strategy.Name = row.Name;
            strategy.AllowedSide = StrategySides.Long;
            strategy.AppliesToAllSymbols = true;
            strategy.AllowedSymbolsCsv = null;
            strategy.IsEnabled = true;
            strategy.IsArchived = false;
            strategy.DeletedAt = null;
            strategy.Description = row.Description;
            strategy.ValidationStatus = StrategyValidationStatuses.ValidationPending;
            var clock = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (clock is null || clock.IsImmutable)
            {
                strategy.Versions.Add(new StrategyVersion
                {
                    Strategy = strategy,
                    VersionNumber = (clock?.VersionNumber ?? 0) + 1,
                    DefinitionJson = StrategyTemplates.Build(strategy.Name, (clock?.VersionNumber ?? 0) + 1, parameters),
                    Symbol = "BTCUSDT",
                    Timeframe = Timeframe.FifteenMinutes
                });
            }
            else if (clock.Timeframe != Timeframe.FifteenMinutes)
            {
                clock.DefinitionJson = StrategyTemplates.Build(strategy.Name, clock.VersionNumber, parameters);
                clock.Timeframe = Timeframe.FifteenMinutes;
                clock.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return;
        }

        if (row.Key == StrategyTemplateKeys.FlowZone)
        {
            strategy.TemplateKey = row.Key;
            strategy.Name = row.Name;
            strategy.AllowedSide = StrategySides.Both;
            strategy.AppliesToAllSymbols = true;
            strategy.AllowedSymbolsCsv = null;
            strategy.IsEnabled = false;
            strategy.IsArchived = false;
            strategy.DeletedAt = null;
            strategy.Description = row.Description;
            strategy.ValidationStatus = StrategyValidationStatuses.ValidationPending;
            var clock = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
            if (clock is null || clock.IsImmutable)
            {
                strategy.Versions.Add(new StrategyVersion
                {
                    Strategy = strategy,
                    VersionNumber = (clock?.VersionNumber ?? 0) + 1,
                    DefinitionJson = StrategyTemplates.Build(strategy.Name, (clock?.VersionNumber ?? 0) + 1, parameters),
                    Symbol = "BTCUSDT",
                    Timeframe = Timeframe.OneHour
                });
            }
            else if (clock.Timeframe != Timeframe.OneHour)
            {
                clock.DefinitionJson = StrategyTemplates.Build(strategy.Name, clock.VersionNumber, parameters);
                clock.Timeframe = Timeframe.OneHour;
                clock.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return;
        }

        if (StrategyTemplateKeys.IsHistoricallyFitted(row.Key))
        {
            AlignFittedBtc15m(strategy, row, parameters);
            return;
        }

        if (StrategyTemplateKeys.IsResearchOnlyFamily(row.Key) || StrategyTemplateKeys.IsNearMiss(row.Key))
        {
            strategy.TemplateKey = row.Key;
            strategy.AllowedSide = StrategySides.Both;
            strategy.AppliesToAllSymbols = true;
            strategy.IsArchived = false;
            strategy.DeletedAt = null;
            strategy.ValidationStatus = StrategyTemplates.ResearchStatus(row.Key);
            strategy.IsEnabled = true;

            if (StrategyTemplateKeys.IsCrossSectionalReversal(row.Key))
            {
                var clock = strategy.Versions.OrderByDescending(version => version.VersionNumber).FirstOrDefault();
                if (clock is not null && !clock.IsImmutable)
                {
                    clock.Timeframe = Timeframe.FifteenMinutes;
                }
            }

            if (string.IsNullOrWhiteSpace(strategy.Description))
            {
                strategy.Description = row.Description;
            }

            return;
        }

        strategy.TemplateKey = row.Key;
        strategy.AllowedSide = parameters.AllowedSide;
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
                Timeframe = CatalogTimeframe(parameters)
            });
            return;
        }

        latest.DefinitionJson = StrategyTemplates.Build(strategy.Name, latest.VersionNumber, parameters);
        latest.Timeframe = CatalogTimeframe(parameters);
        latest.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static Timeframe CatalogTimeframe(StrategyTemplateParams parameters) =>
        TimeframeExtensions.TryParseInterval(parameters.Timeframe, out var timeframe)
            ? timeframe
            : Timeframe.FiveMinutes;

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
            "Research whether OI expansion adds information to a volume breakout. OI_SAMPLE_LIMITED. Not LIVE.", true),
        (StrategyTemplateKeys.VolSpikeEmaTrend, "BTC 15m Volume Spike EMA",
            "HISTORICALLY_FITTED_CANDIDATE. BTCUSDT 15m BOTH. RelVol spike > 1.5 with close vs EMA21. Use risk book BTC 15m Vol Spike (SL 2.50% / TP 5.00%). Not validated alpha. LIVE off.", true),
        (StrategyTemplateKeys.Bb202Break, "BTC 15m Bollinger Break",
            "HISTORICALLY_FITTED_CANDIDATE. BTCUSDT 15m BOTH. Close cross of Bollinger (20,2). Use risk book BTC 15m BB Break (SL 4.00% / TP 5.00%). Not validated alpha. LIVE off.", true),
        (StrategyTemplateKeys.FlowZone, "Flow Zone",
            "All USD-M coins, 1h, both sides. Buy the upper quarter of the last 24 hours when taker buy is the majority and open interest rose. Sell the lower quarter when taker sell is the majority and open interest rose. Missing taker or open interest sends no order. The signal closes the position when that flow leaves the zone, but not while the move is still inside a 0.20% round-trip fee. The 4% stop and 15% take are only the rail if the bot is off. Not measured on the past. Not auto-started.", true),
        (StrategyTemplateKeys.ImpulseCatch, "Impulse Catch",
            "All USD-M coins, 15m, long only. Catches the big move. Buys the first closed bar that closes at least 16% above its 24h low, including a bar that jumps well past that level. The bar is green and closes in its top half, and at least 3M USDT traded in 24h. The coin must already be up 20% in 30 days. A volume climax and a new 30-day high do not block the entry. Exits on a close 25% under the peak high or after 4 days. Book is risk 0.5%, stop 12%, take 300%, leverage 2x, 8 positions. The 2024-10 to 2026-09 result (about 37% winners, +0.6% to +3% mean) measured the older book that skipped a close above 26%, volume above 7×, and a fresh 30-day high. Those numbers do not describe this entry. Not auto-started.", true),
        (StrategyTemplateKeys.SqueezeWatch, "Squeeze Watch",
            "1h, both sides. Price moved less than 3% over 24 hours, open interest rose at least 15%, and funding is at or below -0.10% — buy the crowded shorts. The same quiet price and open-interest rise with funding at or above +0.10% — sell the crowded longs. Missing funding or open interest sends no order. While open, exit when funding leaves that extreme or price moves 2% against the entry. Book is risk 0.5%, stop 4%, take 8%, leverage 2x, 3 positions. Not measured on the past.", false),
        (StrategyTemplateKeys.FlatRange, "Flat Range",
            "ფლეტზე წინა 24 საათის ზედა და ქვედა ზღვარი იკეტება. ლონგი ქვედა 20%-ში, შორტი ზედა 20%-ში. სტოპი შესვლის ზღვარია, ტეიკ-პროფიტი მოპირდაპირე ზღვარი. პოზიციის ზომა ისე ითვლება, რომ სტოპმა დაგეგმილი რისკი წაიღოს. 24 საათში იხურება.", false),
        (StrategyTemplateKeys.ObsCompressionBreakout, "Compression Breakout 72h",
            "OBSERVATION. 1h, ორივე მხარე. ყოველ 4 საათში: თუ 24-საათიანი ვოლატილობა 720-საათიანის ნახევარზე ნაკლებია და დახურვა 168 საათის მაქსიმუმს ზემოთაა — ლონგი, მინიმუმს ქვემოთ — შორტი. 72 საათში იხურება. კატასტროფული სტოპი 2.5σ (2%–15%), ტეიკი 3× სტოპი, მარჟა მაქს. 8 USDT. კვლევაში წმინდა Sharpe უარყოფითი იყო. არ არის validated.", false),
        (StrategyTemplateKeys.ObsShockFade, "Shock Fade 24h",
            "OBSERVATION. 1h, ორივე მხარე. სანთლის დიაპაზონი ≥ 5× საშუალოზე (168 სთ) და მოცულობა ≥ 3× — მოძრაობის საწინააღმდეგოდ შესვლა. 24 საათში იხურება. კატასტროფული სტოპი 2.5σ (2%–15%), ტეიკი 3× სტოპი, მარჟა მაქს. 8 USDT. ლაივის range-shock დაცვა ასეთ სანთლებზე შესვლას ხშირად ბლოკავს. არ არის validated.", false),
        (StrategyTemplateKeys.ObsTopTraderContrarian, "Top-Trader Contrarian 72h",
            "OBSERVATION. 1h, ორივე მხარე, 00:00 UTC-ზე. ტოპ-ტრეიდერების პოზიციების long/short ფარდობის 30-დღიანი z ყველა ≥ $5M მონეტაზე. ყველაზე „მოკლე“ 20% — ლონგი, ყველაზე „გრძელი“ 20% — შორტი. 72 საათში იხურება. საჭიროა ≥ 20 მონეტა. კატასტროფული სტოპი 2.5σ (2%–15%), მარჟა მაქს. 8 USDT. არ არის validated.", false),
        (StrategyTemplateKeys.MacContrarian710, "Contrarian SMA 7/10",
            "MAc(7,10,0.01) on 5m. Fast SMA above the slow band is short. Fast SMA below the slow band is long. Protective book is risk 0.5%, stop 5%, take 5%, leverage 3x.", true),
        (StrategyTemplateKeys.ZigZagFade, "ZigZag Fade",
            "Fade a ZigZag swing break. BTC 30m: length 14, deviation 2%, ATR 1.5. ETH deviation 6%. SOL 5%. Order book is risk 0.5%, stop 4%, take 8%, leverage 3x.", true),
        (StrategyTemplateKeys.DonchianV2, "Donchian 55/5",
            "Donchian v2 daily. Entry 55, exit 5, ATR stop 1.5. Order book is risk 0.5%, stop 8%, take 30%, leverage 1x.", true),
        (StrategyTemplateKeys.BinHv45, "BinHV45",
            "Freqtrade BinHV45. 1m LONG. Close under the prior Bollinger(40, 2) lower band with a short lower wick. Exit when the close reaches 2.5% profit or 2.5% loss. A wick does not exit; the exchange stop and take own it. Not measured on this futures book.", true),
        (StrategyTemplateKeys.ClucMay72018, "Cluc May 2018",
            "Freqtrade ClucMay72018. 5m LONG. Close under EMA(50) and 98.5% of the typical-price lower band, volume below 20× the prior 30-bar mean. Exit at the middle band. ROI 1%, stop 5%.", true),
        (StrategyTemplateKeys.CombinedBinHCluc, "Combined BinH Cluc",
            "Freqtrade CombinedBinHAndCluc. 5m LONG. BinHV45 or Cluc entry. Middle-band exit only while in profit. ROI 5%, stop 5%.", true),
        (StrategyTemplateKeys.Hlhb, "HLHB",
            "Freqtrade hlhb. 4h LONG. RSI(10) of (open+close)/2 crosses 50 and EMA(5) crosses EMA(10) on the same bar, ADX above 25. Opposite cross exits. Live rail is stop 8%, take 62%, leverage 1x. The published hyperopt stop of 32% is not used.", true),
        (StrategyTemplateKeys.FAdxSma, "ADX SMA Cross",
            "Freqtrade FAdxSmaStrategy. 1h BOTH. SMA(12) crosses SMA(48) while ADX(14) is above 30. Exit when ADX falls below 30. ROI 5%, stop 5%.", true),
        (StrategyTemplateKeys.TripleSupertrend, "Triple Supertrend",
            "Freqtrade FSupertrendStrategy. 1h BOTH. Long when Supertrend 8/4, 9/7 and 8/1 are up. Short when 16/1, 18/3 and 18/6 are down. Exit long on 18/3 down, exit short on 9/7 up. Take 10%, stop 8%, leverage 1x.", true),
        (StrategyTemplateKeys.BtcEma20Ema50Long, "30m EMA Cross",
            "HISTORICALLY_FITTED_CANDIDATE. All USD-M coins, 30m LONG only. EMA20 cross above EMA50; exit on the cross back below. Use risk book 30m EMA Cross (R 0.50% / SL 1.00% / TP 3% cap). Not validated alpha. LIVE off.", true),
        (StrategyTemplateKeys.TsMomentum285, "1d Time-Series Momentum",
            "BTCUSDT daily LONG only. Buy when the 28-day return is in the top third of its own history and stay in while any of the next five days is funded. No short. VAL growth on this cache was -11%. Start it yourself on Bots in LIVE mode with an API key. Use risk book 1d Time-Series Momentum (1x, 8% stop rail, 2% planned risk). Not auto-started.", true),
        (StrategyTemplateKeys.BtcDailyMax10, "1d BTC 10-day High",
            "BTCUSDT daily LONG only. Buy the day after the close prints a 10-day high. Exit when the close is no longer that high. No short. On this cache IS growth was -1%, VAL +8%, OOS -2% after 12 bp. Start it yourself on Bots in LIVE mode with an API key. Use risk book 1d BTC 10-day High (1x, 8% stop rail, 2% planned risk). Not auto-started.", true),
        (StrategyTemplateKeys.ScalpEmaMomentum, "Scalp EMA Momentum",
            "RESEARCH_ONLY. Fast/slow EMA momentum on closed 1m–15m bars. Not in the operator catalog. LIVE off.", true),
        (StrategyTemplateKeys.ScalpVwapReclaim, "Scalp VWAP Reclaim",
            "RESEARCH_ONLY. Session VWAP reclaim after a dip. Isolated book owns SL/TP. LIVE off.", true),
        (StrategyTemplateKeys.ScalpVwapReversion, "Scalp VWAP Reversion",
            "RESEARCH_ONLY. ATR-scaled VWAP deviation fade. LIVE off.", true),
        (StrategyTemplateKeys.ScalpVwapBreakout, "Scalp VWAP Breakout",
            "RESEARCH_ONLY. VWAP-aligned breakout with relative volume. LIVE off.", true),
        (StrategyTemplateKeys.ScalpBreakoutRetest, "Scalp Breakout Retest",
            "RESEARCH_ONLY. Donchian break that fails and closes back inside. LIVE off.", true),
        (StrategyTemplateKeys.ScalpLiqSweep, "Scalp Liquidity Sweep",
            "RESEARCH_ONLY. Failed swing sweep then close back through the level. LIVE off.", true),
        (StrategyTemplateKeys.ScalpRsiPullback, "Scalp RSI Pullback",
            "RESEARCH_ONLY. Trend-aligned RSI pullback on short timeframes. LIVE off.", true),
        (StrategyTemplateKeys.ScalpRsiReversion, "Scalp RSI Reversion",
            "RESEARCH_ONLY. RSI extreme fade outside strong ADX. LIVE off.", true),
        (StrategyTemplateKeys.ScalpMacdMicro, "Scalp MACD Micro",
            "RESEARCH_ONLY. MACD histogram flip with slow EMA side. LIVE off.", true),
        (StrategyTemplateKeys.ScalpBbReversion, "Scalp Bollinger Reversion",
            "RESEARCH_ONLY. Close returns inside Bollinger after a tag. LIVE off.", true),
        (StrategyTemplateKeys.ScalpBbSqueeze, "Scalp Bollinger Squeeze",
            "RESEARCH_ONLY. Bollinger/Keltner squeeze then structure break. LIVE off.", true),
        (StrategyTemplateKeys.ScalpAtrBreakout, "Scalp ATR Breakout",
            "RESEARCH_ONLY. ATR-normalized momentum expansion. LIVE off.", true),
        (StrategyTemplateKeys.ScalpAdxTrend, "Scalp ADX Trend",
            "RESEARCH_ONLY. Supertrend + EMA + ADX trend scalp. LIVE off.", true),
        (StrategyTemplateKeys.ScalpRvolMomentum, "Scalp Relative Volume Momentum",
            "RESEARCH_ONLY. Relative-volume spike with EMA side. LIVE off.", true),
        (StrategyTemplateKeys.ScalpMarketStructure, "Scalp Market Structure",
            "RESEARCH_ONLY. Causal HH/HL or LH/LL continuation. LIVE off.", true),
        (StrategyTemplateKeys.ScalpStochMomentum, "Scalp Stochastic Momentum",
            "RESEARCH_ONLY. Stochastic %K/%D cross from an extreme. LIVE off.", true),
        (StrategyTemplateKeys.ScalpMtf, "Scalp Multi-Timeframe",
            "RESEARCH_ONLY. Last-completed HTF trend with LTF trigger. No look-ahead. LIVE off.", true),
        (StrategyTemplateKeys.ScalpSession, "Scalp Session Filter",
            "RESEARCH_ONLY. UTC session high/low as a filter, not a hardcoded session pick. LIVE off.", true),
        (StrategyTemplateKeys.ScalpTakerFlow, "Scalp Taker Flow",
            "RESEARCH_ONLY. Requires taker buy volume. Missing series = DATA_UNAVAILABLE. LIVE off.", true),
        (StrategyTemplateKeys.ScalpPriceOi, "Scalp Price Open Interest",
            "RESEARCH_ONLY. Requires open interest. Missing series = DATA_UNAVAILABLE. LIVE off.", true),
        (StrategyTemplateKeys.ScalpFundingOi, "Scalp Funding Open Interest",
            "RESEARCH_ONLY. Requires funding + OI. Missing series = DATA_UNAVAILABLE. LIVE off.", true),
        (StrategyTemplateKeys.ScalpBasis, "Scalp Basis",
            "RESEARCH_ONLY. Requires mark/index basis. Missing series = DATA_UNAVAILABLE. LIVE off.", true),
        (StrategyTemplateKeys.PaWDoubleBottom, "PA W Double Bottom",
            "RESEARCH_ONLY. Causal W / double bottom. Signal only after neckline close. Isolated book owns SL/TP. LIVE off.", true),
        (StrategyTemplateKeys.PaMDoubleTop, "PA M Double Top",
            "RESEARCH_ONLY. Causal M / double top. Signal only after neckline close. LIVE off.", true),
        (StrategyTemplateKeys.PaBullFlag, "PA Bull Flag",
            "RESEARCH_ONLY. Impulse then consolidation then measured breakout. LIVE off.", true),
        (StrategyTemplateKeys.PaBearFlag, "PA Bear Flag",
            "RESEARCH_ONLY. Impulse then consolidation then measured breakout. LIVE off.", true),
        (StrategyTemplateKeys.PaPennant, "PA Pennant",
            "RESEARCH_ONLY. Impulse plus contracting consolidation plus breakout. LIVE off.", true),
        (StrategyTemplateKeys.PaAscendingTriangle, "PA Ascending Triangle",
            "RESEARCH_ONLY. Flat highs, rising lows, measured breakout direction. LIVE off.", true),
        (StrategyTemplateKeys.PaDescendingTriangle, "PA Descending Triangle",
            "RESEARCH_ONLY. Flat lows, falling highs, measured breakout direction. LIVE off.", true),
        (StrategyTemplateKeys.PaSymmetricalTriangle, "PA Symmetrical Triangle",
            "RESEARCH_ONLY. Contracting highs and lows. Direction is measured, not assumed. LIVE off.", true),
        (StrategyTemplateKeys.PaRisingWedge, "PA Rising Wedge",
            "RESEARCH_ONLY. Rising converging bounds. Reversal and continuation researched separately. LIVE off.", true),
        (StrategyTemplateKeys.PaFallingWedge, "PA Falling Wedge",
            "RESEARCH_ONLY. Falling converging bounds. Reversal and continuation researched separately. LIVE off.", true),
        (StrategyTemplateKeys.PaRectangleBreakout, "PA Rectangle Breakout",
            "RESEARCH_ONLY. Range high/low with measured breakout. LIVE off.", true),
        (StrategyTemplateKeys.PaBreakoutRetest, "PA Breakout Retest",
            "RESEARCH_ONLY. Level break then retest acceptance. LIVE off.", true),
        (StrategyTemplateKeys.PaLiquiditySweep, "PA Liquidity Sweep",
            "RESEARCH_ONLY. Sweep of a confirmed swing then close back through the level. LIVE off.", true),
        (StrategyTemplateKeys.PaHeadShoulders, "PA Head And Shoulders",
            "RESEARCH_ONLY. Objective H&S geometry. Signal at neckline close. LIVE off.", true),
        (StrategyTemplateKeys.PaInverseHeadShoulders, "PA Inverse Head And Shoulders",
            "RESEARCH_ONLY. Objective inverse H&S. Signal at neckline close. LIVE off.", true),
        (StrategyTemplateKeys.PaCandleSequence, "PA Candle Sequence",
            "RESEARCH_ONLY. Sequence plus rejection as an event, not 3-green=long. LIVE off.", true),
        (StrategyTemplateKeys.PaStructureBreak, "PA Structure Break",
            "RESEARCH_ONLY. Causal BOS of last confirmed swing. LIVE off.", true),
        (StrategyTemplateKeys.PaFailedBreakout, "PA Failed Breakout",
            "RESEARCH_ONLY. Close beyond a range then close back inside. LIVE off.", true),
        (StrategyTemplateKeys.CrossSectionalReversalReturn15m, "Return 15m Reversal",
            "Repeatable cross-sectional reversal factor — not validated for trading. RESEARCHING. PAPER off. LIVE off.", true),
        (StrategyTemplateKeys.CrossSectionalReversalReturn1h, "Return 1h Reversal",
            "Repeatable cross-sectional reversal factor — not validated for trading. RESEARCHING. PAPER off. LIVE off.", true),
        ..NearMissCatalog()
    ];

    private static IEnumerable<(string Key, string Name, string Description, bool Research)> NearMissCatalog() =>
        NearMissAudit.SelectedRows.Select(row => (
            NearMissAudit.TemplateKey(row),
            StrategyTemplates.DisplayName(NearMissAudit.TemplateKey(row)),
            $"{row.HypothesisId} is NEAR_MISS, not validated. {NearMissAudit.StrictFailure(row)} Paper and LIVE default off.",
            true));

    private async Task AttachStrategyRiskAsync(CancellationToken cancellationToken)
    {
        var strategies = await _db.Strategies
            .Include(s => s.Versions)
            .Where(s => !s.IsArchived && s.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var profiles = await _db.RiskProfiles.Where(r => r.DeletedAt == null).ToListAsync(cancellationToken);
        var owned = strategies
            .Where(s => s.RiskProfileId is not null)
            .Select(s => s.RiskProfileId!.Value)
            .ToHashSet();

        foreach (var strategy in strategies)
        {
            if (strategy.RiskProfileId is { } existing && profiles.Any(p => p.Id == existing))
            {
                continue;
            }

            var alias = AliasRiskName(strategy.TemplateKey);
            var match = alias is null
                ? null
                : profiles.FirstOrDefault(p => string.Equals(p.Name, alias, StringComparison.OrdinalIgnoreCase) && !owned.Contains(p.Id));
            if (match is null)
            {
                match = LowBook();
                match.Name = strategy.Name;
                match.IsSystem = true;
                match.IsActive = false;
                match.AllowLive = true;
                _db.RiskProfiles.Add(match);
                profiles.Add(match);
            }

            strategy.RiskProfileId = match.Id;
            owned.Add(match.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);

        var bots = await _db.Bots.Include(b => b.StrategyVersion).ThenInclude(v => v.Strategy).ToListAsync(cancellationToken);
        foreach (var bot in bots)
        {
            var riskId = bot.StrategyVersion?.Strategy?.RiskProfileId;
            if (riskId is { } id && bot.RiskProfileId != id)
            {
                bot.RiskProfileId = id;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyPublishedRiskAsync(CancellationToken cancellationToken)
    {
        var strategies = await _db.Strategies
            .Include(s => s.RiskProfile)
            .Where(s => !s.IsArchived && s.DeletedAt == null && s.RiskProfileId != null)
            .ToListAsync(cancellationToken);
        foreach (var strategy in strategies)
        {
            if (strategy.RiskProfile is null || !PublishedRisk.TryGetValue(StrategyTemplateKeys.Normalize(strategy.TemplateKey), out var book))
            {
                continue;
            }

            strategy.RiskProfile.RiskPerTradePercent = book.Risk;
            strategy.RiskProfile.StopLossPercent = book.Stop;
            strategy.RiskProfile.TakeProfitPercent = book.Take;
            strategy.RiskProfile.MaxLeverage = book.Leverage;
            strategy.RiskProfile.MaxSimultaneousPositions = book.Positions;
            if (StrategyTemplateKeys.Normalize(strategy.TemplateKey) == StrategyTemplateKeys.ImpulseCatch)
            {
                strategy.RiskProfile.MaxPortfolioRiskPercent = 15m;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private static readonly Dictionary<string, (decimal Risk, decimal Stop, decimal Take, decimal Leverage, int Positions)> PublishedRisk = new(StringComparer.OrdinalIgnoreCase)
    {
        [StrategyTemplateKeys.EmaRsiTrend] = (0.5m, 3m, 9m, 2m, 3),
        [StrategyTemplateKeys.RsiPullback] = (0.5m, 2.5m, 5m, 2m, 3),
        [StrategyTemplateKeys.BollingerReversion] = (0.5m, 2.5m, 2m, 2m, 3),
        [StrategyTemplateKeys.SupertrendEmaTrend] = (0.5m, 3m, 9m, 2m, 3),
        [StrategyTemplateKeys.LiqSweepContinuation] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.VolSqueezeStructure] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.VwapBreakoutVolume] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.MarketStructureTrend] = (0.5m, 3.5m, 7m, 2m, 2),
        [StrategyTemplateKeys.VolSpikeEmaTrend] = (0.5m, 2.5m, 5m, 3m, 1),
        [StrategyTemplateKeys.Bb202Break] = (0.5m, 4m, 5m, 3m, 1),
        [StrategyTemplateKeys.BtcEma20Ema50Long] = (0.5m, 1m, 3m, 3m, 1),
        [StrategyTemplateKeys.TsMomentum285] = (2m, 8m, 30m, 1m, 1),
        [StrategyTemplateKeys.BtcDailyMax10] = (2m, 8m, 30m, 1m, 1),
        [StrategyTemplateKeys.FlowZone] = (0.5m, 4m, 15m, 1m, 5),
        [StrategyTemplateKeys.SqueezeWatch] = (0.5m, 4m, 8m, 2m, 3),
        [StrategyTemplateKeys.ImpulseCatch] = (0.5m, 12m, 300m, 2m, 30),
        [StrategyTemplateKeys.FlatRange] = (0.5m, 2m, 4m, 3m, 5),
        [StrategyTemplateKeys.ObsCompressionBreakout] = (0.5m, 8m, 24m, 2m, 5),
        [StrategyTemplateKeys.ObsShockFade] = (0.5m, 8m, 24m, 2m, 5),
        [StrategyTemplateKeys.ObsTopTraderContrarian] = (0.5m, 8m, 24m, 2m, 8),
        [StrategyTemplateKeys.MacContrarian710] = (0.5m, 5m, 5m, 3m, 5),
        [StrategyTemplateKeys.ZigZagFade] = (0.5m, 4m, 8m, 3m, 5),
        [StrategyTemplateKeys.DonchianV2] = (0.5m, 8m, 30m, 1m, 1),
        [StrategyTemplateKeys.BinHv45] = (0.5m, 2.5m, 2.5m, 3m, 5),
        [StrategyTemplateKeys.ClucMay72018] = (0.5m, 5m, 1m, 3m, 5),
        [StrategyTemplateKeys.CombinedBinHCluc] = (0.5m, 5m, 5m, 3m, 5),
        [StrategyTemplateKeys.Hlhb] = (0.5m, 8m, 62m, 1m, 5),
        [StrategyTemplateKeys.FAdxSma] = (0.5m, 5m, 5m, 3m, 5),
        [StrategyTemplateKeys.TripleSupertrend] = (0.5m, 8m, 10m, 1m, 5),
    };

    private static string? AliasRiskName(string? templateKey) => StrategyTemplateKeys.Normalize(templateKey) switch
    {
        StrategyTemplateKeys.VolSpikeEmaTrend => "BTC 15m Vol Spike",
        StrategyTemplateKeys.Bb202Break => "BTC 15m BB Break",
        StrategyTemplateKeys.BtcEma20Ema50Long => "30m EMA Cross",
        StrategyTemplateKeys.TsMomentum285 => "1d Time-Series Momentum",
        StrategyTemplateKeys.BtcDailyMax10 => "1d BTC 10-day High",
        StrategyTemplateKeys.FlowZone => "Flow Zone",
        StrategyTemplateKeys.ImpulseCatch => "Impulse Catch",
        _ => null
    };

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
        existing.AllowLive = true;
        if (string.Equals(name, "30m EMA Cross", StringComparison.OrdinalIgnoreCase) && existing.TakeProfitPercent == 20m)
        {
            existing.TakeProfitPercent = template.TakeProfitPercent;
        }    }

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
            MaxSimultaneousPositions = 5,
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
            AllowLive = true
        };

    private static void AlignFittedBtc15m(
        Strategy strategy,
        (string Key, string Name, string Description, bool Research) row,
        StrategyTemplateParams parameters)
    {
        strategy.TemplateKey = row.Key;
        strategy.Name = row.Name;
        strategy.AllowedSide = StrategySides.Both;
        strategy.AppliesToAllSymbols = false;
        strategy.AllowedSymbolsCsv = "BTCUSDT";
        strategy.ValidationStatus = StrategyValidationStatuses.HistoricallyFittedCandidate;
        if (string.IsNullOrWhiteSpace(strategy.Description) || strategy.Description != row.Description)
        {
            strategy.Description = row.Description;
        }

        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var current = latest is null ? null : StrategyTemplates.Read(latest.DefinitionJson);
        var mismatch = latest is null
            || current is null
            || current.TemplateKey != parameters.TemplateKey
            || current.Timeframe != "15m"
            || current.AllowedSide != StrategySides.Both
            || latest.Timeframe != Timeframe.FifteenMinutes;
        if (!mismatch)
        {
            return;
        }

        var json = StrategyTemplates.Build(strategy.Name, (latest?.VersionNumber ?? 0) + (latest is { IsImmutable: true } or null ? 1 : 0), parameters);
        if (latest is null || latest.IsImmutable)
        {
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = (latest?.VersionNumber ?? 0) + 1,
                DefinitionJson = json,
                Symbol = "BTCUSDT",
                Timeframe = Timeframe.FifteenMinutes
            });
            return;
        }

        latest.DefinitionJson = StrategyTemplates.Build(strategy.Name, latest.VersionNumber, parameters);
        latest.Timeframe = Timeframe.FifteenMinutes;
        latest.Symbol = "BTCUSDT";
        latest.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static RiskProfile FittedVolSpikeBook() =>
        new()
        {
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 2.5m,
            TakeProfitPercent = 5m,
            MaxLeverage = 3m,
            MaxDailyLossPercent = 3m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 1,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true,
            IsActive = false
        };

    private static RiskProfile FittedBbBreakBook() =>
        new()
        {
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 4m,
            TakeProfitPercent = 5m,
            MaxLeverage = 3m,
            MaxDailyLossPercent = 3m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 1,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true,
            IsActive = false
        };

    private static void AlignEmaCross(
        Strategy strategy,
        (string Key, string Name, string Description, bool Research) row,
        StrategyTemplateParams parameters)
    {
        strategy.TemplateKey = row.Key;
        strategy.Name = row.Name;
        strategy.AllowedSide = StrategySides.Long;
        strategy.AppliesToAllSymbols = true;
        strategy.AllowedSymbolsCsv = null;
        strategy.IsEnabled = true;
        strategy.ValidationStatus = StrategyValidationStatuses.HistoricallyFittedCandidate;
        if (string.IsNullOrWhiteSpace(strategy.Description) || strategy.Description != row.Description)
        {
            strategy.Description = row.Description;
        }

        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var current = latest is null ? null : StrategyTemplates.Read(latest.DefinitionJson);
        var mismatch = latest is null
            || current is null
            || current.TemplateKey != parameters.TemplateKey
            || current.Timeframe != "30m"
            || current.AllowedSide != StrategySides.Long
            || current.EmaFast != 20
            || current.EmaSlow != 50
            || latest.Timeframe != Timeframe.ThirtyMinutes;
        if (!mismatch)
        {
            return;
        }

        var json = StrategyTemplates.Build(strategy.Name, (latest?.VersionNumber ?? 0) + (latest is { IsImmutable: true } or null ? 1 : 0), parameters);
        if (latest is null || latest.IsImmutable)
        {
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = (latest?.VersionNumber ?? 0) + 1,
                DefinitionJson = json,
                Symbol = "BTCUSDT",
                Timeframe = Timeframe.ThirtyMinutes
            });
            return;
        }

        latest.DefinitionJson = StrategyTemplates.Build(strategy.Name, latest.VersionNumber, parameters);
        latest.Timeframe = Timeframe.ThirtyMinutes;
        latest.Symbol = "BTCUSDT";
        latest.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void AlignTsMomentum(
        Strategy strategy,
        (string Key, string Name, string Description, bool Research) row,
        StrategyTemplateParams parameters)
    {
        strategy.TemplateKey = row.Key;
        strategy.Name = row.Name;
        strategy.AllowedSide = StrategySides.Long;
        strategy.AppliesToAllSymbols = false;
        strategy.AllowedSymbolsCsv = "BTCUSDT";
        strategy.IsEnabled = true;
        strategy.IsArchived = false;
        strategy.DeletedAt = null;
        strategy.ValidationStatus = StrategyValidationStatuses.HistoricallyFittedCandidate;
        if (string.IsNullOrWhiteSpace(strategy.Description) || strategy.Description != row.Description)
        {
            strategy.Description = row.Description;
        }

        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var current = latest is null ? null : StrategyTemplates.Read(latest.DefinitionJson);
        var mismatch = latest is null
            || current is null
            || current.TemplateKey != parameters.TemplateKey
            || current.Timeframe != "1d"
            || current.AllowedSide != StrategySides.Long
            || latest.Timeframe != Timeframe.OneDay;
        if (!mismatch)
        {
            return;
        }

        var json = StrategyTemplates.Build(strategy.Name, (latest?.VersionNumber ?? 0) + (latest is { IsImmutable: true } or null ? 1 : 0), parameters);
        if (latest is null || latest.IsImmutable)
        {
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = (latest?.VersionNumber ?? 0) + 1,
                DefinitionJson = json,
                Symbol = "BTCUSDT",
                Timeframe = Timeframe.OneDay
            });
            return;
        }

        latest.DefinitionJson = StrategyTemplates.Build(strategy.Name, latest.VersionNumber, parameters);
        latest.Timeframe = Timeframe.OneDay;
        latest.Symbol = "BTCUSDT";
        latest.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static RiskProfile TsMomentumBook() =>
        new()
        {
            RiskPerTradePercent = 2m,
            StopLossPercent = 8m,
            TakeProfitPercent = 30m,
            MaxLeverage = 1m,
            MaxDailyLossPercent = 3m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 1,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true,
            IsActive = false
        };

    private static RiskProfile ImpulseCatchBook() =>
        new()
        {
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 12m,
            TakeProfitPercent = 300m,
            MaxLeverage = 2m,
            MaxDailyLossPercent = 3m,
            MaxPortfolioRiskPercent = 15m,
            MaxSimultaneousPositions = 30,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true,
            IsActive = false
        };

    private static RiskProfile FittedEmaCrossBook() =>
        new()
        {
            RiskPerTradePercent = 0.5m,
            StopLossPercent = 1m,
            TakeProfitPercent = 3m,
            MaxLeverage = 3m,
            MaxDailyLossPercent = 3m,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 1,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m,
            AllowLive = true,
            IsActive = false
        };
}
