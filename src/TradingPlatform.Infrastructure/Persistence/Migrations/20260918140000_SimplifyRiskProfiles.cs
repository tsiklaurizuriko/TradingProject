using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(TradingDbContext))]
    [Migration("20260918140000_SimplifyRiskProfiles")]
    public partial class SimplifyRiskProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RiskPerTradePercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxDailyTrades", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "CooldownAfterLossMinutes", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxConsecutiveLosses", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MinAvailableBalance", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "StopBotOnDailyLoss", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "StopAccountOnDailyLoss", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxPortfolioHeatPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxTotalExposurePercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "CorrelationFactor", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MinFreeMarginPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxRiskUsdt", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxNotionalUsdt", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxDailyLossUsdt", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxWeeklyLossPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "StopSlippageMultiplier", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MinStopAtrMultiple", table: "RiskProfiles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "RiskPerTradePercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0.5m);
            migrationBuilder.AddColumn<int>(name: "MaxDailyTrades", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 24);
            migrationBuilder.AddColumn<int>(name: "CooldownAfterLossMinutes", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 15);
            migrationBuilder.AddColumn<int>(name: "MaxConsecutiveLosses", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 4);
            migrationBuilder.AddColumn<decimal>(name: "MinAvailableBalance", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<bool>(name: "StopBotOnDailyLoss", table: "RiskProfiles", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<bool>(name: "StopAccountOnDailyLoss", table: "RiskProfiles", type: "boolean", nullable: false, defaultValue: true);
            migrationBuilder.AddColumn<decimal>(name: "MaxPortfolioHeatPercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 4m);
            migrationBuilder.AddColumn<decimal>(name: "MaxTotalExposurePercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 60m);
            migrationBuilder.AddColumn<decimal>(name: "CorrelationFactor", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0.75m);
            migrationBuilder.AddColumn<decimal>(name: "MinFreeMarginPercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 20m);
            migrationBuilder.AddColumn<decimal>(name: "MaxRiskUsdt", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "MaxNotionalUsdt", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "MaxDailyLossUsdt", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "MaxWeeklyLossPercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "StopSlippageMultiplier", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 1m);
            migrationBuilder.AddColumn<decimal>(name: "MinStopAtrMultiple", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
        }
    }
}
