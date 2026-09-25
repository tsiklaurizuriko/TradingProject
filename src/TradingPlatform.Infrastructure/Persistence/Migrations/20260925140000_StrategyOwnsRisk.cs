using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TradingDbContext))]
[Migration("20260925140000_StrategyOwnsRisk")]
public partial class StrategyOwnsRisk : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "RiskProfileId",
            table: "Strategies",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_Strategies_RiskProfileId",
            table: "Strategies",
            column: "RiskProfileId");

        migrationBuilder.AddForeignKey(
            name: "FK_Strategies_RiskProfiles_RiskProfileId",
            table: "Strategies",
            column: "RiskProfileId",
            principalTable: "RiskProfiles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Strategies_RiskProfiles_RiskProfileId",
            table: "Strategies");

        migrationBuilder.DropIndex(
            name: "IX_Strategies_RiskProfileId",
            table: "Strategies");

        migrationBuilder.DropColumn(
            name: "RiskProfileId",
            table: "Strategies");
    }
}
