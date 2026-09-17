using Microsoft.Extensions.DependencyInjection;

namespace TradingPlatform.MarketData;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketData(this IServiceCollection services)
    {
        return services;
    }
}
