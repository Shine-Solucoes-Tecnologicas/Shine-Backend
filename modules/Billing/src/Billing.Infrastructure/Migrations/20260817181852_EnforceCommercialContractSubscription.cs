using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCommercialContractSubscription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BillingCommercialContracts_SubscriptionId",
                table: "BillingCommercialContracts");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_BillingSubscriptions_Id_AccountId",
                table: "BillingSubscriptions",
                columns: new[] { "Id", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCommercialContracts_SubscriptionId_AccountId",
                table: "BillingCommercialContracts",
                columns: new[] { "SubscriptionId", "AccountId" });

            migrationBuilder.AddForeignKey(
                name: "FK_BillingCommercialContracts_BillingSubscriptions_Subscriptio~",
                table: "BillingCommercialContracts",
                columns: new[] { "SubscriptionId", "AccountId" },
                principalTable: "BillingSubscriptions",
                principalColumns: new[] { "Id", "AccountId" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BillingCommercialContracts_BillingSubscriptions_Subscriptio~",
                table: "BillingCommercialContracts");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_BillingSubscriptions_Id_AccountId",
                table: "BillingSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_BillingCommercialContracts_SubscriptionId_AccountId",
                table: "BillingCommercialContracts");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCommercialContracts_SubscriptionId",
                table: "BillingCommercialContracts",
                column: "SubscriptionId");
        }
    }
}
