using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Abstractions.MarketData;

namespace TradingPlatform.MarketData;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketData(this IServiceCollection services)
    {
        services.AddSingleton<IFuturesUniverseCatalog, FuturesUniverseCatalog>();
        services.AddSingleton<IMarketScanner, MarketScanner>();
        services.AddSingleton<ITradeEligibility, TradeEligibilityService>();
        services.AddHostedService<UniverseRefreshWorker>();
        return services;
    }
}
