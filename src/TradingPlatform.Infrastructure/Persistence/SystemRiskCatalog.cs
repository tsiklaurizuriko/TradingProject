using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.Risk;

namespace TradingPlatform.Infrastructure.Persistence;

internal static class SystemRiskCatalog
{
    public static IReadOnlyList<RiskProfile> All() =>
    [
        Book("LOW", 0.5m, 2m, 4m, 3m, 3m, allowLive: true, active: true, maxPositions: 5),
        Book("MEDIUM", 1m, 2.5m, 5m, 5m, 5m, allowLive: true, active: false, maxPositions: 2),
        Book("HIGH", 2m, 3m, 6m, 8m, 7m, allowLive: false, active: false, maxPositions: 2),
        Book("30m EMA Cross", 0.5m, 1m, 3m, 3m, 3m, allowLive: false, active: false, maxPositions: 1)
    ];

    public static async Task EnsureAsync(TradingDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.RiskProfiles.ToListAsync(cancellationToken);
        var previous = rows.FirstOrDefault(row => string.Equals(row.Name, "BTC 30m EMA Cross", StringComparison.OrdinalIgnoreCase));
        if (previous is not null && rows.TrueForAll(row => !string.Equals(row.Name, "30m EMA Cross", StringComparison.OrdinalIgnoreCase)))
        {
            previous.Name = "30m EMA Cross";
            await db.SaveChangesAsync(cancellationToken);
        }

        var names = rows.Select(row => row.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (previous is not null)
        {
            names.Remove("BTC 30m EMA Cross");
            names.Add("30m EMA Cross");
        }
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
        bool active,
        int maxPositions = 2) =>
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
            MaxSimultaneousPositions = maxPositions,
            MaxConsecutiveLosses = 5,
            CooldownMinutes = 30,
            MinimumLiquidationSafetyBufferPercent = 1m
        };
}
