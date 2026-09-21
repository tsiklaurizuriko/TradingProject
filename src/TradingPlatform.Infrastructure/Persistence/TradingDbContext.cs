using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Npgsql;
using TradingPlatform.Application.Abstractions;
using TradingPlatform.Domain.Backtesting;
using TradingPlatform.Domain.Balances;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Common;
using TradingPlatform.Domain.Errors;
using TradingPlatform.Domain.Exchanges;
using TradingPlatform.Domain.Identity;
using TradingPlatform.Domain.Market;
using TradingPlatform.Domain.Operations;
using TradingPlatform.Domain.Orders;
using TradingPlatform.Domain.Positions;
using TradingPlatform.Domain.Risk;
using TradingPlatform.Domain.Signals;
using TradingPlatform.Domain.Strategies;
using TradingPlatform.Domain.Trades;

namespace TradingPlatform.Infrastructure.Persistence;

public sealed class TradingDbContext : DbContext, IUnitOfWork
{
    public TradingDbContext(DbContextOptions<TradingDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ExchangeAccount> ExchangeAccounts => Set<ExchangeAccount>();
    public DbSet<ExchangeCredential> ExchangeCredentials => Set<ExchangeCredential>();
    public DbSet<Bot> Bots => Set<Bot>();
    public DbSet<BotRun> BotRuns => Set<BotRun>();
    public DbSet<Strategy> Strategies => Set<Strategy>();
    public DbSet<StrategyVersion> StrategyVersions => Set<StrategyVersion>();
    public DbSet<RiskProfile> RiskProfiles => Set<RiskProfile>();
    public DbSet<Symbol> Symbols => Set<Symbol>();
    public DbSet<TimeframeRecord> Timeframes => Set<TimeframeRecord>();
    public DbSet<MarketCandle> MarketCandles => Set<MarketCandle>();
    public DbSet<MarketTrade> MarketTrades => Set<MarketTrade>();
    public DbSet<Signal> Signals => Set<Signal>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<PositionEvent> PositionEvents => Set<PositionEvent>();
    public DbSet<Balance> Balances => Set<Balance>();
    public DbSet<BalanceSnapshot> BalanceSnapshots => Set<BalanceSnapshot>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Backtest> Backtests => Set<Backtest>();
    public DbSet<BacktestRun> BacktestRuns => Set<BacktestRun>();
    public DbSet<BacktestTrade> BacktestTrades => Set<BacktestTrade>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAndBumpVersions();
        const int attempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt < attempts && IsDuplicateClosedCandle(ex))
            {
                DetachAddedCandles();
                StampAndBumpVersions();
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < attempts)
            {
                await MergeConcurrencyAsync(ex, cancellationToken);
                StampAndBumpVersions();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new DomainException(
                    ErrorCodes.ReconciliationRequired,
                    "This row was updated at the same time. Try Close again.");
            }
        }
    }

    private static bool IsDuplicateClosedCandle(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && postgres.SqlState == PostgresErrorCodes.UniqueViolation
        && postgres.ConstraintName == "IX_MarketCandles_SymbolId_Timeframe_OpenTime";

    private void DetachAddedCandles()
    {
        foreach (var entry in ChangeTracker.Entries<MarketCandle>().Where(row => row.State == EntityState.Added))
        {
            entry.State = EntityState.Detached;
        }
    }

    private void StampAndBumpVersions()
    {
        var utc = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = utc;
                entry.Entity.UpdatedAt = utc;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = utc;
            }
        }

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified)
            {
                continue;
            }

            var version = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "RowVersion");
            if (version is null)
            {
                continue;
            }

            var original = ReadVersion(version.OriginalValue);
            var current = ReadVersion(version.CurrentValue);
            if (current <= original)
            {
                SetVersion(version, original + 1);
            }

            version.IsModified = true;
        }
    }

    private static async Task MergeConcurrencyAsync(
        DbUpdateConcurrencyException exception,
        CancellationToken cancellationToken)
    {
        foreach (var entry in exception.Entries)
        {
            var database = await entry.GetDatabaseValuesAsync(cancellationToken);
            if (database is null)
            {
                entry.State = EntityState.Detached;
                continue;
            }

            foreach (var property in entry.Properties)
            {
                var name = property.Metadata.Name;
                if (name == "RowVersion")
                {
                    property.OriginalValue = database[name];
                    SetVersion(property, ReadVersion(database[name]) + 1);
                    continue;
                }

                if (!property.IsModified)
                {
                    property.OriginalValue = database[name];
                    continue;
                }

                if (name is "Free" or "Locked")
                {
                    var merged = ToDecimal(database[name])
                        + (ToDecimal(property.CurrentValue) - ToDecimal(property.OriginalValue));
                    if (merged < 0m)
                    {
                        merged = 0m;
                    }

                    property.OriginalValue = database[name];
                    property.CurrentValue = merged;
                    continue;
                }

                property.OriginalValue = database[name];
            }
        }
    }

    private static long ReadVersion(object? value) => value switch
    {
        uint number => number,
        int number => number,
        long number => number,
        ulong number => (long)number,
        null => 0,
        _ => Convert.ToInt64(value)
    };

    private static void SetVersion(PropertyEntry property, long value)
    {
        var type = Nullable.GetUnderlyingType(property.Metadata.ClrType) ?? property.Metadata.ClrType;
        if (type == typeof(uint))
        {
            property.CurrentValue = (uint)value;
            return;
        }

        if (type == typeof(long))
        {
            property.CurrentValue = value;
            return;
        }

        property.CurrentValue = Convert.ChangeType(value, type);
    }

    private static decimal ToDecimal(object? value) => value switch
    {
        decimal number => number,
        int number => number,
        long number => number,
        double number => (decimal)number,
        float number => (decimal)number,
        null => 0m,
        _ => Convert.ToDecimal(value)
    };

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingDbContext).Assembly);
        TradingModelConfiguration.Configure(modelBuilder);
    }
}
