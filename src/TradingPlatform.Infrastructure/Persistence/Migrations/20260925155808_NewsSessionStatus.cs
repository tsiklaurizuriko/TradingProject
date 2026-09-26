using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsSessionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastStatus",
                table: "NewsTradingSessions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastStatus",
                table: "NewsTradingSessions");
        }
    }
}
