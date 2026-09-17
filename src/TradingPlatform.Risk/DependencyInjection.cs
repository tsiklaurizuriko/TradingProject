using Microsoft.Extensions.DependencyInjection;

namespace TradingPlatform.Risk;

public static class DependencyInjection
{
    public static IServiceCollection AddRisk(this IServiceCollection services)
    {
        services.AddSingleton<IRiskEngine, RiskEngine>();
        return services;
    }
}
