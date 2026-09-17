using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProfessionalRiskControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CorrelationFactor",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0.75m);

            migrationBuilder.AddColumn<int>(
                name: "MarginMode",
                table: "RiskProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxPortfolioHeatPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 4m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxTotalExposurePercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 60m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinFreeMarginPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 20m);

            migrationBuilder.AddColumn<bool>(
                name: "StopAccountOnDailyLoss",
                table: "RiskProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<decimal>(
                name: "InitialRiskUsdt",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "StopLossPercent",
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
            migrationBuilder.DropColumn(
                name: "CorrelationFactor",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "MarginMode",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "MaxPortfolioHeatPercent",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "MaxTotalExposurePercent",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "MinFreeMarginPercent",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "StopAccountOnDailyLoss",
                table: "RiskProfiles");

            migrationBuilder.DropColumn(
                name: "InitialRiskUsdt",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "StopLossPercent",
                table: "Positions");
        }
    }
}
