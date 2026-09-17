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

    private readonly ITradingStore _store;
    private readonly IPublicMarketDataClient _market;
    private readonly IMarketDataCache _cache;
    private readonly IClock _clock;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly ITradingRealtimePublisher _publisher;
    private readonly IExchangeCredentialStore _credentials;
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
        if (!SymbolScope.Allows(strategyVersion.Strategy.AppliesToAllSymbols, strategyVersion.Strategy.AllowedSymbolsCsv, name))
        {
            throw new DomainException(
                ErrorCodes.InvalidSymbol,
                $"{strategyVersion.Strategy.Name} is not assigned to {name}. Open Strategies and add this coin, or set the strategy to all coins.");
        }

        var risk = riskProfileId is { } rid && rid != Guid.Empty
            ? await _store.GetRiskProfileByIdAsync(rid, cancellationToken) ?? await _store.GetConservativeRiskAsync(cancellationToken)
            : await _store.GetConservativeRiskAsync(cancellationToken);
        var riskAllowsAll = string.IsNullOrWhiteSpace(risk.AllowedSymbolsCsv);
        if (!SymbolScope.Allows(riskAllowsAll, risk.AllowedSymbolsCsv, name))
        {
            throw new DomainException(
                ErrorCodes.InvalidSymbol,
                $"{risk.Name} risk is not assigned to {name}. Open Risk Management and add this coin, or set that profile to all coins.");
        }
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

        var bot = await _store.FindBotBySymbolAsync(user.Id, name, mode, strategyVersion.StrategyId, risk.Id, cancellationToken);
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
        var risk = await _store.GetRiskProfileByIdAsync(riskProfileId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile was not found.");
        var riskAllowsAll = string.IsNullOrWhiteSpace(risk.AllowedSymbolsCsv);

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

            if (!SymbolScope.Allows(strategyVersion.Strategy.AppliesToAllSymbols, strategyVersion.Strategy.AllowedSymbolsCsv, name)
                || !SymbolScope.Allows(riskAllowsAll, risk.AllowedSymbolsCsv, name))
            {
                skipped++;
                continue;
            }

            var already = existing.Any(bot =>
                string.Equals(bot.Symbol, name, StringComparison.OrdinalIgnoreCase)
                && bot.StrategyVersion.StrategyId == strategyVersion.StrategyId
                && bot.RiskProfileId == risk.Id);
            if (already)
            {
                skipped++;
                continue;
            }

            if (await _store.GetSymbolAsync(name, cancellationToken) is null)
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
            }

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

            var running = await _store.GetRunningBotsAsync(cancellationToken);
            if (running.Any(other =>
                    other.Id != bot.Id &&
                    other.Mode == TradingMode.Live &&
                    string.Equals(other.Symbol, bot.Symbol, StringComparison.OrdinalIgnoreCase)))
            {
                throw new DomainException(
                    ErrorCodes.BotAlreadyRunning,
                    "A live bot is already running this coin. USD-M is one position per symbol.");
            }
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

    public async Task<StartBotsResult> StartAllIdleAsync(Guid userId, TradingMode mode, CancellationToken cancellationToken = default)
    {
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
        }

        var bots = (await _store.ListBotsAsync(cancellationToken))
            .Where(bot => bot.UserId == user.Id && bot.Mode == mode && bot.Status != BotStatus.Running)
            .OrderBy(bot => bot.Symbol)
            .ToList();
        var taken = new HashSet<string>(
            (await _store.GetRunningBotsAsync(cancellationToken))
                .Where(bot => bot.Mode == mode)
                .Select(bot => bot.Symbol),
            StringComparer.OrdinalIgnoreCase);

        var started = 0;
        var failed = 0;
        string? detail = null;
        foreach (var bot in bots)
        {
            if (mode == TradingMode.Live && !taken.Add(bot.Symbol))
            {
                failed++;
                detail = "A live bot is already running this coin. USD-M is one position per symbol.";
                continue;
            }

            try
            {
                await AttachLatestStrategyAsync(bot, cancellationToken);
                await MarkRunningAsync(bot, cancellationToken);
                if (mode == TradingMode.Paper)
                {
                    taken.Add(bot.Symbol);
                }

                started++;
            }
            catch (Exception ex)
            {
                failed++;
                detail = ex.Message;
                _logger.LogWarning(ex, "Start-all skipped {Mode} bot {BotId} {Symbol}", mode, bot.Id, bot.Symbol);
            }
        }

        if (started > 0)
        {
            await _store.SaveChangesAsync(cancellationToken);
            await _publisher.PublishOverviewAsync(cancellationToken);
        }

        _logger.LogInformation("Start-all {Mode}: started {Started}, failed {Failed}.", mode, started, failed);
        return new StartBotsResult(started, failed, detail);
    }

    private async Task AttachLatestStrategyAsync(Bot bot, CancellationToken cancellationToken)
    {
        var latest = await _store.GetLatestStrategyVersionAsync(bot.StrategyVersion.StrategyId, cancellationToken);
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

    internal static BotDto Map(Bot bot) =>
        new(
            bot.Id,
            bot.Name,
            bot.Status.ToString(),
            bot.Mode.ToString(),
            bot.Symbol,
            UsdtSpotUniverse.DisplayNameOf(bot.Symbol),
            UsdtSpotUniverse.RankOf(bot.Symbol),
            bot.Timeframe.ToBinanceInterval(),
            bot.StrategyVersion.Strategy.Name,
            bot.StrategyVersion.VersionNumber,
            bot.RiskProfile.Name,
            bot.LastError,
            bot.StartedAt,
            bot.StrategyVersion.StrategyId,
            bot.RiskProfileId);
}

public sealed class TradingQueryService : ITradingQueryService
{
    private readonly ITradingStore _store;
    private readonly IMarketDataCache _cache;
    private readonly IPublicMarketDataClient _market;
    private readonly ILiveAccountCache _live;
    private readonly IExchangeAccountService _accounts;
    private readonly TradingOptions _options;

    public TradingQueryService(
        ITradingStore store,
        IMarketDataCache cache,
        IPublicMarketDataClient market,
        ILiveAccountCache live,
        IExchangeAccountService accounts,
        IOptions<TradingOptions> options)
    {
        _store = store;
        _cache = cache;
        _market = market;
        _live = live;
        _accounts = accounts;
        _options = options.Value;
    }

    public async Task<PortfolioDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var bots = (await GetBotsAsync(cancellationToken)).ToList();
        var positions = (await GetPositionsAsync(cancellationToken)).ToList();
        var orders = (await GetOrdersAsync(cancellationToken)).ToList();
        var trades = await GetTradesAsync(cancellationToken);
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
                    UsdtSpotUniverse.RankOf(t.Symbol),
                    t.Price,
                    t.Timestamp)))
            .ToList();

        await RefreshLiveCacheIfStaleAsync(cancellationToken);
        var live = _live.Current;
        positions.AddRange(live.OpenPositions.Select(MapExchangePosition));
        orders.AddRange(live.OpenOrders.Select(MapExchangeOrder));

        var paperUsdt = balances.Where(b => b.Asset == "USDT").Sum(b => b.Free + b.Locked);
        var paperPortfolio = MarkToMarket(balances.Select(b => (b.Asset, b.Free + b.Locked)));
        var paperBotIds = bots.Where(b => !string.Equals(b.Mode, "Live", StringComparison.OrdinalIgnoreCase)).Select(b => b.Id).ToHashSet();
        var liveBotIds = bots.Where(b => string.Equals(b.Mode, "Live", StringComparison.OrdinalIgnoreCase)).Select(b => b.Id).ToHashSet();
        var paperUnrealized = positions.Where(p => paperBotIds.Contains(p.BotId)).Sum(p => p.UnrealizedPnL);
        var liveBotUnrealized = positions.Where(p => liveBotIds.Contains(p.BotId) && p.Source != "Binance").Sum(p => p.UnrealizedPnL);
        var liveUnrealized = liveBotUnrealized + positions.Where(p => p.Source == "Binance").Sum(p => p.UnrealizedPnL);
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
            paperPortfolio > 0m ? paperPortfolio : _options.PaperDefaultBalance,
            paperUsdt > 0m || balances.Count > 0 ? paperUsdt : _options.PaperDefaultBalance,
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
            live.Message);
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
            "Binance");

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
            "Binance");

    private static Guid StableGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task<IReadOnlyList<BotDto>> GetBotsAsync(CancellationToken cancellationToken = default) =>
        UsdtSpotUniverse.OrderByMarketCap(
            (await _store.ListBotsAsync(cancellationToken)).Select(BotLifecycleService.Map),
            b => b.Symbol);

    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = await _store.GetRecentOrdersAsync(50, cancellationToken);
        var trades = await _store.GetRecentTradesAsync(200, cancellationToken);
        var pnlByExit = trades
            .Where(t => t.ExitOrderId is not null && t.ClosedAt is not null)
            .GroupBy(t => t.ExitOrderId!.Value)
            .ToDictionary(g => g.Key, g => g.First().PnL);

        return orders
            .Select(o => new OrderDto(
                o.Id,
                o.ClientOrderId,
                o.ExchangeOrderId,
                o.BotId,
                o.Symbol,
                o.Side.ToString(),
                o.Type.ToString(),
                o.AverageFillPrice ?? o.Price,
                o.Quantity,
                o.FilledQuantity,
                o.Status.ToString(),
                o.CreatedAt,
                "Bot",
                pnlByExit.TryGetValue(o.Id, out var pnl) ? pnl : null,
                o.Executions.Count == 0 ? null : o.Executions.Sum(e => e.Fee)))
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
            p.OpenedAt))
        .ToList();

    public async Task<IReadOnlyList<TradeDto>> GetTradesAsync(CancellationToken cancellationToken = default) =>
        (await _store.GetRecentTradesAsync(200, cancellationToken))
        .Select(MapTrade)
        .ToList();

    public async Task<PerformanceDto> GetPerformanceAsync(string mode, CancellationToken cancellationToken = default)
    {
        var tradingMode = string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase) ? TradingMode.Live : TradingMode.Paper;
        var modeLabel = tradingMode == TradingMode.Live ? "Live" : "Paper";
        var rows = await _store.GetPerformanceTradesAsync(tradingMode, cancellationToken);
        var positions = (await _store.GetOpenPositionsForModeAsync(tradingMode, cancellationToken)).ToList();
        var bots = (await GetBotsAsync(cancellationToken))
            .Where(b => string.Equals(b.Mode, modeLabel, StringComparison.OrdinalIgnoreCase))
            .ToList();

        await RefreshLiveCacheIfStaleAsync(cancellationToken);
        var live = _live.Current;
        var botUnrealized = positions.Sum(p => p.UnrealizedPnL);
        var unrealized = tradingMode == TradingMode.Live
            ? botUnrealized + live.OpenPositions.Sum(p => p.UnrealizedPnL)
            : botUnrealized;
        var openPositions = tradingMode == TradingMode.Live
            ? positions.Count + live.OpenPositions.Count
            : positions.Count;

        return BuildPerformance(modeLabel, rows, bots, unrealized, openPositions, tradingMode == TradingMode.Paper ? _options.PaperDefaultBalance : 0m);
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
            t.Bot?.Mode.ToString() ?? "Paper");

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
            t.Mode);

    private static PerformanceDto BuildPerformance(
        string mode,
        IReadOnlyList<PerformanceTradeRow> rows,
        IReadOnlyList<BotDto> bots,
        decimal unrealized,
        int openPositions,
        decimal startingEquity)
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
            rows.Take(40).Select(MapTrade).ToList());
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

    public async Task<RiskProfileDto> GetRiskProfileAsync(CancellationToken cancellationToken = default) =>
        MapRisk(await _store.GetConservativeRiskAsync(cancellationToken));

    public async Task<IReadOnlyList<StrategyDto>> GetStrategiesAsync(CancellationToken cancellationToken = default) =>
        (await _store.ListStrategiesAsync(cancellationToken)).Select(MapStrategy).ToList();

    public async Task<IReadOnlyList<RiskProfileDto>> GetRiskProfilesAsync(CancellationToken cancellationToken = default) =>
        (await _store.ListRiskProfilesAsync(cancellationToken)).Select(MapRisk).ToList();

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
        return MapStrategy(strategy);
    }

    public async Task<RiskProfileDto> UpdateRiskScopeAsync(
        Guid riskProfileId,
        bool appliesToAll,
        IEnumerable<string>? symbols,
        CancellationToken cancellationToken = default)
    {
        var risk = await _store.GetRiskProfileByIdAsync(riskProfileId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile was not found.");
        ApplyRiskScope(risk, appliesToAll, symbols);
        await _store.SaveChangesAsync(cancellationToken);
        return MapRisk(risk);
    }

    public async Task<StrategyDto> CreateStrategyAsync(
        Guid userId,
        SaveStrategyRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = userId == Guid.Empty
            ? await _store.GetFirstAdminAsync(cancellationToken)
            : await _store.GetUserAsync(userId, cancellationToken) ?? await _store.GetFirstAdminAsync(cancellationToken);
        await EnsureUniqueStrategyNameAsync(request.Name, null, cancellationToken);
        if (!TimeframeExtensions.TryParseInterval(request.Timeframe, out var timeframe))
        {
            throw new DomainException(ErrorCodes.StrategyInvalid, $"Unknown timeframe '{request.Timeframe}'.");
        }

        var parameters = EmaRsiTemplate.Validate(ToParameters(request));
        var strategy = new Strategy
        {
            UserId = user.Id,
            User = user,
            Name = request.Name.Trim(),
            Description = (request.Description ?? string.Empty).Trim()
        };
        ApplyStrategyScope(strategy, request.AppliesToAllSymbols, request.Symbols);
        strategy.Versions.Add(new StrategyVersion
        {
            Strategy = strategy,
            VersionNumber = 1,
            DefinitionJson = EmaRsiTemplate.Build(strategy.Name, 1, timeframe.ToBinanceInterval(), parameters),
            Symbol = "BTCUSDT",
            Timeframe = timeframe
        });
        await _store.AddStrategyAsync(strategy, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);
        return MapStrategy(strategy);
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

        var parameters = EmaRsiTemplate.Validate(ToParameters(request));
        strategy.Name = request.Name.Trim();
        strategy.Description = (request.Description ?? string.Empty).Trim();
        ApplyStrategyScope(strategy, request.AppliesToAllSymbols, request.Symbols);

        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var definitionChanged = latest is null
            || latest.Timeframe != timeframe
            || EmaRsiTemplate.Read(latest.DefinitionJson) != parameters;
        if (latest is null || (definitionChanged && latest.IsImmutable))
        {
            var versionNumber = (latest?.VersionNumber ?? 0) + 1;
            strategy.Versions.Add(new StrategyVersion
            {
                Strategy = strategy,
                VersionNumber = versionNumber,
                DefinitionJson = EmaRsiTemplate.Build(strategy.Name, versionNumber, timeframe.ToBinanceInterval(), parameters),
                Symbol = "BTCUSDT",
                Timeframe = timeframe
            });
        }
        else if (definitionChanged)
        {
            latest.DefinitionJson = EmaRsiTemplate.Build(strategy.Name, latest.VersionNumber, timeframe.ToBinanceInterval(), parameters);
            latest.Timeframe = timeframe;
        }

        await _store.SaveChangesAsync(cancellationToken);
        return MapStrategy(strategy);
    }

    public async Task<RiskProfileDto> CreateRiskProfileAsync(
        SaveRiskProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureUniqueRiskNameAsync(request.Name, null, cancellationToken);
        var risk = new RiskProfile
        {
            Name = request.Name.Trim(),
            IsSystem = false
        };
        ApplyRiskValues(risk, request);
        ApplyRiskScope(risk, request.AppliesToAllSymbols, request.Symbols);
        await _store.AddRiskProfileAsync(risk, cancellationToken);
        await _store.SaveChangesAsync(cancellationToken);
        return MapRisk(risk);
    }

    public async Task<RiskProfileDto> UpdateRiskProfileAsync(
        Guid riskProfileId,
        SaveRiskProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var risk = await _store.GetRiskProfileByIdAsync(riskProfileId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile was not found.");
        if (!risk.IsSystem)
        {
            await EnsureUniqueRiskNameAsync(request.Name, risk.Id, cancellationToken);
            risk.Name = request.Name.Trim();
        }

        ApplyRiskValues(risk, request);
        ApplyRiskScope(risk, request.AppliesToAllSymbols, request.Symbols);
        await _store.SaveChangesAsync(cancellationToken);
        return MapRisk(risk);
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

    private async Task EnsureUniqueRiskNameAsync(string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Risk profile name is required.");
        }

        var exists = (await _store.ListRiskProfilesAsync(cancellationToken))
            .Any(row => row.Id != exceptId && string.Equals(row.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exists)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A risk profile with this name already exists.");
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

    private static void ApplyRiskScope(RiskProfile risk, bool appliesToAll, IEnumerable<string>? symbols)
    {
        var list = SymbolScope.Parse(SymbolScope.Join(symbols));
        if (!appliesToAll && list.Count == 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Pick at least one coin, or set this risk profile to all coins.");
        }

        risk.AllowedSymbolsCsv = appliesToAll ? null : SymbolScope.Join(list);
    }

    private static void ApplyRiskValues(RiskProfile risk, SaveRiskProfileRequest request)
    {
        if (request.RiskPerTradePercent <= 0 || request.MaxPositionPercent <= 0 || request.MaxDailyLossPercent <= 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Risk percents must be greater than zero.");
        }

        if (request.MaxPortfolioHeatPercent <= 0 || request.MaxTotalExposurePercent <= 0)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Portfolio heat and total exposure must be greater than zero.");
        }

        if (request.CorrelationFactor is < 0 or > 1)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Correlation factor must be between 0 (independent) and 1 (lockstep).");
        }

        if (request.MinFreeMarginPercent is < 0 or > 50)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Cash reserve must be between 0% and 50%.");
        }

        if (request.MaxOpenPositions < 1 || request.MaxDailyTrades < 1)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Position and daily trade limits must be at least 1.");
        }

        if (request.MaxLeverage < 1)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Max leverage must be at least 1.");
        }

        var margin = ParseMarginMode(request.MarginMode);
        if (margin == MarginMode.Cross && request.MaxOpenPositions > RiskEngine.MaxCrossOpenPositions)
        {
            throw new DomainException(
                ErrorCodes.ValidationFailed,
                $"Cross margin cannot run more than {RiskEngine.MaxCrossOpenPositions} coins. Use Isolated for a universe.");
        }

        if (margin == MarginMode.Cross && request.MaxLeverage > 5m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Cross leverage is capped at 5x. Isolated can go higher with a hard stop on the exchange.");
        }

        if (margin == MarginMode.Isolated && request.MaxLeverage > 20m)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Isolated leverage is capped at 20x so liquidation stays beyond a typical 1–2% stop.");
        }

        risk.RiskPerTradePercent = request.RiskPerTradePercent;
        risk.MaxPositionPercent = request.MaxPositionPercent;
        risk.MaxDailyLossPercent = request.MaxDailyLossPercent;
        risk.MaxOpenPositions = request.MaxOpenPositions;
        risk.MaxDailyTrades = request.MaxDailyTrades;
        risk.CooldownAfterLossMinutes = Math.Max(0, request.CooldownAfterLossMinutes);
        risk.MaxConsecutiveLosses = Math.Max(1, request.MaxConsecutiveLosses);
        risk.MaxLeverage = request.MaxLeverage;
        risk.StopBotOnDailyLoss = request.StopBotOnDailyLoss;
        risk.StopAccountOnDailyLoss = request.StopAccountOnDailyLoss;
        risk.MarginMode = margin;
        risk.MaxPortfolioHeatPercent = request.MaxPortfolioHeatPercent;
        risk.MaxTotalExposurePercent = request.MaxTotalExposurePercent;
        risk.CorrelationFactor = request.CorrelationFactor;
        risk.MinFreeMarginPercent = request.MinFreeMarginPercent;
    }

    private static MarginMode ParseMarginMode(string? value) =>
        string.Equals(value, "Cross", StringComparison.OrdinalIgnoreCase)
            ? MarginMode.Cross
            : MarginMode.Isolated;

    private static EmaRsiParameters ToParameters(SaveStrategyRequest request) =>
        new(
            request.EmaFast,
            request.EmaSlow,
            request.RsiPeriod,
            request.RsiMinimum,
            request.StopLossPercent,
            request.TakeProfitPercent);

    private static StrategyDto MapStrategy(Strategy strategy)
    {
        var latest = strategy.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        var parameters = EmaRsiTemplate.Read(latest?.DefinitionJson);
        return new StrategyDto(
            strategy.Id,
            strategy.Name,
            strategy.Description,
            latest?.VersionNumber ?? 1,
            (latest?.Timeframe ?? Timeframe.FiveMinutes).ToBinanceInterval(),
            strategy.AppliesToAllSymbols,
            SymbolScope.Parse(strategy.AllowedSymbolsCsv),
            parameters.EmaFast,
            parameters.EmaSlow,
            parameters.RsiPeriod,
            parameters.RsiMinimum,
            parameters.StopLossPercent,
            parameters.TakeProfitPercent,
            latest?.IsImmutable ?? false);
    }

    private static RiskProfileDto MapRisk(RiskProfile risk)
    {
        var symbols = SymbolScope.Parse(risk.AllowedSymbolsCsv);
        return new RiskProfileDto(
            risk.Id,
            risk.Name,
            risk.RiskPerTradePercent,
            risk.MaxPositionPercent,
            risk.MaxDailyLossPercent,
            risk.MaxOpenPositions,
            risk.MaxDailyTrades,
            risk.CooldownAfterLossMinutes,
            risk.MaxConsecutiveLosses,
            risk.MaxLeverage,
            risk.StopBotOnDailyLoss,
            risk.StopAccountOnDailyLoss,
            risk.MarginMode.ToString(),
            risk.MaxPortfolioHeatPercent,
            risk.MaxTotalExposurePercent,
            risk.CorrelationFactor,
            risk.MinFreeMarginPercent,
            risk.IsSystem,
            symbols.Count == 0,
            symbols);
    }
}
