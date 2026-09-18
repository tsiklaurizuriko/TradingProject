using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(TradingDbContext))]
    [Migration("20260918110000_AbsoluteRiskCaps")]
    public partial class AbsoluteRiskCaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxRiskUsdt",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxNotionalUsdt",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDailyLossUsdt",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeeklyLossPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 6m);

            migrationBuilder.AddColumn<decimal>(
                name: "StopSlippageMultiplier",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 1.25m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinStopAtrMultiple",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<bool>(
                name: "AllowLive",
                table: "RiskProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "MarginUsdt",
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
            migrationBuilder.DropColumn(name: "MaxRiskUsdt", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxNotionalUsdt", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxDailyLossUsdt", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxWeeklyLossPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "StopSlippageMultiplier", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MinStopAtrMultiple", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "AllowLive", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MarginUsdt", table: "Positions");
        }
    }
}
