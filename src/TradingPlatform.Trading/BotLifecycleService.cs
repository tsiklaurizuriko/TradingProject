using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trades;
using TradingPlatform.Domain.Trading;
using TradingPlatform.Risk;
using TradingPlatform.Strategies.Engine;

namespace TradingPlatform.Trading;

public sealed class BotLifecycleService : IBotLifecycleService
{
    public const string SamplePaperBotName = "BTCUSDT EMA RSI Paper";
    private const int WorkspaceBatchSize = 10;

    private readonly ITradingStore _store;
    private readonly IPublicMarketDataClient _market;
    private readonly IMarketDataCache _cache;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ITradingRealtimePublisher _publisher;
    private readonly IExchangeCredentialStore _credentials;
    private readonly ITradeEligibility _eligibility;
    private readonly IMarketScanner _scanner;
    private readonly TradingOptions _options;
    private readonly ILogger<BotLifecycleService> _logger;

    public BotLifecycleService(
        ITradingStore store,
        IPublicMarketDataClient market,
        IMarketDataCache cache,
        IClock clock,
        ICorrelationIdAccessor correlation,
        ITradingRealtimePublisher publisher,
        IExchangeCredentialStore credentials,
        ITradeEligibility eligibility,
        IMarketScanner scanner,
        IOptions<TradingOptions> options,
        ILogger<BotLifecycleService> logger)
    {
        _store = store;
        _market = market;
        _cache = cache;
        _clock = clock;
        _correlation = correlation;
        _publisher = publisher;
        _credentials = credentials;
        _eligibility = eligibility;
        _scanner = scanner;
        _options = options.Value;
        _logger = logger;
    }

    public static string PaperBotName(string symbol) => $"{symbol.ToUpperInvariant()} EMA RSI Paper";

    public Task<BotDto> StartSamplePaperBotAsync(Guid userId, CancellationToken cancellationToken = default) =>
        StartSymbolAsync(userId, "BTCUSDT", TradingMode.Paper, null, null, cancellationToken);

    public async Task<IReadOnlyList<BotDto>> StartTopVolumePaperBotsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _ = userId;
        var universe = await _market.GetPaperUniverseAsync(cancellationToken);
        if (universe.Count == 0)
        {
            throw new DomainException(ErrorCodes.ExchangeUnavailable, "Binance did not return the USD-M USDT futures universe.");
        }

        foreach (var ranked in universe)
        {
            await _store.UpsertSymbolAsync(
                ranked.Symbol,
                ranked.BaseAsset,
                ranked.QuoteAsset,
                ranked.TickSize,
                ranked.StepSize,
                ranked.MinQuantity,
                ranked.MinNotional,
                ranked.PricePrecision,
                ranked.QuantityPrecision,
                cancellationToken);
            _cache.SetTicker(ranked.Symbol, ranked.LastPrice, _clock.UtcNow);
        }

        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
        _logger.LogInformation("Refreshed {Count} USD-M USDT perpetual symbols. No bots were started.", universe.Count);
        return (await _store.ListBotsAsync(cancellationToken)).Select(Map).ToList();
    }

    public async Task<BotDto> StartSymbolAsync(
        Guid userId,
        string symbol,
        TradingMode mode,
        Guid? strategyId = null,
        Guid? riskProfileId = null,
        CancellationToken cancellationToken = default)
    {
        if (_options.KillSwitchEnabled)
        {
            throw new DomainException(ErrorCodes.KillSwitchActive, "Kill switch is active.");
        }

        var name = symbol.Trim().ToUpperInvariant();
        var universe = await _market.GetPaperUniverseAsync(cancellationToken);
        var ranked = universe.FirstOrDefault(s => string.Equals(s.Symbol, name, StringComparison.OrdinalIgnoreCase));
        if (ranked is null)
        {
            throw new DomainException(ErrorCodes.InvalidSymbol, $"{name} is not a Binance USD-M USDT perpetual.");
        }

        await EnsureTradeEligibleAsync(name, cancellationToken);

        if (mode is not TradingMode.Paper and not TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        var user = userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);

        var strategyVersion = strategyId is { } sid && sid != Guid.Empty
            ? await _store.GetLatestStrategyVersionAsync(sid, cancellationToken)
            : await _store.GetSampleStrategyVersionAsync(cancellationToken);
        if (strategyVersion is null)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy was not found.");
        }

        EnsureStrategyEnabled(strategyVersion.Strategy);
        if (!SymbolScope.Allows(strategyVersion.Strategy.AppliesToAllSymbols, strategyVersion.Strategy.AllowedSymbolsCsv, name))
        {
            throw new DomainException(
                ErrorCodes.InvalidSymbol,
                $"{strategyVersion.Strategy.Name} is not assigned to {name}. Open Strategies and add this coin, or set the strategy to all coins.");
        }

        var risk = await ResolveRiskAsync(strategyVersion.Strategy.RiskProfileId ?? riskProfileId, cancellationToken);
        RiskLiveGuard.EnsureAllowed(mode, risk);

        Domain.Exchanges.ExchangeAccount account;
        if (mode == TradingMode.Live)
        {
            var live = await _credentials.GetLiveAccountAsync(user.Id, cancellationToken)
                ?? throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before starting a live bot.");
            var keys = await _credentials.GetAsync(live.Id, cancellationToken);
            if (keys is null)
            {
                throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before starting a live bot.");
            }

            account = live;
        }
        else
        {
            account = await _store.GetOrCreatePaperAccountAsync(user.Id, cancellationToken);
            await _store.GetOrCreateBalanceAsync(account.Id, null, "USDT", TradingMode.Paper, _options.PaperDefaultBalance, cancellationToken);
        }

        await _store.UpsertSymbolAsync(
            ranked.Symbol,
            ranked.BaseAsset,
            ranked.QuoteAsset,
            ranked.TickSize,
            ranked.StepSize,
            ranked.MinQuantity,
            ranked.MinNotional,
            ranked.PricePrecision,
            ranked.QuantityPrecision,
            cancellationToken);
        _cache.SetTicker(ranked.Symbol, ranked.LastPrice, _clock.UtcNow);

        var bot = await _store.FindBotBySymbolAsync(user.Id, name, mode, strategyVersion.StrategyId, null, cancellationToken);
        if (bot is null)
        {
            var display = UsdtSpotUniverse.DisplayNameOf(name);
            bot = new Bot
            {
                UserId = user.Id,
                ExchangeAccountId = account.Id,
                StrategyVersionId = strategyVersion.Id,
                StrategyVersion = strategyVersion,
                RiskProfileId = risk.Id,
                RiskProfile = risk,
                Name = $"{display} {strategyVersion.Strategy.Name} {risk.Name} {mode}",
                Status = BotStatus.Created,
                Mode = mode,
                Symbol = name,
                Timeframe = strategyVersion.Timeframe
            };
            await _store.AddBotAsync(bot, cancellationToken);
            await _store.SaveChangesAsync(cancellationToken);
            bot = await _store.FindBotBySymbolAsync(user.Id, name, mode, strategyVersion.StrategyId, risk.Id, cancellationToken)
                ?? throw new DomainException(ErrorCodes.BotNotFound, "Bot was not created.");
        }
        else if (bot.Status != BotStatus.Running)
        {
            bot.StrategyVersionId = strategyVersion.Id;
            bot.StrategyVersion = strategyVersion;
            bot.Timeframe = strategyVersion.Timeframe;
            bot.RiskProfileId = risk.Id;
            bot.RiskProfile = risk;
        }

        if (bot.Status == BotStatus.Running)
        {
            throw new DomainException(ErrorCodes.BotAlreadyRunning, $"{name} is already running.");
        }

        var mapped = await StartWithoutConflictAsync(bot, cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
        _logger.LogInformation("Started {Mode} bot for {Symbol} only. Other coins stay stopped.", mode, name);
        return mapped;
    }

    public async Task<CreateBotsResult> CreateSymbolBotsAsync(
        Guid userId,
        TradingMode mode,
        Guid strategyId,
        Guid riskProfileId,
        IReadOnlyList<string> symbols,
        CancellationToken cancellationToken = default)
    {
        if (mode is not TradingMode.Paper and not TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        var requested = (symbols ?? [])
            .Select(symbol => symbol.Trim().ToUpperInvariant())
            .Where(symbol => symbol.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requested.Length == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Pick at least one coin.");
        }

        var user = userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);

        var strategyVersion = await _store.GetLatestStrategyVersionAsync(strategyId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy was not found.");
        EnsureStrategyEnabled(strategyVersion.Strategy);
        var risk = await ResolveRiskAsync(strategyVersion.Strategy.RiskProfileId ?? riskProfileId, cancellationToken);
        RiskLiveGuard.EnsureAllowed(mode, risk);

        Domain.Exchanges.ExchangeAccount account;
        if (mode == TradingMode.Live)
        {
            var live = await _credentials.GetLiveAccountAsync(user.Id, cancellationToken)
                ?? throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before creating a live bot.");
            var keys = await _credentials.GetAsync(live.Id, cancellationToken);
            if (keys is null)
            {
                throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before creating a live bot.");
            }

            account = live;
        }
        else
        {
            account = await _store.GetOrCreatePaperAccountAsync(user.Id, cancellationToken);
            await _store.GetOrCreateBalanceAsync(account.Id, null, "USDT", TradingMode.Paper, _options.PaperDefaultBalance, cancellationToken);
        }

        var universe = (await _market.GetPaperUniverseAsync(cancellationToken))
            .ToDictionary(row => row.Symbol, StringComparer.OrdinalIgnoreCase);
        var existing = (await _store.ListBotsAsync(cancellationToken))
            .Where(bot => bot.UserId == user.Id && bot.Mode == mode)
            .ToList();
        var created = 0;
        var skipped = 0;

        foreach (var name in requested)
        {
            if (!universe.TryGetValue(name, out var ranked))
            {
                skipped++;
                continue;
            }

            if (!SymbolScope.Allows(strategyVersion.Strategy.AppliesToAllSymbols, strategyVersion.Strategy.AllowedSymbolsCsv, name))
            {
                skipped++;
                continue;
            }

            var already = existing.Any(bot =>
                string.Equals(bot.Symbol, name, StringComparison.OrdinalIgnoreCase)
                && bot.StrategyVersion.StrategyId == strategyVersion.StrategyId);
            if (already)
            {
                skipped++;
                continue;
            }

            await _store.UpsertSymbolAsync(
                ranked.Symbol,
                ranked.BaseAsset,
                ranked.QuoteAsset,
                ranked.TickSize,
                ranked.StepSize,
                ranked.MinQuantity,
                ranked.MinNotional,
                ranked.PricePrecision,
                ranked.QuantityPrecision,
                cancellationToken);

            var display = UsdtSpotUniverse.DisplayNameOf(name);
            var bot = new Bot
            {
                UserId = user.Id,
                ExchangeAccountId = account.Id,
                StrategyVersionId = strategyVersion.Id,
                StrategyVersion = strategyVersion,
                RiskProfileId = risk.Id,
                RiskProfile = risk,
                Name = $"{display} {strategyVersion.Strategy.Name} {risk.Name} {mode}",
                Status = BotStatus.Created,
                Mode = mode,
                Symbol = name,
                Timeframe = strategyVersion.Timeframe,
                LastError = "Created. Start it when you want it to trade."
            };
            await _store.AddBotAsync(bot, cancellationToken);
            existing.Add(bot);
            created++;
        }

        if (created > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
            await _publisher.PublishOverviewAsync(cancellationToken);
        }

        _logger.LogInformation("Created {Created} {Mode} bots, skipped {Skipped}. None were started.", created, mode, skipped);
        return new CreateBotsResult(created, skipped);
    }

    public async Task<BotDto> StartAsync(Guid botId, CancellationToken cancellationToken = default)
    {
        if (_options.KillSwitchEnabled)
        {
            throw new DomainException(ErrorCodes.KillSwitchActive, "Kill switch is active.");
        }

        var bot = await _store.GetBotAsync(botId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.BotNotFound, "Bot was not found.");

        if (bot.Mode == TradingMode.Live)
        {
            var keys = await _credentials.GetAsync(bot.ExchangeAccountId, cancellationToken);
            if (keys is null)
            {
                throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before starting a live bot.");
            }

            RiskLiveGuard.EnsureAllowed(bot.Mode, bot.RiskProfile ?? await _store.GetConservativeRiskAsync(cancellationToken));
        }
        else if (bot.Mode != TradingMode.Paper)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, "Only paper or live bots can be started.");
        }

        if (bot.Status == BotStatus.Running)
        {
            throw new DomainException(ErrorCodes.BotAlreadyRunning, "Bot is already running.");
        }

        await AttachLatestStrategyAsync(bot, cancellationToken);
        var mapped = await StartWithoutConflictAsync(bot, cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
        return mapped;
    }

    public async Task<StartBotsResult> StartAllIdleAsync(
        Guid userId,
        TradingMode mode,
        Guid? preferredStrategyId = null,
        CancellationToken cancellationToken = default)
    {
        _ = preferredStrategyId;
        if (_options.KillSwitchEnabled)
        {
            throw new DomainException(ErrorCodes.KillSwitchActive, "Kill switch is active.");
        }

        if (mode is not TradingMode.Paper and not TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        var user = userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);

        if (mode == TradingMode.Live)
        {
            var live = await _credentials.GetLiveAccountAsync(user.Id, cancellationToken)
                ?? throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before starting a live bot.");
            var keys = await _credentials.GetAsync(live.Id, cancellationToken);
            if (keys is null)
            {
                throw new DomainException(ErrorCodes.LiveTradingDisabled, "Save a Binance API key on Exchanges before starting a live bot.");
            }

            RiskLiveGuard.EnsureAllowed(mode, await _store.GetConservativeRiskAsync(cancellationToken));
        }

        var bots = (await _store.ListWorkspaceBotsAsync(user.Id, mode, cancellationToken))
            .Where(bot => bot.Status != BotStatus.Running)
            .OrderBy(bot => bot.Symbol)
            .ThenBy(bot => bot.Name)
            .ToList();

        var started = 0;
        var failed = 0;
        string? detail = null;
        var dirty = 0;
        foreach (var bot in bots)
        {
            try
            {
                await AttachLatestStrategyAsync(bot, cancellationToken);
                await MarkRunningAsync(bot, cancellationToken);
                started++;
                dirty++;
            }
            catch (Exception ex)
            {
                failed++;
                detail = ex.Message;
                _logger.LogWarning(ex, "Start-all skipped {Mode} bot {BotId} {Symbol}", mode, bot.Id, bot.Symbol);
            }

            if (dirty >= WorkspaceBatchSize)
            {
                await FlushWorkspaceProgressAsync(cancellationToken);
                dirty = 0;
            }
        }

        if (dirty > 0)
        {
            await FlushWorkspaceProgressAsync(cancellationToken);
        }

        _logger.LogInformation("Start-all {Mode}: started {Started}, failed {Failed}.", mode, started, failed);
        return new StartBotsResult(started, failed, detail);
    }

    public async Task<StopBotsResult> StopAllRunningAsync(Guid userId, TradingMode mode, CancellationToken cancellationToken = default)
    {
        if (mode is not TradingMode.Paper and not TradingMode.Live)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Use Paper or Live.");
        }

        var user = userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);

        var bots = (await _store.ListWorkspaceBotsAsync(user.Id, mode, cancellationToken))
            .Where(bot => bot.Status == BotStatus.Running)
            .OrderBy(bot => bot.Symbol)
            .ThenBy(bot => bot.Id)
            .ToList();

        var stopped = 0;
        var failed = 0;
        string? detail = null;
        var dirty = 0;
        var now = _clock.UtcNow;
        foreach (var bot in bots)
        {
            try
            {
                bot.Status = BotStatus.Stopped;
                bot.StoppedAt = now;
                bot.LastError = "Stopped. Open positions were left in place.";
                stopped++;
                dirty++;
            }
            catch (Exception ex)
            {
                failed++;
                detail = ex.Message;
                _logger.LogWarning(ex, "Stop-all skipped {Mode} bot {BotId} {Symbol}", mode, bot.Id, bot.Symbol);
            }

            if (dirty >= WorkspaceBatchSize)
            {
                await FlushWorkspaceProgressAsync(cancellationToken);
                dirty = 0;
            }
        }

        if (dirty > 0)
        {
            await FlushWorkspaceProgressAsync(cancellationToken);
        }

        _logger.LogInformation("Stop-all {Mode}: stopped {Stopped}, failed {Failed}. Positions were not closed.", mode, stopped, failed);
        return new StopBotsResult(stopped, failed, detail);
    }

    private async Task FlushWorkspaceProgressAsync(CancellationToken cancellationToken)
    {
        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
    }

    private async Task AttachLatestStrategyAsync(Bot bot, CancellationToken cancellationToken)
    {
        var latest = await _store.GetLatestStrategyVersionAsync(bot.StrategyVersion.StrategyId, cancellationToken);
        EnsureStrategyEnabled(latest?.Strategy ?? bot.StrategyVersion.Strategy);
        if (latest is null || latest.Id == bot.StrategyVersionId)
        {
            return;
        }

        bot.StrategyVersionId = latest.Id;
        bot.StrategyVersion = latest;
        bot.Timeframe = latest.Timeframe;
    }

    private async Task MarkRunningAsync(Bot bot, CancellationToken cancellationToken)
    {
        bot.Status = BotStatus.Running;
        bot.StartedAt = _clock.UtcNow;
        bot.StoppedAt = null;
        bot.LastError = bot.Mode == TradingMode.Live
            ? "Waiting for the first live cycle. No order is sent until the strategy fires."
            : "Waiting for the first paper cycle.";
        bot.StrategyVersion.IsImmutable = true;
        bot.StrategyVersion.FirstUsedAt ??= _clock.UtcNow;
        await _store.AddBotRunAsync(new BotRun
        {
            BotId = bot.Id,
            Status = BotStatus.Running,
            StartedAt = _clock.UtcNow,
            CorrelationId = _correlation.GetOrCreate()
        }, cancellationToken);
    }

    private async Task<BotDto> StartWithoutConflictAsync(Bot bot, CancellationToken cancellationToken)
    {
        await MarkRunningAsync(bot, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishBotAsync(bot.Id, bot.Status.ToString(), cancellationToken);
        return Map(bot);
    }

    public async Task<BotDto> StopAsync(Guid botId, CancellationToken cancellationToken = default)
    {
        var bot = await _store.GetBotAsync(botId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.BotNotFound, "Bot was not found.");
        bot.Status = BotStatus.Stopped;
        bot.StoppedAt = _clock.UtcNow;
        bot.LastError = "Stopped.";
        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishBotAsync(bot.Id, bot.Status.ToString(), cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
        return Map(bot);
    }

    public async Task<DeleteBotsResult> DeleteBotsAsync(
        IReadOnlyList<Guid> ids,
        TradingMode? requiredMode,
        CancellationToken cancellationToken = default)
    {
        var requested = (ids ?? [])
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (requested.Length == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Pick at least one bot to delete.");
        }

        var deleted = 0;
        var skipped = 0;
        var now = _clock.UtcNow;
        foreach (var id in requested)
        {
            var bot = await _store.GetBotAsync(id, cancellationToken);
            if (bot is null || (requiredMode is { } mode && bot.Mode != mode))
            {
                skipped++;
                continue;
            }

            if (bot.Status == BotStatus.Running)
            {
                bot.Status = BotStatus.Stopped;
                bot.StoppedAt = now;
            }

            bot.DeletedAt = now;
            bot.LastError = "Deleted. Open positions were left in place.";
            deleted++;
            await _publisher.PublishBotAsync(bot.Id, BotStatus.Stopped.ToString(), cancellationToken);
        }

        if (deleted > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
            await _publisher.PublishOverviewAsync(cancellationToken);
        }

        _logger.LogInformation("Deleted {Deleted} bots, skipped {Skipped}. Positions were not closed.", deleted, skipped);
        return new DeleteBotsResult(deleted, skipped);
    }

    public async Task EmergencyStopAsync(CancellationToken cancellationToken = default)
    {
        await _store.StopAllRunningBotsAsync("Emergency stop.", cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);
        await _publisher.PublishOverviewAsync(cancellationToken);
    }

    private async Task EnsureTradeEligibleAsync(string symbol, CancellationToken cancellationToken)
    {
        IReadOnlyList<MarketScanRow> scan;
        try
        {
            scan = await _scanner.ScanAsync(cancellationToken);
        }
        catch
        {
            scan = [];
        }

        var row = scan.FirstOrDefault(item => string.Equals(item.Contract.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        var decision = row is not null
            ? _eligibility.Evaluate(row)
            : _eligibility.Evaluate(
                (await _market.GetPaperUniverseAsync(cancellationToken))
                    .FirstOrDefault(item => string.Equals(item.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                ?? throw new DomainException(ErrorCodes.InvalidSymbol, $"{symbol} is not a Binance USD-M USDT perpetual."));
        if (!decision.Eligible)
        {
            throw new DomainException(
                ErrorCodes.ValidationFailed,
                $"{symbol} is watched but not currently eligible to trade. {decision.Reason}");
        }
    }

    internal static BotDto Map(Bot bot) =>
        new(
            bot.Id,
            bot.Name,
            bot.Status.ToString(),
            bot.Mode.ToString(),
            bot.Symbol,
            UsdtSpotUniverse.DisplayNameOf(bot.Symbol),
            0,
            bot.Timeframe.ToBinanceInterval(),
            bot.StrategyVersion.Strategy.Name,
            bot.StrategyVersion.VersionNumber,
            bot.RiskProfile.Name,
            bot.LastError,
            bot.StartedAt,
            bot.StrategyVersion.StrategyId,
            bot.RiskProfileId);

    private async Task<RiskProfile> ResolveRiskAsync(Guid? riskProfileId, CancellationToken cancellationToken)
    {
        if (riskProfileId is { } id && id != Guid.Empty)
        {
            var selected = await _store.GetRiskProfileByIdAsync(id, cancellationToken);
            if (selected is not null)
            {
                return selected;
            }
        }

        return await _store.GetConservativeRiskAsync(cancellationToken);
    }

    public async Task<PriceActionArmDto> GetPriceActionArmAsync(CancellationToken cancellationToken = default)
    {
        await ReloadPriceActionArmAsync(cancellationToken);
        return MapArm(await _store.ListStrategiesAsync(cancellationToken));
    }

    public async Task<PriceActionArmDto> SetPriceActionArmAsync(
        SetPriceActionArmRequest request,
        CancellationToken cancellationToken = default)
    {
        await ReloadPriceActionArmAsync(cancellationToken);
        var block = PriceActionArm.RejectLive(_options.LiveTradingEnabled, request.LiveEnabled);
        if (block is not null)
        {
            throw new DomainException(ErrorCodes.LiveTradingDisabled, block);
        }

        var price = _options.PriceAction;
        if (request.Enabled is bool enabled)
        {
            price.Enabled = enabled;
            await SaveArmAsync(PriceActionArm.EnabledKey, enabled, "Operator Price Action master. Default off. Does not start bots.", cancellationToken);
        }

        if (request.PaperEnabled is bool paper)
        {
            price.PaperEnabled = paper;
            await SaveArmAsync(PriceActionArm.PaperKey, paper, "Operator Price Action paper. Default off. Does not start bots.", cancellationToken);
        }

        if (request.LiveEnabled is bool live)
        {
            price.LiveEnabled = live;
            await SaveArmAsync(PriceActionArm.LiveKey, live, "Operator Price Action LIVE. Default off. Does not start bots.", cancellationToken);
        }

        var strategies = await _store.ListStrategiesAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(request.TemplateKey))
        {
            var key = StrategyTemplateKeys.Normalize(request.TemplateKey);
            if (!StrategyTemplateKeys.IsNearMiss(key))
            {
                throw new DomainException(ErrorCodes.ValidationFailed, "Only a near-miss strategy can be armed here.");
            }

            var on = request.CandidateEnabled == true;
            price.Candidates ??= new Dictionary<string, NearMissCandidateOptions>(StringComparer.OrdinalIgnoreCase);
            price.Candidates[key] = new NearMissCandidateOptions { Enabled = on };
            if (on && !price.Enabled)
            {
                price.Enabled = true;
                await SaveArmAsync(PriceActionArm.EnabledKey, true, "Operator Price Action master. Default off. Does not start bots.", cancellationToken);
            }

            await SaveArmAsync(
                PriceActionArm.CandidateKey(key),
                on,
                "Operator near-miss candidate. Default off. Does not start a bot.",
                cancellationToken);
            var strategy = strategies.FirstOrDefault(row =>
                string.Equals(row.TemplateKey, key, StringComparison.OrdinalIgnoreCase));
            if (strategy is not null)
            {
                strategy.IsEnabled = on;
            }
        }

        await _store.SaveChangesAsync(cancellationToken);
        return MapArm(strategies);
    }

    private async Task ReloadPriceActionArmAsync(CancellationToken cancellationToken)
    {
        var settings = await _store.GetSettingsAsync("Trading.PriceAction.", cancellationToken);
        PriceActionArm.Apply(_options.PriceAction, settings);
    }

    private Task SaveArmAsync(string key, bool value, string description, CancellationToken cancellationToken) =>
        _store.SetSettingAsync(key, value ? "true" : "false", description, cancellationToken);

    private PriceActionArmDto MapArm(IReadOnlyList<Strategy> strategies)
    {
        var price = _options.PriceAction;
        var candidates = StrategyTemplateKeys.NearMiss.Select(key =>
        {
            var hypothesis = StrategyTemplateKeys.NearMissHypothesisId(key);
            var failure = NearMissAudit.SelectedRows
                .Where(row => string.Equals(row.HypothesisId, hypothesis, StringComparison.Ordinal))
                .Select(NearMissAudit.StrictFailure)
                .FirstOrDefault() ?? "";
            var strategy = strategies.FirstOrDefault(row =>
                string.Equals(row.TemplateKey, key, StringComparison.OrdinalIgnoreCase));
            return new PriceActionCandidateArmDto(
                key,
                StrategyTemplates.DisplayName(key),
                hypothesis,
                failure,
                NearMissGate.CandidateEnabled(price, key),
                strategy?.IsEnabled ?? false,
                strategy?.Id);
        }).ToArray();

        return new PriceActionArmDto(
            price.Enabled,
            price.PaperEnabled,
            price.LiveEnabled,
            _options.LiveTradingEnabled,
            candidates);
    }

    private static void EnsureStrategyEnabled(Strategy strategy)
    {
        if (!strategy.IsEnabled)
        {
            throw new DomainException(
                ErrorCodes.StrategyInvalid,
                $"{strategy.Name} is disabled. Enable it on Strategies. The template is not deleted.");
        }
    }
}

public sealed class TradingQueryService : ITradingQueryService
{
    private const int OverviewLedgerLimit = 120;

    private readonly ITradingStore _store;
    private readonly IMarketDataCache _cache;
    private readonly IPublicMarketDataClient _market;
    private readonly ILiveAccountCache _live;
    private readonly IExchangeAccountService _accounts;
    private readonly IStrategyEngine _strategy;
    private readonly StrategyDefinitionValidator _validator;
    private readonly LiveIsolatedReconciler _reconcile;
    private readonly TradingOptions _options;

    public TradingQueryService(
        ITradingStore store,
        IMarketDataCache cache,
        IPublicMarketDataClient market,
        ILiveAccountCache live,
        IExchangeAccountService accounts,
        IStrategyEngine strategy,
        StrategyDefinitionValidator validator,
        LiveIsolatedReconciler reconcile,
        IOptions<TradingOptions> options)
    {
        _store = store;
        _cache = cache;
        _market = market;
        _live = live;
        _accounts = accounts;
        _strategy = strategy;
        _validator = validator;
        _reconcile = reconcile;
        _options = options.Value;
    }

    public async Task<PortfolioDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var bots = (await GetBotsAsync(cancellationToken)).ToList();
        var tradeRows = IsolatedOccupancy.UniqueClosedTrips(
            await _store.GetRecentTradesAsync(OverviewLedgerLimit, cancellationToken),
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees);
        var trades = tradeRows.Select(MapTrade).ToList();
        var signals = (await _store.GetRecentSignalsAsync(20, cancellationToken))
            .Select(s => new SignalDto(s.Id, s.BotId, s.Symbol, s.SignalType.ToString(), s.Price, s.Reason, s.Timestamp))
            .ToList();

        var balances = await _store.GetPaperBalancesAsync(cancellationToken);
        IReadOnlyList<RankedUsdtSpotSymbol> universe;
        try
        {
            universe = await _market.GetPaperUniverseAsync(cancellationToken);
        }
        catch
        {
            universe = [];
        }

        foreach (var row in universe)
        {
            _cache.SetTicker(row.Symbol, row.LastPrice, DateTimeOffset.UtcNow);
        }

        var tickers = (universe.Count > 0
                ? universe.Take(40).Select((s, i) => new TickerDto(
                    s.Symbol,
                    UsdtSpotUniverse.DisplayNameOf(s.Symbol),
                    i + 1,
                    s.LastPrice,
                    DateTimeOffset.UtcNow))
                : _cache.GetTickers().Select(t => new TickerDto(
                    t.Symbol,
                    UsdtSpotUniverse.DisplayNameOf(t.Symbol),
                    0,
                    t.Price,
                    t.Timestamp)))
            .ToList();

        await RefreshLiveCacheIfStaleAsync(cancellationToken);
        await _reconcile.ReconcileAsync(cancellationToken);
        var live = _live.Current;
        var books = await GetPositionsAsync(cancellationToken);
        var positions = IsolatedOccupancy.MergeBotAndExchange(
            books,
            live.OpenPositions,
            MapExchangePosition,
            IsolatedOccupancy.HasFreshFuturesBook(live),
            DateTimeOffset.UtcNow);
        positions = await StampMissingIsolatedProtectionAsync(positions, bots, cancellationToken);
        var orders = MapOrders(
            await _store.GetRecentOrdersAsync(OverviewLedgerLimit, cancellationToken),
            tradeRows).ToList();
        foreach (var liveOrder in live.OpenOrders)
        {
            if (orders.Any(existing => OrderLedger.Same(
                    existing.ClientOrderId,
                    existing.ExchangeOrderId,
                    liveOrder.ClientOrderId,
                    liveOrder.ExchangeOrderId)))
            {
                continue;
            }

            orders.Add(MapExchangeOrder(liveOrder));
        }

        var paperFree = balances.Where(b => b.Asset == "USDT").Sum(b => b.Free);
        var paperUsdt = balances.Where(b => b.Asset == "USDT").Sum(b => b.Free + b.Locked);
        var paperBotIds = bots.Where(b => !string.Equals(b.Mode, "Live", StringComparison.OrdinalIgnoreCase)).Select(b => b.Id).ToHashSet();
        var liveBotIds = bots.Where(b => string.Equals(b.Mode, "Live", StringComparison.OrdinalIgnoreCase)).Select(b => b.Id).ToHashSet();
        var paperUnrealized = positions.Where(p => paperBotIds.Contains(p.BotId)).Sum(p => p.UnrealizedPnL);
        var paperEquity = paperUsdt + paperUnrealized;
        var liveBotUnrealized = positions.Where(p => liveBotIds.Contains(p.BotId) || p.Source == "Binance").Sum(p => p.UnrealizedPnL);
        var liveUnrealized = liveBotUnrealized;
        var todayStart = new DateTimeOffset(DateTime.UtcNow.Date, TimeSpan.Zero);
        var paperRealized = await _store.SumClosedPnLSinceForModeAsync(TradingMode.Paper, DateTimeOffset.UnixEpoch, cancellationToken);
        var paperTodays = await _store.SumClosedPnLSinceForModeAsync(TradingMode.Paper, todayStart, cancellationToken) + paperUnrealized;
        var liveTodays = await _store.SumClosedPnLSinceForModeAsync(TradingMode.Live, todayStart, cancellationToken) + liveUnrealized;

        var liveAvailable = live.UsdtFree ?? live.FuturesUsdt;
        var liveEquity = live.HasKeys
            ? live.SpotUsdt + live.FundingUsdt + (live.FuturesEquity > 0m ? live.FuturesEquity : live.FuturesUsdt)
            : 0m;

        TickerDto? ticker = tickers.FirstOrDefault(t => t.Symbol == "BTCUSDT") ?? tickers.FirstOrDefault();

        return new PortfolioDto(
            paperEquity > 0m || balances.Count > 0 ? paperEquity : _options.PaperDefaultBalance,
            paperFree > 0m || balances.Count > 0 ? paperFree : _options.PaperDefaultBalance,
            paperUnrealized,
            paperRealized,
            paperTodays,
            live.HasKeys && live.CanTrade,
            ticker is null ? "Waiting for Binance public ticker" : "Healthy",
            ticker,
            tickers,
            bots,
            positions,
            orders,
            trades,
            signals,
            live.HasKeys,
            live.CanTrade,
            liveEquity,
            liveAvailable,
            liveUnrealized,
            liveTodays,
            live.SpotUsdt,
            live.FundingUsdt,
            live.FuturesUsdt,
            live.Message,
            books);
    }

    private async Task RefreshLiveCacheIfStaleAsync(CancellationToken cancellationToken)
    {
        var current = _live.Current;
        if (current.UpdatedAt is { } at && DateTimeOffset.UtcNow - at < TimeSpan.FromSeconds(20))
        {
            return;
        }

        try
        {
            await _accounts.GetStatusAsync(Guid.Empty, cancellationToken);
        }
        catch
        {
            /* keep last snapshot; paper numbers stay paper */
        }
    }

    private decimal MarkToMarket(IEnumerable<(string Asset, decimal Quantity)> holdings)
    {
        decimal value = 0m;
        foreach (var (asset, quantity) in holdings)
        {
            if (quantity <= 0m)
            {
                continue;
            }

            if (string.Equals(asset, "USDT", StringComparison.OrdinalIgnoreCase))
            {
                value += quantity;
                continue;
            }

            if (_cache.TryGetTicker($"{asset}USDT", out var px))
            {
                value += quantity * px;
            }
        }

        return value;
    }

    private async Task<List<PositionDto>> StampMissingIsolatedProtectionAsync(
        List<PositionDto> positions,
        IReadOnlyList<BotDto> bots,
        CancellationToken cancellationToken)
    {
        if (positions.TrueForAll(row => row.StopLossPrice > 0m && row.MarginUsdt > 0m))
        {
            return positions;
        }

        var books = (await _store.ListRiskProfilesAsync(cancellationToken))
            .ToDictionary(row => row.Id);
        var active = books.Values.FirstOrDefault(row => row.IsActive)
            ?? await _store.GetConservativeRiskAsync(cancellationToken);
        var liveBots = bots
            .Where(bot => string.Equals(bot.Mode, "Live", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var stamped = new List<PositionDto>(positions.Count);
        foreach (var row in positions)
        {
            if (row.StopLossPrice > 0m && row.MarginUsdt > 0m)
            {
                stamped.Add(row);
                continue;
            }

            var bot = liveBots.FirstOrDefault(item => item.Id == row.BotId)
                ?? UniqueRunningBot(liveBots, row.Symbol);
            var book = bot is not null && books.TryGetValue(bot.RiskProfileId, out var matched)
                ? matched
                : active;
            var market = await _store.GetSymbolAsync(row.Symbol, cancellationToken);
            try
            {
                stamped.Add(IsolatedOccupancy.StampProtection(
                    row,
                    book.StopLossPercent,
                    book.TakeProfitPercent,
                    book.RiskPerTradePercent,
                    book.MaxLeverage,
                    market?.TickSize ?? 0m,
                    bot?.Id));
            }
            catch (Exception)
            {
                stamped.Add(row);
            }
        }

        return stamped;
    }

    private static BotDto? UniqueRunningBot(IReadOnlyList<BotDto> bots, string symbol)
    {
        var running = bots
            .Where(bot =>
                bot.Status == "Running"
                && string.Equals(bot.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (running.Count == 0)
        {
            return null;
        }

        var strategies = running.Select(bot => bot.StrategyId).Distinct().ToList();
        return strategies.Count == 1 ? running[0] : null;
    }

    private static PositionDto MapExchangePosition(LiveOpenPosition position) =>
        new(
            StableGuid($"pos:{position.Venue}:{position.Symbol}:{position.Side}"),
            Guid.Empty,
            position.Symbol,
            position.Side,
            position.Quantity,
            position.EntryPrice,
            position.MarkPrice,
            position.UnrealizedPnL,
            0m,
            0m,
            DateTimeOffset.UtcNow,
            "Binance",
            0m);

    private static OrderDto MapExchangeOrder(LiveOpenOrder order) =>
        new(
            StableGuid($"ord:{order.Venue}:{order.ExchangeOrderId}:{order.ClientOrderId}"),
            order.ClientOrderId ?? order.ExchangeOrderId ?? "binance",
            order.ExchangeOrderId,
            Guid.Empty,
            order.Symbol,
            order.Side,
            order.Type,
            order.Price is 0m ? null : order.Price,
            order.Quantity,
            order.FilledQuantity,
            order.Status,
            order.CreatedAt,
            "Binance",
            null,
            null,
            "Live",
            OrderLedger.Kind(order.Type));

    private static Guid StableGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<IReadOnlyList<BotDto>> GetBotsAsync(CancellationToken cancellationToken = default) =>
        UsdtSpotUniverse.OrderBySymbol(
            (await _store.ListBotsAsync(cancellationToken)).Select(BotLifecycleService.Map),
            b => b.Symbol);

    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _store.GetRecentOrdersAsync(2000, cancellationToken);
        var trades = await _store.GetRecentTradesAsync(2000, cancellationToken);
        return MapOrders(orders, trades);
    }

    private static List<OrderDto> MapOrders(IReadOnlyList<Order> orders, IReadOnlyList<Trade> trades)
    {
        var pnlByExit = trades
            .Where(t => t.ExitOrderId is not null && t.ClosedAt is not null)
            .GroupBy(t => t.ExitOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.First().PnL);

        return orders
            .Select(o =>
            {
                var fill = FillLedger(o);
                return new OrderDto(
                    o.Id,
                    o.ClientOrderId,
                    o.ExchangeOrderId,
                    o.BotId,
                    o.Symbol,
                    o.Side.ToString(),
                    o.Type.ToString(),
                    o.AverageFillPrice is > 0m ? o.AverageFillPrice : o.Price > 0m ? o.Price : null,
                    o.Quantity,
                    o.FilledQuantity,
                    o.Status.ToString(),
                    o.ExchangeTimestamp ?? o.CreatedAt,
                    "Bot",
                    fill.PnL ?? (pnlByExit.TryGetValue(o.Id, out var pnl) ? pnl : null),
                    fill.Fee ?? (o.Executions.Count == 0 ? null : o.Executions.Sum(e => e.Fee)),
                    o.Mode.ToString(),
                    OrderLedger.Kind(o.Type.ToString()));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<PositionDto>> GetPositionsAsync(CancellationToken cancellationToken = default) =>
        (await _store.GetOpenPositionsAsync(cancellationToken))
        .Select(p => new PositionDto(
            p.Id,
            p.BotId,
            p.Symbol,
            p.Side.ToString(),
            p.Quantity,
            p.AverageEntryPrice,
            p.CurrentPrice,
            p.UnrealizedPnL,
            p.RealizedPnL,
            p.Fees,
            p.OpenedAt,
            "Bot",
            p.InitialRiskUsdt,
            p.MarginUsdt,
            p.NotionalUsdt > 0m ? p.NotionalUsdt : p.Quantity * p.AverageEntryPrice,
            p.Leverage,
            p.StopLossPercent,
            p.TakeProfitPercent,
            p.StopLossPrice,
            p.TakeProfitPrice,
            p.LiquidationPrice,
            p.RiskPerTradePercent))
        .ToList();

    public async Task<IReadOnlyList<TradeDto>> GetTradesAsync(CancellationToken cancellationToken = default) =>
        IsolatedOccupancy.UniqueClosedTrips(
            await _store.GetRecentTradesAsync(2000, cancellationToken),
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees)
        .Select(MapTrade)
        .ToList();

    public async Task<PerformanceDto> GetPerformanceAsync(string mode, CancellationToken cancellationToken = default)
    {
        var tradingMode = string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase) ? TradingMode.Live : TradingMode.Paper;
        var modeLabel = tradingMode == TradingMode.Live ? "Live" : "Paper";
        var rawTrades = await _store.GetPerformanceTradesAsync(tradingMode, cancellationToken);
        var rows = IsolatedOccupancy.UniqueClosedTrips(
            rawTrades,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees);
        var strategyTrades = IsolatedOccupancy.UniqueClosedTrips(
            rawTrades,
            t => t.Symbol,
            t => t.Quantity,
            t => t.OpenedAt,
            t => t.ClosedAt,
            t => t.CorrelationId,
            t => t.Fees,
            t => t.StrategyName);
        var bots = (await GetBotsAsync(cancellationToken))
            .Where(b => string.Equals(b.Mode, modeLabel, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await RefreshLiveCacheIfStaleAsync(cancellationToken);
        await _reconcile.ReconcileAsync(cancellationToken);
        var live = _live.Current;
        var positions = (await _store.GetOpenPositionsForModeAsync(tradingMode, cancellationToken)).ToList();
        var liveAuth = tradingMode == TradingMode.Live && IsolatedOccupancy.HasFreshFuturesBook(live);
        var botUnrealized = positions.Sum(p => p.UnrealizedPnL);
        var unrealized = tradingMode == TradingMode.Live
            ? IsolatedOccupancy.UniqueUnrealized(positions, live.OpenPositions, liveAuth)
            : botUnrealized;
        var openPositions = tradingMode == TradingMode.Live
            ? IsolatedOccupancy.UniqueCoins(positions, live.OpenPositions, liveAuth)
            : positions.Count;

        var strategyResults = BuildStrategyResults(strategyTrades, positions, bots);
        return BuildPerformance(
            modeLabel,
            rows,
            bots,
            unrealized,
            openPositions,
            tradingMode == TradingMode.Paper ? _options.PaperDefaultBalance : 0m,
            strategyResults);
    }

    private static (decimal? PnL, decimal? Fee) FillLedger(Order order)
    {
        const string prefix = "binance-fill:";
        var value = order.CorrelationId;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return (null, null);
        }

        var parts = value[prefix.Length..].Split(':', 2);
        decimal? pnl = decimal.TryParse(parts[0], CultureInfo.InvariantCulture, out var parsedPnL) ? parsedPnL : null;
        decimal? fee = parts.Length > 1 && decimal.TryParse(parts[1], CultureInfo.InvariantCulture, out var parsedFee)
            ? parsedFee
            : null;
        return (pnl, fee);
    }

    private static TradeDto MapTrade(Trade t) =>
        new(
            t.Id,
            t.BotId,
            t.Symbol,
            t.Quantity,
            t.EntryPrice,
            t.ExitPrice,
            t.PnL,
            t.PnLPercent,
            t.Fees,
            t.OpenedAt,
            t.ClosedAt,
            t.Bot?.Mode.ToString() ?? "Paper",
            t.Side == OrderSide.Sell ? "Short" : "Long");

    private static TradeDto MapTrade(PerformanceTradeRow t) =>
        new(
            t.Id,
            t.BotId,
            t.Symbol,
            t.Quantity,
            t.EntryPrice,
            t.ExitPrice,
            t.PnL,
            t.PnLPercent,
            t.Fees,
            t.OpenedAt,
            t.ClosedAt,
            t.Mode,
            string.IsNullOrWhiteSpace(t.Side) ? "Long" : t.Side);

    private static List<StrategyResultDto> BuildStrategyResults(
        IReadOnlyList<PerformanceTradeRow> uniqueTrips,
        IReadOnlyList<Position> positions,
        IReadOnlyList<BotDto> bots)
    {
        var nameByBot = bots.ToDictionary(
            bot => bot.Id,
            bot => string.IsNullOrWhiteSpace(bot.StrategyName) ? "Strategy" : bot.StrategyName);
        var map = new Dictionary<string, (int Entries, int Wins, int Losses, int Open, decimal Realized, decimal Unrealized)>(StringComparer.OrdinalIgnoreCase);

        (int Entries, int Wins, int Losses, int Open, decimal Realized, decimal Unrealized) Slot(string name)
        {
            if (!map.TryGetValue(name, out var slot))
            {
                slot = (0, 0, 0, 0, 0m, 0m);
            }

            return slot;
        }

        foreach (var trip in uniqueTrips)
        {
            var name = string.IsNullOrWhiteSpace(trip.StrategyName) ? "Strategy" : trip.StrategyName;
            var slot = Slot(name);
            slot.Entries++;
            if (trip.ClosedAt is null)
            {
                slot.Open++;
            }
            else if (trip.PnL > 0m)
            {
                slot.Wins++;
                slot.Realized += trip.PnL;
            }
            else if (trip.PnL < 0m)
            {
                slot.Losses++;
                slot.Realized += trip.PnL;
            }
            else
            {
                slot.Realized += trip.PnL;
            }

            map[name] = slot;
        }

        foreach (var position in positions)
        {
            if (position.Quantity <= 0m || !nameByBot.TryGetValue(position.BotId, out var name))
            {
                continue;
            }

            var slot = Slot(name);
            slot.Unrealized += position.UnrealizedPnL;
            map[name] = slot;
        }

        return map
            .Select(pair => new StrategyResultDto(
                pair.Key,
                pair.Value.Entries,
                pair.Value.Wins,
                pair.Value.Losses,
                pair.Value.Open,
                RoundPerf(pair.Value.Realized),
                RoundPerf(pair.Value.Unrealized)))
            .OrderByDescending(row => row.Entries)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PerformanceDto BuildPerformance(
        string mode,
        IReadOnlyList<PerformanceTradeRow> rows,
        IReadOnlyList<BotDto> bots,
        decimal unrealized,
        int openPositions,
        decimal startingEquity,
        IReadOnlyList<StrategyResultDto> strategyResults)
    {
        var closed = rows.Where(t => t.ClosedAt is not null).OrderBy(t => t.ClosedAt).ToList();
        var openTrades = rows.Count(t => t.ClosedAt is null);
        var wins = closed.Where(t => t.PnL > 0m).ToList();
        var losses = closed.Where(t => t.PnL < 0m).ToList();
        var realized = closed.Sum(t => t.PnL);
        var fees = rows.Sum(t => t.Fees);
        var grossWins = wins.Sum(t => t.PnL);
        var grossLoss = Math.Abs(losses.Sum(t => t.PnL));
        var weekStart = DateTimeOffset.UtcNow.AddDays(-7);
        var todayKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var weekClosed = closed.Count(t => t.ClosedAt >= weekStart);
        var todayClosed = closed.Where(t => t.ClosedAt is { } at && at.UtcDateTime.Date == DateTime.UtcNow.Date).Sum(t => t.PnL);
        var winRate = closed.Count == 0 ? 0m : RoundPerf((decimal)wins.Count / closed.Count * 100m);
        var expectancy = closed.Count == 0 ? 0m : RoundPerf(realized / closed.Count);
        var profitFactor = grossLoss == 0m ? (grossWins > 0m ? 999m : 0m) : RoundPerf(grossWins / grossLoss);
        var avgWin = wins.Count == 0 ? 0m : RoundPerf(grossWins / wins.Count);
        var avgLoss = losses.Count == 0 ? 0m : RoundPerf(losses.Sum(t => t.PnL) / losses.Count);
        var best = closed.Count == 0 ? 0m : RoundPerf(closed.Max(t => t.PnL));
        var worst = closed.Count == 0 ? 0m : RoundPerf(closed.Min(t => t.PnL));
        var holds = closed
            .Where(t => t.ClosedAt is not null)
            .Select(t => (t.ClosedAt!.Value - t.OpenedAt).TotalHours)
            .ToList();
        double? avgHold = holds.Count == 0 ? null : Math.Round(holds.Average(), 2);
        var (winStreak, lossStreak) = Streaks(closed);
        var days = PerformanceDays(closed, unrealized, todayKey);
        var net = realized + unrealized;
        var ret = startingEquity > 0m ? RoundPerf(net / startingEquity * 100m) : 0m;
        var monthStart = new DateTimeOffset(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var monthClosed = closed.Where(t => t.ClosedAt >= monthStart).Sum(t => t.PnL);
        var botCounts = bots
            .GroupBy(b => string.IsNullOrWhiteSpace(b.StrategyName) ? "Strategy" : b.StrategyName)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        return new PerformanceDto(
            mode,
            closed.Count,
            openTrades,
            openPositions,
            wins.Count,
            losses.Count,
            weekClosed,
            RoundPerf(realized),
            RoundPerf(unrealized),
            RoundPerf(todayClosed + unrealized),
            RoundPerf(fees),
            winRate,
            expectancy,
            profitFactor,
            avgWin,
            avgLoss,
            best,
            worst,
            MaxDrawdown(closed),
            ret,
            avgHold,
            winStreak,
            lossStreak,
            days,
            Slices(closed, t => string.IsNullOrWhiteSpace(t.StrategyName) ? "Strategy" : t.StrategyName, botCounts),
            Slices(closed, t => t.Symbol, null),
            rows.Take(40).Select(MapTrade).ToList(),
            RoundPerf(monthClosed + unrealized),
            strategyResults);
    }

    private static IReadOnlyList<PerformanceDayDto> PerformanceDays(
        IReadOnlyList<PerformanceTradeRow> closed,
        decimal unrealized,
        string todayKey)
    {
        var start = DateTime.UtcNow.Date.AddDays(-6);
        var buckets = Enumerable.Range(0, 7)
            .Select(i => start.AddDays(i).ToString("yyyy-MM-dd"))
            .ToDictionary(k => k, _ => 0m);
        foreach (var trade in closed)
        {
            var key = trade.ClosedAt!.Value.UtcDateTime.Date.ToString("yyyy-MM-dd");
            if (buckets.ContainsKey(key))
            {
                buckets[key] += trade.PnL;
            }
        }

        if (buckets.ContainsKey(todayKey))
        {
            buckets[todayKey] += unrealized;
        }

        decimal cumulative = 0m;
        return buckets.Select(pair =>
        {
            cumulative += pair.Value;
            return new PerformanceDayDto(pair.Key, RoundPerf(pair.Value), RoundPerf(cumulative));
        }).ToList();
    }

    private static IReadOnlyList<PerformanceSliceDto> Slices(
        IReadOnlyList<PerformanceTradeRow> closed,
        Func<PerformanceTradeRow, string> key,
        IReadOnlyDictionary<string, int>? bots)
    {
        var groups = closed
            .GroupBy(key)
            .Select(g =>
            {
                var realized = g.Sum(t => t.PnL);
                var wins = g.Count(t => t.PnL > 0m);
                var n = g.Count();
                return new PerformanceSliceDto(
                    g.Key,
                    n,
                    wins,
                    RoundPerf(realized),
                    n == 0 ? 0m : RoundPerf((decimal)wins / n * 100m),
                    n == 0 ? 0m : RoundPerf(realized / n),
                    bots is not null && bots.TryGetValue(g.Key, out var count) ? count : 0);
            })
            .OrderByDescending(s => Math.Abs(s.Realized))
            .ThenByDescending(s => s.Closed)
            .Take(8)
            .ToList();

        if (bots is null || groups.Count > 0)
        {
            return groups;
        }

        return bots
            .OrderByDescending(pair => pair.Value)
            .Take(8)
            .Select(pair => new PerformanceSliceDto(pair.Key, 0, 0, 0m, 0m, 0m, pair.Value))
            .ToList();
    }

    private static (int WinStreak, int LossStreak) Streaks(IReadOnlyList<PerformanceTradeRow> closedNewestLast)
    {
        var newestFirst = closedNewestLast.AsEnumerable().Reverse();
        var win = 0;
        var loss = 0;
        foreach (var trade in newestFirst)
        {
            if (trade.PnL > 0m)
            {
                if (loss > 0)
                {
                    break;
                }

                win++;
            }
            else if (trade.PnL < 0m)
            {
                if (win > 0)
                {
                    break;
                }

                loss++;
            }
            else
            {
                break;
            }
        }

        return (win, loss);
    }

    private static decimal MaxDrawdown(IReadOnlyList<PerformanceTradeRow> closedOldestFirst)
    {
        decimal equity = 0m;
        decimal peak = 0m;
        decimal maxDd = 0m;
        foreach (var trade in closedOldestFirst)
        {
            equity += trade.PnL;
            if (equity > peak)
            {
                peak = equity;
            }

            var dd = peak - equity;
            if (dd > maxDd)
            {
                maxDd = dd;
            }
        }

        return RoundPerf(maxDd);
    }

    private static decimal RoundPerf(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public async Task<RiskProfileDto> GetRiskProfileAsync(string? mode = null, CancellationToken cancellationToken = default)
    {
        _ = mode;
        return MapRisk(await _store.GetConservativeRiskAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<StrategyDto>> GetStrategiesAsync(string? mode = null, CancellationToken cancellationToken = default)
    {
        _ = mode;
        return (await _store.ListStrategiesAsync(cancellationToken))
            .Select(row => MapStrategy(row, _options))
            .ToList();
    }

    public async Task<StrategyPreviewDto> PreviewStrategyAsync(
        Guid strategyId,
        string? symbol,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        var strategy = await _store.GetStrategyAsync(strategyId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy was not found.");
        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault()
            ?? throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy has no version to preview.");
        var definition = _validator.Parse(latest.DefinitionJson);
        var coin = (symbol ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(coin))
        {
            coin = strategy.AppliesToAllSymbols
                ? "BTCUSDT"
                : SymbolScope.Parse(strategy.AllowedSymbolsCsv).FirstOrDefault() ?? "BTCUSDT";
        }

        var wanted = limit ?? 80;
        if (string.Equals(strategy.TemplateKey, StrategyTemplateKeys.TsMomentum285, StringComparison.OrdinalIgnoreCase)
            || string.Equals(strategy.TemplateKey, StrategyTemplateKeys.BtcDailyMax10, StringComparison.OrdinalIgnoreCase))
        {
            wanted = Math.Max(wanted, 500);
        }

        var bars = Math.Clamp(wanted, 20, 1500);
        var candles = await _market.GetClosedKlinesAsync(coin, latest.Timeframe, bars, cancellationToken);
        var series = new List<StrategyPreviewBarDto>();
        var open = false;
        var side = PositionSide.Long;
        var lastSignal = SignalType.NoAction;
        var lastReason = candles.Count == 0 ? "No closed candles yet." : "Waiting for enough closed candles.";
        for (var i = 1; i < candles.Count; i++)
        {
            var window = candles.Take(i + 1).ToList();
            lastSignal = _strategy.Evaluate(
                definition,
                new StrategyContext
                {
                    ClosedCandles = window,
                    CurrentPrice = window[^1].Close,
                    HasOpenPosition = open,
                    AverageEntryPrice = open ? window[^1].Close : null,
                    PositionSide = side
                },
                out lastReason);
            if (!open && lastSignal is SignalType.Buy or SignalType.Sell)
            {
                open = true;
                side = lastSignal == SignalType.Sell ? PositionSide.Short : PositionSide.Long;
            }
            else if (open && (lastSignal is SignalType.Exit
                || (side == PositionSide.Long && lastSignal == SignalType.Sell)
                || (side == PositionSide.Short && lastSignal == SignalType.Buy)))
            {
                open = false;
            }

            series.Add(new StrategyPreviewBarDto(window[^1].CloseTime, lastSignal.ToString(), window[^1].Close, lastReason));
        }

        var parsed = StrategyTemplates.Read(latest.DefinitionJson);
        return new StrategyPreviewDto(
            strategy.Id,
            strategy.Name,
            parsed.TemplateKey,
            coin,
            (latest.Timeframe).ToBinanceInterval(),
            lastSignal.ToString(),
            lastReason,
            series.TakeLast(40).ToList());
    }

    public async Task<IReadOnlyList<RiskProfileDto>> GetRiskProfilesAsync(string? mode = null, CancellationToken cancellationToken = default)
    {
        _ = mode;
        return (await _store.ListRiskProfilesAsync(cancellationToken))
            .Select(MapRisk)
            .ToList();
    }

    public async Task<StrategyDto> UpdateStrategyScopeAsync(
        Guid strategyId,
        bool appliesToAll,
        IEnumerable<string>? symbols,
        CancellationToken cancellationToken = default)
    {
        var strategy = await _store.GetStrategyAsync(strategyId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy was not found.");
        ApplyStrategyScope(strategy, appliesToAll, symbols);
        await _store.SaveChangesAsync(cancellationToken);
        return MapStrategy(strategy, _options);
    }

    public async Task<RiskProfileDto> UpdateRiskScopeAsync(
        Guid riskProfileId,
        bool appliesToAll,
        IEnumerable<string>? symbols,
        CancellationToken cancellationToken = default)
    {
        var risk = await _store.GetRiskProfileByIdAsync(riskProfileId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile was not found.");
        return MapRisk(risk);
    }

    public async Task<StrategyDto> CreateStrategyAsync(
        Guid userId,
        SaveStrategyRequest request,
        string? mode = null,
        CancellationToken cancellationToken = default)
    {
        _ = mode;
        var user = userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);
        await EnsureUniqueStrategyNameAsync(request.Name, null, cancellationToken);
        if (!TimeframeExtensions.TryParseInterval(request.Timeframe, out var timeframe))
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, $"Unknown timeframe '{request.Timeframe}'.");
        }

        var parameters = StrategyTemplates.Validate(ToTemplateParams(request) with { Timeframe = timeframe.ToBinanceInterval() });
        var crossSection = StrategyTemplateKeys.IsCrossSectionalReversal(parameters.TemplateKey);

        var strategy = new Strategy
        {
            UserId = user.Id,
            User = user,
            Name = request.Name.Trim(),
            Description = (request.Description ?? string.Empty).Trim(),
            TemplateKey = parameters.TemplateKey,
            AllowedSide = parameters.AllowedSide,
            IsEnabled = true,
            ValidationStatus = crossSection
                ? StrategyValidationStatuses.Researching
                : StrategyValidationStatuses.ValidationPending
        };
        ApplyStrategyScope(strategy, request.AppliesToAllSymbols, request.Symbols);
        var risk = new RiskProfile
        {
            Name = strategy.Name,
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
            IsActive = false,
            IsSystem = true
        };
        strategy.RiskProfile = risk;
        strategy.RiskProfileId = risk.Id;
        await _store.AddRiskProfileAsync(risk, cancellationToken);
        strategy.Versions.Add(new StrategyVersion
        {
            Strategy = strategy,
            VersionNumber = 1,
            DefinitionJson = StrategyTemplates.Build(strategy.Name, 1, parameters),
            Symbol = "BTCUSDT",
            Timeframe = timeframe
        });
        await _store.AddStrategyAsync(strategy, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);
        return MapStrategy(strategy, _options);
    }

    public async Task<StrategyDto> UpdateStrategyAsync(
        Guid strategyId,
        SaveStrategyRequest request,
        CancellationToken cancellationToken = default)
    {
        var strategy = await _store.GetStrategyAsync(strategyId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy was not found.");
        await EnsureUniqueStrategyNameAsync(request.Name, strategy.Id, cancellationToken);
        if (!TimeframeExtensions.TryParseInterval(request.Timeframe, out var timeframe))
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, $"Unknown timeframe '{request.Timeframe}'.");
        }

        var parameters = StrategyTemplates.Validate(ToTemplateParams(request) with { Timeframe = timeframe.ToBinanceInterval() });

        strategy.Name = request.Name.Trim();
        strategy.Description = (request.Description ?? string.Empty).Trim();
        strategy.TemplateKey = parameters.TemplateKey;
        strategy.AllowedSide = parameters.AllowedSide;
        ApplyStrategyScope(strategy, request.AppliesToAllSymbols, request.Symbols);

        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var definitionChanged = latest is null
            || latest.Timeframe != timeframe
            || StrategyTemplates.Read(latest.DefinitionJson) != parameters;
        if (latest is null || (definitionChanged && latest.IsImmutable))
        {
            var versionNumber = (latest?.VersionNumber ?? 0) + 1;
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = versionNumber,
                DefinitionJson = StrategyTemplates.Build(strategy.Name, versionNumber, parameters),
                Symbol = "BTCUSDT",
                Timeframe = timeframe
            });
        }
        else if (definitionChanged)
        {
            latest.DefinitionJson = StrategyTemplates.Build(strategy.Name, latest.VersionNumber, parameters);
            latest.Timeframe = timeframe;
        }

        await _store.SaveChangesAsync(cancellationToken);
        return MapStrategy(strategy, _options);
    }

    public async Task<StrategyDto> SetStrategyEnabledAsync(
        Guid strategyId,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var strategy = await _store.GetStrategyAsync(strategyId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy was not found.");
        if (enabled && strategy.IsArchived)
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, $"{strategy.Name} is archived.");
        }
        strategy.IsEnabled = enabled;
        await _store.SaveChangesAsync(cancellationToken);
        return MapStrategy(strategy, _options);
    }

    public async Task<RiskProfileDto> CreateRiskProfileAsync(
        SaveRiskProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new DomainException(ErrorCodes.ValidationFailed, "Use LOW, MEDIUM, or HIGH. Extra risk books are not allowed.");
    }

    public async Task<RiskProfileDto> UpdateRiskProfileAsync(
        Guid riskProfileId,
        SaveRiskProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var risk = await _store.GetRiskProfileByIdAsync(riskProfileId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile was not found.");
        ApplyRiskValues(risk, request);
        await _store.SaveChangesAsync(cancellationToken);
        return MapRisk(risk);
    }

    public async Task<RiskProfileDto> ActivateRiskProfileAsync(Guid riskProfileId, CancellationToken cancellationToken = default)
    {
        var books = await _store.ListRiskProfilesAsync(cancellationToken);
        var selected = books.FirstOrDefault(r => r.Id == riskProfileId)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile was not found.");
        foreach (var book in books)
        {
            book.IsActive = book.Id == selected.Id;
        }

        await _store.SaveChangesAsync(cancellationToken);
        return MapRisk(selected);
    }

    public async Task<RiskPreviewDto> PreviewRiskAsync(string mode, decimal price, CancellationToken cancellationToken = default)
    {
        var profile = await _store.GetConservativeRiskAsync(cancellationToken);
        decimal available;
        if (string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase))
        {
            await RefreshLiveCacheIfStaleAsync(cancellationToken);
            available = _live.Current.UsdtFree ?? _live.Current.FuturesUsdt;
        }
        else
        {
            var user = await _store.GetFirstAdminAsync(cancellationToken);
            var account = await _store.GetOrCreatePaperAccountAsync(user.Id, cancellationToken);
            var usdt = await _store.GetOrCreateBalanceAsync(
                account.Id,
                null,
                "USDT",
                TradingMode.Paper,
                _options.PaperDefaultBalance,
                cancellationToken);
            available = usdt.Free;
        }

        var entry = price > 0m ? price : 100_000m;
        var open = await _store.GetOpenPositionsForModeAsync(
            string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase) ? TradingMode.Live : TradingMode.Paper,
            cancellationToken);
        var openRisk = available > 0m ? open.Sum(p => p.InitialRiskUsdt) / available * 100m : 0m;
        var plan = RiskEngine.Plan(profile, available, entry, PositionSide.Long, openRisk, null);
        return new RiskPreviewDto(
            profile.Name,
            plan.AvailableBalance,
            plan.RiskPerTradePercent,
            plan.RiskAmount,
            plan.Price,
            plan.StopLossPercent,
            plan.StopLossPrice,
            plan.TakeProfitPercent,
            plan.TakeProfitPrice,
            plan.PositionNotional,
            plan.Leverage,
            plan.IsolatedMargin,
            plan.EstimatedFee,
            plan.EstimatedEntryFee,
            plan.EstimatedExitFee,
            plan.EstimatedSlippage,
            plan.EstimatedTotalRisk,
            plan.LiquidationPrice,
            plan.PortfolioRiskBefore,
            plan.PortfolioRiskAfter,
            plan.Allowed,
            plan.Reason);
    }

    private async Task EnsureUniqueStrategyNameAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, "Strategy name is required.");
        }

        var exists = (await _store.ListStrategiesAsync(cancellationToken))
            .Any(row => row.Id != exceptId && string.Equals(row.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A strategy with this name already exists.");
        }
    }

    private static void ApplyStrategyScope(Strategy strategy, bool appliesToAll, IEnumerable<string>? symbols)
    {
        var list = SymbolScope.Parse(SymbolScope.Join(symbols));
        if (!appliesToAll && list.Count == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Pick at least one coin, or set this strategy to all USD-M USDT perpetuals.");
        }

        strategy.AppliesToAllSymbols = appliesToAll;
        strategy.AllowedSymbolsCsv = appliesToAll ? null : SymbolScope.Join(list);
    }

    private static void ApplyRiskValues(RiskProfile risk, SaveRiskProfileRequest request)
    {
        if (request.RiskPerTradePercent <= 0m || request.RiskPerTradePercent > 10m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Risk per trade % must be between 0 and 10.");
        }

        if (request.StopLossPercent <= 0m || request.TakeProfitPercent <= 0m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Stop loss and take profit percents must be greater than zero.");
        }

        if (request.MaxLeverage < 1m || request.MaxLeverage > 20m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Isolated leverage must be between 1x and 20x.");
        }

        if (request.MaxPortfolioRiskPercent <= 0m || request.MaxPortfolioRiskPercent > 50m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Maximum portfolio planned risk must be between 0% and 50%.");
        }

        if (request.MaxSimultaneousPositions < 1 || request.MaxSimultaneousPositions > 20)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Maximum simultaneous positions per strategy must be between 1 and 20.");
        }

        if (request.MaxConsecutiveLosses < 1 || request.MaxConsecutiveLosses > 50)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Consecutive loss limit must be between 1 and 50.");
        }

        if (request.CooldownMinutes < 1 || request.CooldownMinutes > 1440)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Cooldown must be between 1 and 1440 minutes.");
        }

        if (request.MinimumLiquidationSafetyBufferPercent < 0.1m || request.MinimumLiquidationSafetyBufferPercent > 20m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Liquidation safety buffer must be between 0.1% and 20%.");
        }

        risk.RiskPerTradePercent = request.RiskPerTradePercent;
        risk.StopLossPercent = request.StopLossPercent;
        risk.TakeProfitPercent = request.TakeProfitPercent;
        risk.MaxLeverage = request.MaxLeverage;
        risk.MaxDailyLossPercent = request.MaxDailyLossPercent;
        risk.MaxPortfolioRiskPercent = request.MaxPortfolioRiskPercent;
        risk.MaxSimultaneousPositions = request.MaxSimultaneousPositions;
        risk.MaxConsecutiveLosses = request.MaxConsecutiveLosses;
        risk.CooldownMinutes = request.CooldownMinutes;
        risk.MinimumLiquidationSafetyBufferPercent = request.MinimumLiquidationSafetyBufferPercent;
        risk.AllowLive = request.AllowLive;
    }

    private static StrategyTemplateParams ToTemplateParams(SaveStrategyRequest request) =>
        new(
            request.TemplateKey,
            request.AllowedSide,
            request.Timeframe,
            request.EmaFast,
            request.EmaSlow,
            request.RsiPeriod,
            request.RsiMinimum,
            request.RsiLongMax,
            request.RsiOversold,
            request.RsiOverbought,
            request.MacdFast,
            request.MacdSlow,
            request.MacdSignal,
            request.BbPeriod,
            request.BbStdDev,
            request.DonchianLength,
            new StrategyQualityParams(
                request.RequireVolume,
                request.VolumeLookback,
                request.MinAtrPercent,
                request.MaxAtrPercent),
            request.EntryLookback,
            request.ExitLookback,
            request.AtrPeriod,
            request.AtrStopMultiplier,
            request.TrendEmaPeriod,
            request.VolumeFilterEnabled,
            request.RelativeVolumePeriod,
            request.MinimumRelativeVolume,
            request.MaxVwapDistanceAtr,
            request.StopAtrMultiplier,
            request.VolatilityLookback,
            request.CompressionPercentile,
            request.AtrExpansionLookback,
            request.BreakoutRelativeVolume,
            request.SupertrendPeriod,
            request.SupertrendMultiplier,
            request.AdxPeriod,
            request.MinimumAdx);

    private static void EnsureStrategyEnabled(Strategy strategy)
    {
        if (!strategy.IsEnabled)
        {
            throw new DomainException(
                ErrorCodes.StrategyInvalid,
                $"{strategy.Name} is disabled. Enable it on Strategies. The template is not deleted.");
        }
    }

    private static StrategyDto MapStrategy(Strategy strategy, TradingOptions? options = null)
    {
        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var parsed = StrategyTemplates.Read(latest?.DefinitionJson);
        var template = StrategyTemplateKeys.Normalize(
            string.IsNullOrWhiteSpace(strategy.TemplateKey) ? parsed.TemplateKey : strategy.TemplateKey);
        var side = StrategySides.Normalize(
            string.IsNullOrWhiteSpace(strategy.AllowedSide) ? parsed.AllowedSide : strategy.AllowedSide);
        var quality = parsed.Quality ?? new StrategyQualityParams();
        return new StrategyDto(
            strategy.Id,
            strategy.Name,
            strategy.Description,
            latest?.VersionNumber ?? 1,
            (latest?.Timeframe ?? Timeframe.FiveMinutes).ToBinanceInterval(),
            strategy.AppliesToAllSymbols,
            SymbolScope.Parse(strategy.AllowedSymbolsCsv),
            template,
            StrategyTemplates.DisplayName(template),
            side,
            StrategyTemplates.Blurb(template),
            parsed.EmaFast,
            parsed.EmaSlow,
            parsed.RsiPeriod,
            parsed.RsiMinimum,
            parsed.RsiLongMax,
            parsed.RsiOversold,
            parsed.RsiOverbought,
            parsed.MacdFast,
            parsed.MacdSlow,
            parsed.MacdSignal,
            parsed.BbPeriod,
            parsed.BbStdDev,
            parsed.DonchianLength,
            quality.RequireVolume,
            quality.VolumeLookback,
            quality.MinAtrPercent,
            quality.MaxAtrPercent,
            latest?.IsImmutable ?? false,
            strategy.IsEnabled,
            string.IsNullOrWhiteSpace(strategy.ValidationStatus)
                ? StrategyValidationStatuses.ValidationPending
                : strategy.ValidationStatus,
            StrategyTemplateKeys.TimeframesFor(template),
            StrategyTemplateKeys.DirectionsFor(template),
            StrategyTemplates.DataDependencies(template),
            parsed.EntryLookback,
            parsed.ExitLookback,
            parsed.AtrPeriod,
            parsed.AtrStopMultiplier,
            parsed.TrendEmaPeriod,
            parsed.VolumeFilterEnabled,
            parsed.RelativeVolumePeriod,
            parsed.MinimumRelativeVolume,
            parsed.MaxVwapDistanceAtr,
            parsed.StopAtrMultiplier,
            parsed.VolatilityLookback,
            parsed.CompressionPercentile,
            parsed.AtrExpansionLookback,
            parsed.BreakoutRelativeVolume,
            parsed.SupertrendPeriod,
            parsed.SupertrendMultiplier,
            parsed.AdxPeriod,
            parsed.MinimumAdx,
            StrategyTemplateKeys.Family(template),
            StrategyTemplateKeys.IsNearMiss(template),
            StrategyTemplateKeys.IsNearMiss(template)
                && options?.PriceAction.Enabled == true
                && options.PriceAction.PaperEnabled
                && NearMissGate.CandidateEnabled(options.PriceAction, template),
            StrategyTemplateKeys.IsNearMiss(template)
                && options?.PriceAction.Enabled == true
                && options.PriceAction.LiveEnabled
                && NearMissGate.CandidateEnabled(options.PriceAction, template),
            StrategyTemplateKeys.NearMissHypothesisId(template),
            strategy.RiskProfileId,
            strategy.RiskProfile?.StopLossPercent ?? 0m,
            strategy.RiskProfile?.TakeProfitPercent ?? 0m,
            strategy.RiskProfile?.RiskPerTradePercent ?? 0m,
            strategy.RiskProfile?.MaxLeverage ?? 0m,
            strategy.RiskProfile?.MaxSimultaneousPositions ?? 0,
            strategy.RiskProfile?.MaxConsecutiveLosses ?? 0,
            strategy.RiskProfile?.CooldownMinutes ?? 0);
    }

    private static RiskProfileDto MapRisk(RiskProfile risk) =>
        new(
            risk.Id,
            risk.Name,
            risk.RiskPerTradePercent,
            risk.StopLossPercent,
            risk.TakeProfitPercent,
            risk.MaxLeverage,
            risk.MaxDailyLossPercent,
            risk.MaxPortfolioRiskPercent,
            risk.MaxSimultaneousPositions,
            risk.MaxConsecutiveLosses,
            risk.CooldownMinutes,
            risk.MinimumLiquidationSafetyBufferPercent,
            risk.IsActive,
            risk.AllowLive,
            risk.IsSystem);
}
