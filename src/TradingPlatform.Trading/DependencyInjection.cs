using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions.Exchange;
using TradingPlatform.Application.Abstractions.MarketData;
using TradingPlatform.Application.Trading;
using TradingPlatform.Execution;
using TradingPlatform.MarketData;

namespace TradingPlatform.Trading;

public static class DependencyInjection
{
    public static IServiceCollection AddTrading(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TradingOptions>(configuration.GetSection(TradingOptions.SectionName));
        services.AddSingleton<IMarketDataCache, MarketDataCache>();
        services.AddSingleton<ILiveAccountCache, LiveAccountCache>();
        services.AddSingleton<ITradingRealtimePublisher, NullTradingRealtimePublisher>();
        services.AddSingleton<PaperExchangeConnector>();
        services.AddScoped<IExchangeConnectorFactory, ExchangeConnectorFactory>();
        services.AddScoped<LiveIsolatedReconciler>();
        services.AddScoped<IBotEngine, BotEngine>();
        services.AddScoped<IBotLifecycleService, BotLifecycleService>();
        services.AddScoped<ITradingQueryService, TradingQueryService>();
        services.AddHostedService<PriceActionArmLoader>();

        if (configuration.GetValue("Trading:HostBotEngine", true))
        {
            services.AddHostedService<BotEngineWorker>();
        }

        return services;
    }
}
