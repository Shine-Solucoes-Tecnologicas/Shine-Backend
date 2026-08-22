using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSubscriptionCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckoutIdempotencyKey",
                table: "BillingSubscriptions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubscriptionId",
                table: "BillingSubscriptions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderCode",
                table: "BillingSubscriptions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptions_AccountId_CheckoutIdempotencyKey",
                table: "BillingSubscriptions",
                columns: new[] { "AccountId", "CheckoutIdempotencyKey" },
                unique: true,
                filter: "\"CheckoutIdempotencyKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingSubscriptions_ProviderCode_ExternalSubscriptionId",
                table: "BillingSubscriptions",
                columns: new[] { "ProviderCode", "ExternalSubscriptionId" },
                unique: true,
                filter: "\"ExternalSubscriptionId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingSubscriptions_AccountId_CheckoutIdempotencyKey",
                table: "BillingSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_BillingSubscriptions_ProviderCode_ExternalSubscriptionId",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "CheckoutIdempotencyKey",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "ExternalSubscriptionId",
                table: "BillingSubscriptions");

            migrationBuilder.DropColumn(
                name: "ProviderCode",
                table: "BillingSubscriptions");
        }
    }
}
