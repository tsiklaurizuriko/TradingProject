using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Strategies.Engine;
using TradingPlatform.Strategies.Indicators;

namespace TradingPlatform.Strategies;

public static class DependencyInjection
{
    public static IServiceCollection AddStrategies(this IServiceCollection services)
    {
        services.AddSingleton<IndicatorRegistry>();
        services.AddSingleton<StrategyDefinitionValidator>();
        services.AddSingleton<IStrategyEngine, StrategyEngine>();
        return services;
    }
}
