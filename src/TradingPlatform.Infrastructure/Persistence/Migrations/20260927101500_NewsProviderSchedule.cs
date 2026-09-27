using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsProviderSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "NewsProviderHealth",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextEligibleUtc",
                table: "NewsProviderHealth",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "NewsProviderHealth");

            migrationBuilder.DropColumn(
                name: "NextEligibleUtc",
                table: "NewsProviderHealth");
        }
    }
}
