using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(TradingDbContext))]
    [Migration("20260918150000_PercentRRisk")]
    public partial class PercentRRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaxPositionPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxOpenPositions", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MarginMode", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "AllowedSymbolsCsv", table: "RiskProfiles");

            migrationBuilder.AddColumn<decimal>(
                name: "RiskPerTradePercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0.5m);
            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 2m);
            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 4m);
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "RiskProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "AvailableBalanceAtEntry",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "RiskPerTradePercent",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPercent",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPrice",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "TakeProfitPrice",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "NotionalUsdt",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(
                name: "Leverage",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "RiskPerTradePercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "StopLossPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "TakeProfitPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "IsActive", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "AvailableBalanceAtEntry", table: "Positions");
            migrationBuilder.DropColumn(name: "RiskPerTradePercent", table: "Positions");
            migrationBuilder.DropColumn(name: "TakeProfitPercent", table: "Positions");
            migrationBuilder.DropColumn(name: "StopLossPrice", table: "Positions");
            migrationBuilder.DropColumn(name: "TakeProfitPrice", table: "Positions");
            migrationBuilder.DropColumn(name: "NotionalUsdt", table: "Positions");
            migrationBuilder.DropColumn(name: "Leverage", table: "Positions");

            migrationBuilder.AddColumn<decimal>(name: "MaxPositionPercent", table: "RiskProfiles", type: "numeric(28,8)", precision: 28, scale: 8, nullable: false, defaultValue: 8m);
            migrationBuilder.AddColumn<int>(name: "MaxOpenPositions", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 8);
            migrationBuilder.AddColumn<int>(name: "MarginMode", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "AllowedSymbolsCsv", table: "RiskProfiles", type: "character varying(4000)", maxLength: 4000, nullable: true);
        }
    }
}
