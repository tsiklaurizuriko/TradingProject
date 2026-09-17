using Microsoft.Extensions.DependencyInjection;

namespace TradingPlatform.Workers;

public static class DependencyInjection
{
    public static IServiceCollection AddTradingPlatformWorkers(this IServiceCollection services)
    {
        services.AddHostedService<WorkerHeartbeatService>();
        return services;
    }
}
