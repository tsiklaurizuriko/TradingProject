using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds a nullable net PnL and funding to trades. <c>PnL</c> now means gross everywhere.
    /// Only Binance-synced closed rows (gross <c>realizedPnl</c>, correlation BNT/BNI) with a certified USDT fee get a net now.
    /// Older locally booked rows mixed gross and exit-fee-only net, so they stay pending until the next Binance sync rewrites them.
    /// </summary>
    [DbContext(typeof(TradingDbContext))]
    [Migration("20261005120000_TradeNetPnl")]
    public partial class TradeNetPnl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NetPnL",
                table: "Trades",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FundingPnL",
                table: "Trades",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "Trades"
                SET "NetPnL" = "PnL" - "Fees"
                WHERE "ClosedAt" IS NOT NULL
                  AND "FeeStatus" = 1
                  AND ("Fees" = 0 OR upper("FeeAsset") = 'USDT')
                  AND ("CorrelationId" LIKE 'BNT%' OR "CorrelationId" LIKE 'BNI%');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "NetPnL", table: "Trades");
            migrationBuilder.DropColumn(name: "FundingPnL", table: "Trades");
        }
    }
}
