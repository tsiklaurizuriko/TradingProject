using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(TradingDbContext))]
    [Migration("20260918170000_RiskControls")]
    public partial class RiskControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "MaxPortfolioRiskPercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 4m);
            migrationBuilder.AddColumn<int>(name: "MaxSimultaneousPositions", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 2);
            migrationBuilder.AddColumn<int>(name: "MaxConsecutiveLosses", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 5);
            migrationBuilder.AddColumn<int>(name: "CooldownMinutes", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 30);
            migrationBuilder.AddColumn<decimal>(name: "MinimumLiquidationSafetyBufferPercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 1m);

            migrationBuilder.AddColumn<decimal>(name: "EquityAtEntry", table: "Positions", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "LiquidationPrice", table: "Positions", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "EstimatedEntryFee", table: "Positions", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "EstimatedExitFee", table: "Positions", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "EstimatedSlippage", table: "Positions", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "EstimatedTotalRisk", table: "Positions", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaxPortfolioRiskPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxSimultaneousPositions", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxConsecutiveLosses", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "CooldownMinutes", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MinimumLiquidationSafetyBufferPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "EquityAtEntry", table: "Positions");
            migrationBuilder.DropColumn(name: "LiquidationPrice", table: "Positions");
            migrationBuilder.DropColumn(name: "EstimatedEntryFee", table: "Positions");
            migrationBuilder.DropColumn(name: "EstimatedExitFee", table: "Positions");
            migrationBuilder.DropColumn(name: "EstimatedSlippage", table: "Positions");
            migrationBuilder.DropColumn(name: "EstimatedTotalRisk", table: "Positions");
        }
    }
}
