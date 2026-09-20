using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TradingPlatform.Infrastructure.Persistence;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TradingDbContext))]
[Migration("20260920193000_SharedCatalog")]
public partial class SharedCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE TEMP TABLE twin_strategy (live_id uuid PRIMARY KEY, paper_id uuid NOT NULL);

            INSERT INTO twin_strategy (live_id, paper_id)
            SELECT live_s."Id", paper_s."Id"
            FROM "Strategies" live_s
            JOIN "Strategies" paper_s
              ON paper_s."Mode" = 0
             AND paper_s."DeletedAt" IS NULL
             AND lower(paper_s."Name") = lower(live_s."Name")
             AND lower(paper_s."TemplateKey") = lower(live_s."TemplateKey")
            WHERE live_s."Mode" = 2;

            INSERT INTO twin_strategy (live_id, paper_id)
            SELECT live_s."Id", (
              SELECT paper_s."Id"
              FROM "Strategies" paper_s
              WHERE paper_s."Mode" = 0
                AND paper_s."DeletedAt" IS NULL
                AND lower(paper_s."TemplateKey") = lower(live_s."TemplateKey")
              LIMIT 1
            )
            FROM "Strategies" live_s
            WHERE live_s."Mode" = 2
              AND NOT EXISTS (SELECT 1 FROM twin_strategy t WHERE t.live_id = live_s."Id");

            DELETE FROM twin_strategy WHERE paper_id IS NULL;

            INSERT INTO twin_strategy (live_id, paper_id)
            SELECT live_s."Id", (SELECT "Id" FROM "Strategies" WHERE "Mode" = 0 AND "DeletedAt" IS NULL ORDER BY "Name" LIMIT 1)
            FROM "Strategies" live_s
            WHERE live_s."Mode" = 2
              AND NOT EXISTS (SELECT 1 FROM twin_strategy t WHERE t.live_id = live_s."Id")
              AND EXISTS (SELECT 1 FROM "Strategies" WHERE "Mode" = 0 AND "DeletedAt" IS NULL);

            CREATE TEMP TABLE twin_version (live_vid uuid PRIMARY KEY, paper_vid uuid NOT NULL);

            INSERT INTO twin_version (live_vid, paper_vid)
            SELECT live_v."Id", paper_v."Id"
            FROM "StrategyVersions" live_v
            JOIN twin_strategy t ON t.live_id = live_v."StrategyId"
            JOIN "StrategyVersions" paper_v
              ON paper_v."StrategyId" = t.paper_id
             AND paper_v."VersionNumber" = live_v."VersionNumber";

            INSERT INTO twin_version (live_vid, paper_vid)
            SELECT live_v."Id", (
              SELECT paper_v."Id"
              FROM "StrategyVersions" paper_v
              WHERE paper_v."StrategyId" = t.paper_id
              ORDER BY paper_v."VersionNumber" DESC
              LIMIT 1
            )
            FROM "StrategyVersions" live_v
            JOIN twin_strategy t ON t.live_id = live_v."StrategyId"
            WHERE NOT EXISTS (SELECT 1 FROM twin_version tv WHERE tv.live_vid = live_v."Id");

            DELETE FROM twin_version WHERE paper_vid IS NULL;

            UPDATE "Bots" b SET "StrategyVersionId" = tv.paper_vid
            FROM twin_version tv
            WHERE b."StrategyVersionId" = tv.live_vid;

            UPDATE "Orders" o
            SET "StrategyVersionId" = tv.paper_vid,
                "StrategyId" = t.paper_id
            FROM twin_version tv
            JOIN "StrategyVersions" live_v ON live_v."Id" = tv.live_vid
            JOIN twin_strategy t ON t.live_id = live_v."StrategyId"
            WHERE o."StrategyVersionId" = tv.live_vid;

            UPDATE "Signals" s
            SET "StrategyVersionId" = tv.paper_vid,
                "StrategyId" = t.paper_id
            FROM twin_version tv
            JOIN "StrategyVersions" live_v ON live_v."Id" = tv.live_vid
            JOIN twin_strategy t ON t.live_id = live_v."StrategyId"
            WHERE s."StrategyVersionId" = tv.live_vid;

            UPDATE "Trades" tr
            SET "StrategyVersionId" = tv.paper_vid,
                "StrategyId" = t.paper_id
            FROM twin_version tv
            JOIN "StrategyVersions" live_v ON live_v."Id" = tv.live_vid
            JOIN twin_strategy t ON t.live_id = live_v."StrategyId"
            WHERE tr."StrategyVersionId" = tv.live_vid;

            UPDATE "Backtests" b
            SET "StrategyVersionId" = tv.paper_vid
            FROM twin_version tv
            WHERE b."StrategyVersionId" = tv.live_vid;

            UPDATE "Orders" o SET "StrategyId" = t.paper_id
            FROM twin_strategy t
            WHERE o."StrategyId" = t.live_id;

            UPDATE "Signals" s SET "StrategyId" = t.paper_id
            FROM twin_strategy t
            WHERE s."StrategyId" = t.live_id;

            UPDATE "Trades" tr SET "StrategyId" = t.paper_id
            FROM twin_strategy t
            WHERE tr."StrategyId" = t.live_id;

            UPDATE "Bots" b
            SET "RiskProfileId" = paper_r."Id"
            FROM "RiskProfiles" live_r
            JOIN "RiskProfiles" paper_r
              ON paper_r."Mode" = 0
             AND paper_r."DeletedAt" IS NULL
             AND paper_r."Name" = live_r."Name"
            WHERE b."RiskProfileId" = live_r."Id"
              AND live_r."Mode" = 2;

            UPDATE "Bots" b
            SET "RiskProfileId" = (
              SELECT paper_r."Id"
              FROM "RiskProfiles" paper_r
              WHERE paper_r."Mode" = 0
                AND paper_r."DeletedAt" IS NULL
                AND paper_r."IsActive"
              LIMIT 1
            )
            FROM "RiskProfiles" live_r
            WHERE b."RiskProfileId" = live_r."Id"
              AND live_r."Mode" = 2
              AND EXISTS (
                SELECT 1 FROM "RiskProfiles" paper_r
                WHERE paper_r."Mode" = 0 AND paper_r."DeletedAt" IS NULL AND paper_r."IsActive"
              );

            DO $$
            BEGIN
              IF EXISTS (
                SELECT 1 FROM "Bots" b
                JOIN "StrategyVersions" v ON v."Id" = b."StrategyVersionId"
                JOIN "Strategies" s ON s."Id" = v."StrategyId"
                WHERE s."Mode" = 2)
              THEN
                RAISE EXCEPTION 'SharedCatalog: bots still reference live strategy twins';
              END IF;
              IF EXISTS (
                SELECT 1 FROM "Bots" b
                JOIN "RiskProfiles" r ON r."Id" = b."RiskProfileId"
                WHERE r."Mode" = 2)
              THEN
                RAISE EXCEPTION 'SharedCatalog: bots still reference live risk twins';
              END IF;
              IF EXISTS (SELECT 1 FROM "Orders" o JOIN "Strategies" s ON s."Id" = o."StrategyId" WHERE s."Mode" = 2)
                 OR EXISTS (SELECT 1 FROM "Signals" x JOIN "Strategies" s ON s."Id" = x."StrategyId" WHERE s."Mode" = 2)
                 OR EXISTS (SELECT 1 FROM "Trades" t JOIN "Strategies" s ON s."Id" = t."StrategyId" WHERE s."Mode" = 2)
                 OR EXISTS (
                   SELECT 1 FROM "Backtests" b
                   JOIN "StrategyVersions" v ON v."Id" = b."StrategyVersionId"
                   JOIN "Strategies" s ON s."Id" = v."StrategyId"
                   WHERE s."Mode" = 2)
              THEN
                RAISE EXCEPTION 'SharedCatalog: ledger still references live strategy twins';
              END IF;
            END $$;

            DELETE FROM "StrategyVersions" WHERE "StrategyId" IN (SELECT "Id" FROM "Strategies" WHERE "Mode" = 2);
            DELETE FROM "Strategies" WHERE "Mode" = 2;
            DELETE FROM "RiskProfiles" WHERE "Mode" = 2;
            """);

        migrationBuilder.DropIndex(name: "IX_Strategies_UserId_Name_Mode", table: "Strategies");
        migrationBuilder.DropIndex(name: "IX_RiskProfiles_Name_Mode", table: "RiskProfiles");
        migrationBuilder.DropColumn(name: "Mode", table: "Strategies");
        migrationBuilder.DropColumn(name: "Mode", table: "RiskProfiles");
        migrationBuilder.CreateIndex(name: "IX_Strategies_UserId_Name", table: "Strategies", columns: new[] { "UserId", "Name" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_Strategies_UserId_Name", table: "Strategies");
        migrationBuilder.AddColumn<int>(name: "Mode", table: "Strategies", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "Mode", table: "RiskProfiles", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.CreateIndex(name: "IX_Strategies_UserId_Name_Mode", table: "Strategies", columns: new[] { "UserId", "Name", "Mode" });
        migrationBuilder.CreateIndex(name: "IX_RiskProfiles_Name_Mode", table: "RiskProfiles", columns: new[] { "Name", "Mode" });
    }
}
