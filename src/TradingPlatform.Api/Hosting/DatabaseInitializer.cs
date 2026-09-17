using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Trading;
using TradingPlatform.Infrastructure.Persistence;

namespace TradingPlatform.Api.Hosting;

public static class DatabaseInitializer
{
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        IHostEnvironment environment,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");
        var db = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        try
        {
            if (environment.IsDevelopment())
            {
                await db.Database.MigrateAsync(cancellationToken);
                var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
                await seeder.SeedAsync(cancellationToken);
            }

            var store = scope.ServiceProvider.GetRequiredService<ITradingStore>();
            await store.StopAllRunningBotsAsync("Stopped on startup. Start a coin manually from Bots.", cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            logger.LogInformation("No bot is running until a coin is started manually.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Database is not available yet. API will start; readiness checks will report the database as down until PostgreSQL is reachable.");
        }
    }
}
