using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingPlatform.Infrastructure.Persistence;
using Xunit;

namespace TradingPlatform.IntegrationTests;

public sealed class MigrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__TradingPlatform")
        ?? "Host=localhost;Port=5432;Database=TradingProject;Username=admin;Password=admin";

    [Fact]
    public async Task Migrations_apply_and_match_the_model()
    {
        await using var db = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

        await db.Database.MigrateAsync();

        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        db.Database.HasPendingModelChanges().Should().BeFalse("the model snapshot must match the entity configuration");
        var probe = await db.Trades.AsNoTracking().Select(t => new { t.NetPnL, t.FundingPnL }).Take(1).ToListAsync();
        probe.Should().NotBeNull();
    }
}
