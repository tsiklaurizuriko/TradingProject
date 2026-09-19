using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TradingDbContext))]
[Migration("20260918180000_StrategyTemplates")]
public partial class StrategyTemplates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AllowedSide",
            table: "Strategies",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Long");

        migrationBuilder.AddColumn<string>(
            name: "TemplateKey",
            table: "Strategies",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "ema_rsi_trend");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AllowedSide", table: "Strategies");
        migrationBuilder.DropColumn(name: "TemplateKey", table: "Strategies");
    }
}
