using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PersistSubscriptionsOutboxAndWebhookClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntilUtc",
                table: "BillingProviderWebhookInbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingId",
                table: "BillingProviderWebhookInbox",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingOutbox",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessingId = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingOutbox", x => x.EventId);
                });

            migrationBuilder.CreateTable(
                name: "BillingSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    PendingPlanId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingPlanEffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Interval = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentPeriodStartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentPeriodEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancellationEffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CanceledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSubscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingSubscriptionUnits",
                columns: table => new
                {
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEffective = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingSubscriptionUnits", x => new { x.SubscriptionId, x.UnitId });
                    table.ForeignKey(
                        name: "FK_BillingSubscriptionUnits_BillingSubscriptions_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "BillingSubscriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingOutbox_LockedUntilUtc",
                table: "BillingOutbox",
                column: "LockedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BillingOutbox_Status_NextAttemptAtUtc",
                table: "BillingOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptions_AccountId",
                table: "BillingSubscriptions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptionUnits_UnitId",
                table: "BillingSubscriptionUnits",
                column: "UnitId",
                unique: true,
                filter: "\"IsEffective\" = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingOutbox");

            migrationBuilder.DropTable(
                name: "BillingSubscriptionUnits");

            migrationBuilder.DropTable(
                name: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "LockedUntilUtc",
                table: "BillingProviderWebhookInbox");

            migrationBuilder.DropColumn(
                name: "ProcessingId",
                table: "BillingProviderWebhookInbox");
        }
    }
}
