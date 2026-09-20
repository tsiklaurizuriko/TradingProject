using Microsoft.EntityFrameworkCore;
using TradingPlatform.Domain.Backtesting;
using TradingPlatform.Domain.Balances;
using TradingPlatform.Domain.Bots;
using TradingPlatform.Domain.Common;
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
using TradingPlatform.Domain.Trading;

namespace TradingPlatform.Infrastructure.Persistence;

internal static class TradingModelConfiguration
{
    public static void Configure(ModelBuilder model)
    {
        ConfigureIdentity(model);
        ConfigureTrading(model);
        ConfigureMarket(model);
        ConfigureOps(model);
    }

    private static void Money(Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<decimal> property) =>
        property.HasPrecision(DecimalConventions.PricePrecision, DecimalConventions.PriceScale);

    private static void ConfigureIdentity(ModelBuilder model)
    {
        model.Entity<User>(b =>
        {
            b.ToTable("Users");
            b.HasIndex(x => x.NormalizedEmail).IsUnique();
            b.Property(x => x.Email).HasMaxLength(256).IsRequired();
            b.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            b.HasQueryFilter(x => x.DeletedAt == null);
        });
        model.Entity<Role>(b =>
        {
            b.ToTable("Roles");
            b.HasIndex(x => x.Name).IsUnique();
            b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        });
        model.Entity<Permission>(b =>
        {
            b.ToTable("Permissions");
            b.HasIndex(x => x.Code).IsUnique();
            b.Property(x => x.Code).HasMaxLength(128).IsRequired();
        });
        model.Entity<UserRole>(b =>
        {
            b.ToTable("UserRoles");
            b.HasKey(x => new { x.UserId, x.RoleId });
            b.HasOne(x => x.User).WithMany(x => x.UserRoles).HasForeignKey(x => x.UserId);
            b.HasOne(x => x.Role).WithMany(x => x.UserRoles).HasForeignKey(x => x.RoleId);
        });
        model.Entity<RolePermission>(b =>
        {
            b.ToTable("RolePermissions");
            b.HasKey(x => new { x.RoleId, x.PermissionId });
            b.HasOne(x => x.Role).WithMany(x => x.RolePermissions).HasForeignKey(x => x.RoleId);
            b.HasOne(x => x.Permission).WithMany(x => x.RolePermissions).HasForeignKey(x => x.PermissionId);
        });
        model.Entity<RefreshToken>(b =>
        {
            b.ToTable("RefreshTokens");
            b.HasIndex(x => x.TokenHash).IsUnique();
            b.HasOne(x => x.User).WithMany(x => x.RefreshTokens).HasForeignKey(x => x.UserId);
        });
        model.Entity<PasswordResetToken>(b =>
        {
            b.ToTable("PasswordResetTokens");
            b.HasIndex(x => x.TokenHash).IsUnique();
        });
        model.Entity<ExchangeAccount>(b =>
        {
            b.ToTable("ExchangeAccounts");
            b.HasQueryFilter(x => x.DeletedAt == null);
            b.HasIndex(x => new { x.UserId, x.Name }).IsUnique();
        });
        model.Entity<ExchangeCredential>(b =>
        {
            b.ToTable("ExchangeCredentials");
            b.HasIndex(x => x.ExchangeAccountId).IsUnique();
            b.HasOne(x => x.ExchangeAccount).WithOne(x => x.Credential).HasForeignKey<ExchangeCredential>(x => x.ExchangeAccountId);
        });
    }

    private static void ConfigureTrading(ModelBuilder model)
    {
        model.Entity<Bot>(b =>
        {
            b.ToTable("Bots");
            b.HasQueryFilter(x => x.DeletedAt == null);
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.Symbol).HasMaxLength(32).IsRequired();
            b.Property(x => x.RowVersion).IsConcurrencyToken();
            b.HasIndex(x => new { x.UserId, x.Name });
        });
        model.Entity<BotRun>().ToTable("BotRuns");
        model.Entity<Strategy>(b =>
        {
            b.ToTable("Strategies");
            b.HasQueryFilter(x => x.DeletedAt == null);
            b.Property(x => x.Name).HasMaxLength(128).IsRequired();
            b.Property(x => x.AllowedSymbolsCsv).HasMaxLength(4000);
            b.Property(x => x.TemplateKey).HasMaxLength(32).IsRequired();
            b.Property(x => x.AllowedSide).HasMaxLength(16).IsRequired();
            b.Property(x => x.IsEnabled).IsRequired();
            b.Property(x => x.ValidationStatus).HasMaxLength(32).IsRequired();
            b.HasIndex(x => new { x.UserId, x.Name });
        });
        model.Entity<StrategyVersion>(b =>
        {
            b.ToTable("StrategyVersions");
            b.HasIndex(x => new { x.StrategyId, x.VersionNumber }).IsUnique();
            b.Property(x => x.DefinitionJson).HasColumnType("jsonb");
        });
        model.Entity<RiskProfile>(b =>
        {
            b.ToTable("RiskProfiles");
            b.HasQueryFilter(x => x.DeletedAt == null);
            Money(b.Property(x => x.RiskPerTradePercent));
            Money(b.Property(x => x.StopLossPercent));
            Money(b.Property(x => x.TakeProfitPercent));
            Money(b.Property(x => x.MaxLeverage));
            Money(b.Property(x => x.MaxDailyLossPercent));
            Money(b.Property(x => x.MaxPortfolioRiskPercent));
            Money(b.Property(x => x.MinimumLiquidationSafetyBufferPercent));
        });
        model.Entity<Signal>(b =>
        {
            b.ToTable("Signals");
            Money(b.Property(x => x.Price));
            b.HasIndex(x => new { x.BotId, x.Timestamp });
        });
        model.Entity<Order>(b =>
        {
            b.ToTable("Orders");
            b.HasIndex(x => x.ClientOrderId).IsUnique();
            b.HasIndex(x => x.ExchangeOrderId);
            b.HasIndex(x => new { x.BotId, x.Status, x.CreatedAt });
            Money(b.Property(x => x.Quantity));
            Money(b.Property(x => x.FilledQuantity));
            Money(b.Property(x => x.RemainingQuantity));
            b.Property(x => x.Price).HasPrecision(DecimalConventions.PricePrecision, DecimalConventions.PriceScale);
            b.Property(x => x.AverageFillPrice).HasPrecision(DecimalConventions.PricePrecision, DecimalConventions.PriceScale);
            b.Property(x => x.RowVersion).IsConcurrencyToken();
        });
        model.Entity<OrderEvent>().ToTable("OrderEvents");
        model.Entity<Execution>(b =>
        {
            b.ToTable("Executions");
            Money(b.Property(x => x.Price));
            Money(b.Property(x => x.Quantity));
            Money(b.Property(x => x.Fee));
            b.HasIndex(x => x.ExchangeTradeId);
        });
        model.Entity<Position>(b =>
        {
            b.ToTable("Positions");
            b.Ignore(x => x.IsOpen);
            Money(b.Property(x => x.Quantity));
            Money(b.Property(x => x.AverageEntryPrice));
            Money(b.Property(x => x.CurrentPrice));
            Money(b.Property(x => x.UnrealizedPnL));
            Money(b.Property(x => x.RealizedPnL));
            Money(b.Property(x => x.Fees));
            Money(b.Property(x => x.StopLossPercent));
            Money(b.Property(x => x.TakeProfitPercent));
            Money(b.Property(x => x.InitialRiskUsdt));
            Money(b.Property(x => x.MarginUsdt));
            Money(b.Property(x => x.AvailableBalanceAtEntry));
            Money(b.Property(x => x.RiskPerTradePercent));
            Money(b.Property(x => x.StopLossPrice));
            Money(b.Property(x => x.TakeProfitPrice));
            Money(b.Property(x => x.NotionalUsdt));
            Money(b.Property(x => x.Leverage));
            Money(b.Property(x => x.EquityAtEntry));
            Money(b.Property(x => x.LiquidationPrice));
            Money(b.Property(x => x.EstimatedEntryFee));
            Money(b.Property(x => x.EstimatedExitFee));
            Money(b.Property(x => x.EstimatedSlippage));
            Money(b.Property(x => x.EstimatedTotalRisk));
            b.Property(x => x.RowVersion).IsConcurrencyToken();
            b.HasIndex(x => new { x.BotId, x.Symbol, x.ClosedAt });
        });
        model.Entity<PositionEvent>().ToTable("PositionEvents");
        model.Entity<Balance>(b =>
        {
            b.ToTable("Balances");
            Money(b.Property(x => x.Free));
            Money(b.Property(x => x.Locked));
            b.HasIndex(x => new { x.ExchangeAccountId, x.BotId, x.Asset, x.Mode }).IsUnique();
            b.Property(x => x.RowVersion).IsConcurrencyToken();
        });
        model.Entity<BalanceSnapshot>(b =>
        {
            b.ToTable("BalanceSnapshots");
            Money(b.Property(x => x.Free));
            Money(b.Property(x => x.Locked));
            Money(b.Property(x => x.PortfolioValueQuote));
            b.HasIndex(x => new { x.ExchangeAccountId, x.SnapshotAt });
        });
        model.Entity<Trade>(b =>
        {
            b.ToTable("Trades");
            b.Ignore(x => x.Duration);
            Money(b.Property(x => x.Quantity));
            Money(b.Property(x => x.EntryPrice));
            b.Property(x => x.ExitPrice).HasPrecision(DecimalConventions.PricePrecision, DecimalConventions.PriceScale);
            Money(b.Property(x => x.PnL));
            Money(b.Property(x => x.PnLPercent));
            Money(b.Property(x => x.Fees));
            b.HasIndex(x => new { x.BotId, x.ClosedAt });
        });
    }

    private static void ConfigureMarket(ModelBuilder model)
    {
        model.Entity<Symbol>(b =>
        {
            b.ToTable("Symbols");
            b.HasIndex(x => x.Name).IsUnique();
            Money(b.Property(x => x.TickSize));
            Money(b.Property(x => x.StepSize));
            Money(b.Property(x => x.MinQuantity));
            Money(b.Property(x => x.MinNotional));
        });
        model.Entity<TimeframeRecord>(b =>
        {
            b.ToTable("Timeframes");
            b.HasIndex(x => x.Code).IsUnique();
        });
        model.Entity<MarketCandle>(b =>
        {
            b.ToTable("MarketCandles");
            b.HasIndex(x => new { x.SymbolId, x.Timeframe, x.OpenTime }).IsUnique();
            Money(b.Property(x => x.Open));
            Money(b.Property(x => x.High));
            Money(b.Property(x => x.Low));
            Money(b.Property(x => x.Close));
            Money(b.Property(x => x.Volume));
            b.Ignore(x => x.TakerBuyVolume);
        });
        model.Entity<MarketTrade>(b =>
        {
            b.ToTable("MarketTrades");
            Money(b.Property(x => x.Price));
            Money(b.Property(x => x.Quantity));
            b.HasIndex(x => new { x.SymbolId, x.ExchangeTimestamp });
        });
    }

    private static void ConfigureOps(ModelBuilder model)
    {
        model.Entity<Backtest>(b =>
        {
            b.ToTable("Backtests");
            Money(b.Property(x => x.InitialBalance));
            Money(b.Property(x => x.FeeBps));
            Money(b.Property(x => x.SlippageBps));
        });
        model.Entity<BacktestRun>(b =>
        {
            b.ToTable("BacktestRuns");
            foreach (var p in new[]
                     {
                         nameof(BacktestRun.InitialBalance), nameof(BacktestRun.FinalBalance), nameof(BacktestRun.NetProfit),
                         nameof(BacktestRun.ReturnPercent), nameof(BacktestRun.WinRate), nameof(BacktestRun.ProfitFactor),
                         nameof(BacktestRun.AverageWin), nameof(BacktestRun.AverageLoss), nameof(BacktestRun.MaximumDrawdown),
                         nameof(BacktestRun.FeesPaid), nameof(BacktestRun.LargestWinningTrade), nameof(BacktestRun.LargestLosingTrade)
                     })
            {
                b.Property<decimal>(p).HasPrecision(DecimalConventions.PricePrecision, DecimalConventions.PriceScale);
            }
            b.Property(x => x.SharpeRatio).HasPrecision(DecimalConventions.PricePrecision, DecimalConventions.PriceScale);
            b.Property(x => x.AssumptionsJson).HasColumnType("jsonb");
        });
        model.Entity<BacktestTrade>(b =>
        {
            b.ToTable("BacktestTrades");
            Money(b.Property(x => x.Quantity));
            Money(b.Property(x => x.EntryPrice));
            Money(b.Property(x => x.ExitPrice));
            Money(b.Property(x => x.PnL));
            Money(b.Property(x => x.Fees));
            b.Property(x => x.Side).HasMaxLength(8).IsRequired();
        });
        model.Entity<Notification>().ToTable("Notifications");
        model.Entity<AuditLog>(b =>
        {
            b.ToTable("AuditLogs");
            b.HasIndex(x => x.CreatedAt);
            b.HasIndex(x => x.CorrelationId);
            b.Property(x => x.MetadataJson).HasColumnType("jsonb");
        });
        model.Entity<SystemSetting>(b =>
        {
            b.ToTable("SystemSettings");
            b.HasIndex(x => x.Key).IsUnique();
        });
    }
}
