using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The Binance history sync gave every round trip on a coin to one bot on that coin, so live strategies held each
    /// other's trades and manual or pump-short trades. A Binance-synced live trade now belongs to the bot whose filled
    /// <c>L</c> market order on the entry side sits within 15 minutes of the open, under the strategy stamped on that order.
    /// A trade with no such entry and a news, pump, or manual app fill from open to close is removed. Others stay as they are.
    /// </summary>
    [DbContext(typeof(TradingDbContext))]
    [Migration("20261113120000_TradeOwnerFromEntryOrder")]
    public partial class TradeOwnerFromEntryOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TEMP TABLE trade_entry AS
                SELECT t."Id",
                       t."Symbol",
                       t."OpenedAt",
                       t."ClosedAt",
                       e."ClientOrderId" AS entry_id,
                       e."BotId" AS order_bot,
                       e."StrategyId" AS order_strategy,
                       e."StrategyVersionId" AS order_version,
                       coalesce(e."CorrelationId", '') AS order_correlation
                FROM "Trades" t
                JOIN "Bots" b ON b."Id" = t."BotId" AND b."Mode" = 2
                LEFT JOIN LATERAL (
                    SELECT o.*
                    FROM "Orders" o
                    WHERE o."Mode" = 2
                      AND o."Status" = 4
                      AND o."Type" = 0
                      AND o."Symbol" = t."Symbol"
                      AND o."Side" = t."Side"
                      AND o."ClientOrderId" LIKE 'L%'
                      AND o."CreatedAt" BETWEEN t."OpenedAt" - interval '15 minutes' AND t."OpenedAt" + interval '15 minutes'
                    ORDER BY abs(extract(epoch FROM o."CreatedAt" - t."OpenedAt"))
                    LIMIT 1) e ON true
                WHERE t."ClosedAt" IS NOT NULL
                  AND (t."CorrelationId" LIKE 'BNT%' OR t."CorrelationId" LIKE 'BNI%');

                DELETE FROM "Trades" t
                USING trade_entry x
                WHERE t."Id" = x."Id"
                  AND x.entry_id IS NULL
                  AND EXISTS (
                      SELECT 1
                      FROM "Orders" o
                      WHERE o."Mode" = 2
                        AND o."Symbol" = x."Symbol"
                        AND o."CreatedAt" BETWEEN x."OpenedAt" - interval '5 minutes' AND x."ClosedAt" + interval '5 minutes'
                        AND (o."ClientOrderId" LIKE 'news-%'
                             OR o."ClientOrderId" LIKE 'pump-%'
                             OR (o."Status" = 4
                                 AND o."Type" IN (0, 1)
                                 AND (o."ClientOrderId" LIKE 'ios\_%'
                                      OR o."ClientOrderId" LIKE 'android\_%'
                                      OR o."ClientOrderId" LIKE 'web\_%'
                                      OR o."ClientOrderId" LIKE 'electron\_%'))));

                UPDATE "Trades" t
                SET "BotId" = owner."Id",
                    "StrategyId" = CASE
                        WHEN x.order_bot = owner."Id" AND x.order_correlation NOT LIKE 'binance-%' THEN x.order_strategy
                        ELSE v."StrategyId" END,
                    "StrategyVersionId" = CASE
                        WHEN x.order_bot = owner."Id" AND x.order_correlation NOT LIKE 'binance-%' THEN x.order_version
                        ELSE owner."StrategyVersionId" END
                FROM trade_entry x
                JOIN LATERAL (
                    SELECT b.*
                    FROM "Bots" b
                    WHERE b."Mode" = 2
                      AND b."Symbol" = x."Symbol"
                      AND replace(b."Id"::text, '-', '') LIKE lower(substr(x.entry_id, 2, 11)) || '%'
                    LIMIT 1) owner ON true
                JOIN "StrategyVersions" v ON v."Id" = owner."StrategyVersionId"
                WHERE t."Id" = x."Id"
                  AND x.entry_id IS NOT NULL;

                DROP TABLE trade_entry;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
