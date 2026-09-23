using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NearMissAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HypothesisId",
                table: "Trades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAdverseExcursion",
                table: "Trades",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxFavorableExcursion",
                table: "Trades",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignalAt",
                table: "Trades",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StrategyFamily",
                table: "Trades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxAdverseExcursion",
                table: "Positions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxFavorableExcursion",
                table: "Positions",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HypothesisId",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "MaxAdverseExcursion",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "MaxFavorableExcursion",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "SignalAt",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "StrategyFamily",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "MaxAdverseExcursion",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "MaxFavorableExcursion",
                table: "Positions");
        }
    }
}
