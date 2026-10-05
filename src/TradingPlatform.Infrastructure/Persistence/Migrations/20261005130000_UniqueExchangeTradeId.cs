using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One execution row per exchange trade id. Existing duplicates keep the earliest row (lowest CreatedAt, then Id).
    /// </summary>
    [DbContext(typeof(TradingDbContext))]
    [Migration("20261005130000_UniqueExchangeTradeId")]
    public partial class UniqueExchangeTradeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "Executions" e
                USING (
                    SELECT "Id",
                           row_number() OVER (PARTITION BY "ExchangeTradeId" ORDER BY "CreatedAt", "Id") AS rn
                    FROM "Executions"
                    WHERE "ExchangeTradeId" IS NOT NULL
                ) d
                WHERE e."Id" = d."Id" AND d.rn > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_Executions_ExchangeTradeId",
                table: "Executions");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_ExchangeTradeId",
                table: "Executions",
                column: "ExchangeTradeId",
                unique: true,
                filter: "\"ExchangeTradeId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Executions_ExchangeTradeId",
                table: "Executions");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_ExchangeTradeId",
                table: "Executions",
                column: "ExchangeTradeId");
        }
    }
}
