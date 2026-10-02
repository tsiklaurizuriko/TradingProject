using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds fee knowledge without rewriting stored amounts.
    /// Existing rows stay <c>FeeStatus = 0</c> (Unknown). A stored 0 is not certified as a real zero.
    /// </summary>
    [DbContext(typeof(TradingDbContext))]
    [Migration("20261002183000_FeeKnowledge")]
    public partial class FeeKnowledge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeeAsset",
                table: "Trades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FeeStatus",
                table: "Trades",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FeeStatus",
                table: "Executions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FeeAsset",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "FeeStatus",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "FeeStatus",
                table: "Executions");
        }
    }
}
