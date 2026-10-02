using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Application.Trading;
using TradingPlatform.Infrastructure.Persistence;
using TradingPlatform.Strategies.Engine;

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
            await db.Database.MigrateAsync(cancellationToken);
            await ApplyCanonicalStrategiesAsync(db, logger, cancellationToken);
            if (environment.IsDevelopment())
            {
                var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
                await seeder.SeedAsync(cancellationToken);
                await ApplyCanonicalStrategiesAsync(db, logger, cancellationToken);
            }

            var store = scope.ServiceProvider.GetRequiredService<ITradingStore>();
            await store.StopAllRunningBotsAsync("Stopped on startup. Start a coin manually from Bots.", cancellationToken);
            await store.SaveChangesAsync(cancellationToken);
            logger.LogInformation("No bot is running until a coin is started manually. Live entries stay blocked until Trading:LiveTradingEnabled is true.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration or canonical strategy validation failed. Startup is stopping.");
            throw;
        }
    }

    private static async Task ApplyCanonicalStrategiesAsync(
        TradingDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var strategies = await db.Strategies.Include(strategy => strategy.Versions).ToListAsync(cancellationToken);
        var snapshots = strategies
            .Select(strategy => new CanonicalStrategySnapshot(
                strategy.Id,
                strategy.TemplateKey,
                strategy.IsEnabled,
                strategy.IsArchived,
                strategy.Versions.OrderBy(version => version.VersionNumber).Select(version => version.DefinitionJson).ToList()))
            .ToList();
        var plan = CanonicalStrategyMigration.Plan(snapshots);
        CanonicalStrategyMigration.EnsureSafe(plan);
        foreach (var update in plan.Updates)
        {
            var strategy = strategies.First(row => row.Id == update.Id);
            strategy.TemplateKey = update.TemplateKey;
            strategy.IsEnabled = update.IsEnabled;
            strategy.IsArchived = update.IsArchived;
            var versions = strategy.Versions.OrderBy(version => version.VersionNumber).ToList();
            for (var i = 0; i < versions.Count && i < update.DefinitionJson.Count; i++)
            {
                if (string.Equals(versions[i].DefinitionJson, update.DefinitionJson[i], StringComparison.Ordinal))
                {
                    continue;
                }

                versions[i].DefinitionJson = update.DefinitionJson[i];
                versions[i].UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        if (plan.Updates.Count > 0)
        {
            if (db.Database.IsRelational())
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            else
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        foreach (var review in plan.Reviews.Distinct(StringComparer.Ordinal))
        {
            logger.LogWarning("Canonical strategy review: {Review}", review);
        }

        var enabled = strategies
            .Where(strategy => strategy.IsEnabled && strategy.DeletedAt == null)
            .Select(strategy => strategy.TemplateKey)
            .ToList();
        var obsolete = enabled.Where(StrategyTemplateKeys.IsObsoleteAlias).ToList();
        if (obsolete.Count > 0)
        {
            throw new InvalidOperationException(
                "Enabled strategies still use obsolete ids: " + string.Join(", ", obsolete) + ". Startup did not fall back to the retired implementation.");
        }

        var duplicates = enabled
            .Where(StrategyTemplateKeys.IsCanonical)
            .GroupBy(StrategyTemplateKeys.Normalize, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new InvalidOperationException(
                "More than one enabled strategy uses " + string.Join(", ", duplicates) + ". Disable the extra row. History was not deleted.");
        }

        var build = typeof(DatabaseInitializer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(DatabaseInitializer).Assembly.GetName().Version?.ToString()
            ?? "unknown";
        logger.LogInformation(
            "Build {Build}. Canonical strategies: {Strategies}",
            build,
            string.Join(", ", StrategyTemplateKeys.Canonical));
    }
}
