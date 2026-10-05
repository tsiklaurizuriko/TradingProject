using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <summary>Weekly loss and equity drawdown halts on every risk profile. Existing rows get the domain defaults.</summary>
    [DbContext(typeof(TradingDbContext))]
    [Migration("20261005140000_RiskLossLimits")]
    public partial class RiskLossLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeeklyLossPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 8m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDrawdownPercent",
                table: "RiskProfiles",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 15m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "MaxWeeklyLossPercent", table: "RiskProfiles");
            migrationBuilder.DropColumn(name: "MaxDrawdownPercent", table: "RiskProfiles");
        }
    }
}
