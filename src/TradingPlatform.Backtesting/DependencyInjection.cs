using Microsoft.Extensions.DependencyInjection;
using TradingPlatform.Application.Trading;

namespace TradingPlatform.Backtesting;

public static class DependencyInjection
{
    public static IServiceCollection AddBacktesting(this IServiceCollection services)
    {
        services.AddScoped<IBacktestService, BacktestService>();
        return services;
    }
}
