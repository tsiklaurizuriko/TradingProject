using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.Risk;

namespace TradingPlatform.Infrastructure.Persistence;

internal static class SystemRiskCatalog
{
    public static IReadOnlyList<RiskProfile> All() =>
    [
        Book("LOW", 0.5m, 2m, 4m, 3m, 3m, allowLive: true, active: true),
        Book("MEDIUM", 1m, 2.5m, 5m, 5m, 5m, allowLive: true, active: false),
        Book("HIGH", 2m, 3m, 6m, 8m, 7m, allowLive: false, active: false)
    ];

    public static async Task EnsureAsync(TradingDbContext db, CancellationToken cancellationToken)
    {
        var names = (await db.RiskProfiles.Select(row => row.Name).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var book in All())
        {
            if (names.Contains(book.Name))
            {
                continue;
            }

            db.RiskProfiles.Add(book);
            added = true;
        }

        if (added)
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static RiskProfile Book(
        string name,
        decimal risk,
        decimal stop,
        decimal take,
        decimal leverage,
        decimal dailyLoss,
        bool allowLive,
        bool active) =>
        new()
        {
            Name = name,
            IsSystem = true,
            IsActive = active,
            AllowLive = allowLive,
            RiskPerTradePercent = risk,
            StopLossPercent = stop,
            TakeProfitPercent = take,
            MaxLeverage = leverage,
            MaxDailyLossPercent = dailyLoss,
            MaxPortfolioRiskPercent = 4m,
            MaxSimultaneousPositions = 2,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m
        };
}
