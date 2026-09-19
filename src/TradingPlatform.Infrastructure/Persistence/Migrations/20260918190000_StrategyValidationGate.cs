using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TradingDbContext))]
[Migration("20260918190000_StrategyValidationGate")]
public partial class StrategyValidationGate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsEnabled",
            table: "Strategies",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<string>(
            name: "ValidationStatus",
            table: "Strategies",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "VALIDATION_PENDING");

        migrationBuilder.AddColumn<string>(
            name: "Side",
            table: "BacktestTrades",
            type: "character varying(8)",
            maxLength: 8,
            nullable: false,
            defaultValue: "Long");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsEnabled", table: "Strategies");
        migrationBuilder.DropColumn(name: "ValidationStatus", table: "Strategies");
        migrationBuilder.DropColumn(name: "Side", table: "BacktestTrades");
    }
}
