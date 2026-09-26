using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsTrading : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsArticles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderArticleId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Summary = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsArticles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DedupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PrimaryAsset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MarketScope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Direction = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Impact = table.Column<double>(type: "double precision", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ArticleIds = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsTradingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Running = table.Column<bool>(type: "boolean", nullable: false),
                    Mode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StoppedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UniverseCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsTradingSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsEventAssets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredNewsEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Asset = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Relevance = table.Column<double>(type: "double precision", nullable: false),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsEventAssets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsEventAssets_NewsEvents_StoredNewsEventId",
                        column: x => x.StoredNewsEventId,
                        principalTable: "NewsEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsTradingSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoredNewsEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SignalTimeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    NewsScore = table.Column<double>(type: "double precision", nullable: false),
                    MarketScore = table.Column<double>(type: "double precision", nullable: false),
                    FinalScore = table.Column<double>(type: "double precision", nullable: false),
                    NewsImpact = table.Column<double>(type: "double precision", nullable: false),
                    NewsConfidence = table.Column<double>(type: "double precision", nullable: false),
                    Relevance = table.Column<double>(type: "double precision", nullable: false),
                    StrategyName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    MarketDetail = table.Column<string>(type: "text", nullable: false),
                    RiskDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskReason = table.Column<string>(type: "text", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Notional = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Leverage = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Margin = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    TakeProfitPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    OrderClientId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BecameTrade = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsTradingSignals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsTradingSignals_NewsEvents_StoredNewsEventId",
                        column: x => x.StoredNewsEventId,
                        principalTable: "NewsEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NewsSignalOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NewsTradingSignalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Horizon = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    ReferencePrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    FuturePrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    ReturnPercent = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    ObservedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsSignalOutcomes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NewsSignalOutcomes_NewsTradingSignals_NewsTradingSignalId",
                        column: x => x.NewsTradingSignalId,
                        principalTable: "NewsTradingSignals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_CanonicalUrl",
                table: "NewsArticles",
                column: "CanonicalUrl");

            migrationBuilder.CreateIndex(
                name: "IX_NewsArticles_Provider_ProviderArticleId",
                table: "NewsArticles",
                columns: new[] { "Provider", "ProviderArticleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsEventAssets_StoredNewsEventId_Asset",
                table: "NewsEventAssets",
                columns: new[] { "StoredNewsEventId", "Asset" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsEvents_DedupKey",
                table: "NewsEvents",
                column: "DedupKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsSignalOutcomes_NewsTradingSignalId_Horizon",
                table: "NewsSignalOutcomes",
                columns: new[] { "NewsTradingSignalId", "Horizon" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsTradingSignals_StoredNewsEventId_Symbol",
                table: "NewsTradingSignals",
                columns: new[] { "StoredNewsEventId", "Symbol" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NewsArticles");

            migrationBuilder.DropTable(
                name: "NewsEventAssets");

            migrationBuilder.DropTable(
                name: "NewsSignalOutcomes");

            migrationBuilder.DropTable(
                name: "NewsTradingSessions");

            migrationBuilder.DropTable(
                name: "NewsTradingSignals");

            migrationBuilder.DropTable(
                name: "NewsEvents");
        }
    }
}
