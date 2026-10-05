using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Execution;
using TradingPlatform.Execution.Shadow;
using TradingPlatform.MarketData;
using TradingPlatform.Trading.Shadow;

namespace TradingPlatform.Trading;

public static class DependencyInjection
{
    public static IServiceCollection AddTrading(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TradingOptions>(configuration.GetSection(TradingOptions.SectionName));
        services.AddSingleton<IMarketDataCache, MarketDataCache>();
        services.AddSingleton<ILiveAccountCache, LiveAccountCache>();
        services.AddSingleton<ReconciliationState>();
        services.AddSingleton<BotCycleState>();
        services.TryAddSingleton<IWorkerLease, InProcessWorkerLease>();
        services.TryAddSingleton<IExchangeEventSignal, ExchangeEventSignal>();
        services.AddSingleton<ITradingRealtimePublisher, NullTradingRealtimePublisher>();
        services.AddScoped<IExchangeConnectorFactory, ExchangeConnectorFactory>();
        services.AddScoped<LiveIsolatedReconciler>();
        services.AddScoped<IBotEngine, BotEngine>();
        services.AddScoped<IBotLifecycleService, BotLifecycleService>();
        services.AddScoped<ITradingQueryService, TradingQueryService>();
        services.AddHostedService<PriceActionArmLoader>();
        services.AddHostedService<CrossSectionalUniverseWorker>();
        services.AddSingleton<TopTraderRankBook>();
        services.AddHostedService<TopTraderRankWorker>();

        if (configuration.GetValue("Trading:HostBotEngine", true))
        {
            services.AddHostedService<BotEngineWorker>();
        }

        if (TradingVenue.Read(configuration) == TradingVenueKind.Shadow)
        {
            AddShadowVenue(services, configuration);
        }

        return services;
    }

    private static void AddShadowVenue(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ShadowExchangeOptions.SectionName).Get<ShadowExchangeOptions>() ?? new ShadowExchangeOptions();
        services.AddSingleton(new ShadowExchange(options));
        services.AddSingleton<IShadowPriceFeed, PublicShadowPriceFeed>();
        services.Replace(ServiceDescriptor.Scoped<ILiveExchangeConnectorFactory, ShadowExchangeConnectorFactory>());
        services.Replace(ServiceDescriptor.Scoped<IExchangeAccountService, ShadowExchangeAccountService>());
        services.AddHostedService<ShadowExchangeWorker>();
    }
}
