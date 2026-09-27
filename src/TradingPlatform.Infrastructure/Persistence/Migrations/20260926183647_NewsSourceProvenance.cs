using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsSourceProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Publisher",
                table: "NewsArticles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""UPDATE "NewsArticles" SET "Publisher" = "Source" WHERE "Publisher" = '';""");
            migrationBuilder.Sql("""
                UPDATE "NewsArticles" SET "Provider" = 'rss' WHERE "ProviderArticleId" LIKE 'rss:%';
                UPDATE "NewsArticles" SET "Provider" = 'gdelt' WHERE "ProviderArticleId" LIKE 'gdelt:%';
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "StoredNewsEventId",
                table: "NewsArticles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NewsArticleSightings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsArticleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderArticleId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsArticleSightings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsArticleSightings_NewsArticles_NewsArticleId",
                        column: x => x.NewsArticleId,
                        principalTable: "NewsArticles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsProviderHealth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastAttemptUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSuccessUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    LastErrorUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedCount = table.Column<int>(type: "integer", nullable: false),
                    InsertedCount = table.Column<int>(type: "integer", nullable: false),
                    DeduplicatedCount = table.Column<int>(type: "integer", nullable: false),
                    RejectedCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsProviderHealth", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_StoredNewsEventId",
                table: "NewsArticles",
                column: "StoredNewsEventId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticleSightings_NewsArticleId",
                table: "NewsArticleSightings",
                column: "NewsArticleId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticleSightings_Provider_ProviderArticleId",
                table: "NewsArticleSightings",
                columns: new[] { "Provider", "ProviderArticleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsProviderHealth_Provider",
                table: "NewsProviderHealth",
                column: "Provider",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "NewsArticleSightings" ("Id", "NewsArticleId", "Provider", "ProviderArticleId", "ReceivedAtUtc", "CreatedAt", "UpdatedAt")
                SELECT gen_random_uuid(), a."Id", a."Provider", a."ProviderArticleId", a."ReceivedAtUtc", NOW(), NOW()
                FROM "NewsArticles" a
                WHERE NOT EXISTS (
                    SELECT 1 FROM "NewsArticleSightings" s
                    WHERE s."Provider" = a."Provider" AND s."ProviderArticleId" = a."ProviderArticleId");
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_NewsArticles_NewsEvents_StoredNewsEventId",
                table: "NewsArticles",
                column: "StoredNewsEventId",
                principalTable: "NewsEvents",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NewsArticles_NewsEvents_StoredNewsEventId",
                table: "NewsArticles");

            migrationBuilder.DropTable(
                name: "NewsArticleSightings");

            migrationBuilder.DropTable(
                name: "NewsProviderHealth");

            migrationBuilder.DropIndex(
                name: "IX_NewsArticles_StoredNewsEventId",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "Publisher",
                table: "NewsArticles");

            migrationBuilder.DropColumn(
                name: "StoredNewsEventId",
                table: "NewsArticles");
        }
    }
}
