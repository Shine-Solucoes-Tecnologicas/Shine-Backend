using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookClaimIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_BillingProviderWebhookInbox_LockedUntilUtc",
                table: "BillingProviderWebhookInbox",
                column: "LockedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BillingProviderWebhookInbox_Status_ReceivedAtUtc",
                table: "BillingProviderWebhookInbox",
                columns: new[] { "Status", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingProviderWebhookInbox_LockedUntilUtc",
                table: "BillingProviderWebhookInbox");

            migrationBuilder.DropIndex(
                name: "IX_BillingProviderWebhookInbox_Status_ReceivedAtUtc",
                table: "BillingProviderWebhookInbox");
        }
    }
}
