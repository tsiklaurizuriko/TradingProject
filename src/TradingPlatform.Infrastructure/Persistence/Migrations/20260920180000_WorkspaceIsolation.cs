using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TradingDbContext))]
[Migration("20260920180000_WorkspaceIsolation")]
public partial class WorkspaceIsolation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "Strategies",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "Mode",
            table: "RiskProfiles",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.DropIndex(
            name: "IX_Strategies_UserId",
            table: "Strategies");

        migrationBuilder.CreateIndex(
            name: "IX_Strategies_UserId_Name_Mode",
            table: "Strategies",
            columns: new[] { "UserId", "Name", "Mode" });

        migrationBuilder.CreateIndex(
            name: "IX_RiskProfiles_Name_Mode",
            table: "RiskProfiles",
            columns: new[] { "Name", "Mode" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Strategies_UserId_Name_Mode", table: "Strategies");
        migrationBuilder.DropIndex(name: "IX_RiskProfiles_Name_Mode", table: "RiskProfiles");
        migrationBuilder.DropColumn(name: "Mode", table: "Strategies");
        migrationBuilder.DropColumn(name: "Mode", table: "RiskProfiles");
        migrationBuilder.CreateIndex(
            name: "IX_Strategies_UserId",
            table: "Strategies",
            column: "UserId");
    }
}
