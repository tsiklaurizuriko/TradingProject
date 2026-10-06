using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NewsDeepAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NewsAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AffectedAssetsJson = table.Column<string>(type: "text", nullable: false),
                    AlreadyPricedIn = table.Column<int>(type: "integer", nullable: false),
                    ArticleIds = table.Column<string>(type: "text", nullable: false),
                    ClassifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClassificationLatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DetectionLatencyMs = table.Column<long>(type: "bigint", nullable: false),
                    EventDedupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EventType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedHorizon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Impact = table.Column<int>(type: "integer", nullable: false),
                    MarketMechanism = table.Column<string>(type: "text", nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Novelty = table.Column<int>(type: "integer", nullable: false),
                    OverallConfidence = table.Column<int>(type: "integer", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    RiskFlagsJson = table.Column<string>(type: "text", nullable: false),
                    ShouldConsiderTrading = table.Column<bool>(type: "boolean", nullable: false),
                    SourceReliability = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoredNewsEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerificationStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsAnalyses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsDecisionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecisionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EventDedupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsDecisionAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsTradeExecutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    EventDedupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExchangeOrderId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExitReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ExpectedHorizonMinutes = table.Column<int>(type: "integer", nullable: false),
                    Fees = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    FundingRate = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    HoldingSeconds = table.Column<long>(type: "bigint", nullable: true),
                    NewsTradingDecisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    NewsToFillLatencyMs = table.Column<long>(type: "bigint", nullable: true),
                    OrderAcceptedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrderFilledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrderRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    RealizedPnL = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    RiskDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskReason = table.Column<string>(type: "text", nullable: true),
                    Side = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StopLossPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TakeProfitPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsTradeExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NewsTradingDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AlreadyPricedIn = table.Column<int>(type: "integer", nullable: false),
                    ArticleIds = table.Column<string>(type: "text", nullable: false),
                    ClassifiedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Confidence = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DecisionAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EventDedupKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedHorizon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Impact = table.Column<int>(type: "integer", nullable: false),
                    MarketContextJson = table.Column<string>(type: "text", nullable: false),
                    NewsAnalysisId = table.Column<Guid>(type: "uuid", nullable: true),
                    Novelty = table.Column<int>(type: "integer", nullable: false),
                    ProposedDirection = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RiskDecision = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskReason = table.Column<string>(type: "text", nullable: true),
                    StoredNewsEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Symbol = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsTradingDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NewsAnalyses_EventDedupKey",
                table: "NewsAnalyses",
                column: "EventDedupKey");

            migrationBuilder.CreateIndex(
                name: "IX_NewsDecisionAudits_EventDedupKey_Symbol_DecisionAtUtc",
                table: "NewsDecisionAudits",
                columns: new[] { "EventDedupKey", "Symbol", "DecisionAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NewsTradeExecutions_ClientOrderId",
                table: "NewsTradeExecutions",
                column: "ClientOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsTradingDecisions_EventDedupKey_Symbol_DecisionAtUtc",
                table: "NewsTradingDecisions",
                columns: new[] { "EventDedupKey", "Symbol", "DecisionAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "NewsAnalyses");
            migrationBuilder.DropTable(name: "NewsDecisionAudits");
            migrationBuilder.DropTable(name: "NewsTradeExecutions");
            migrationBuilder.DropTable(name: "NewsTradingDecisions");
        }
    }
}
