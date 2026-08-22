using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFinancialLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CycleStartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CycleEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingInvoices", x => x.Id);
                    table.UniqueConstraint("AK_BillingInvoices_Id_AccountId_SubscriptionId", x => new { x.Id, x.AccountId, x.SubscriptionId });
                    table.ForeignKey(
                        name: "FK_BillingInvoices_BillingSubscriptions_SubscriptionId_Account~",
                        columns: x => new { x.SubscriptionId, x.AccountId },
                        principalTable: "BillingSubscriptions",
                        principalColumns: new[] { "Id", "AccountId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingCharges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCharges", x => x.Id);
                    table.UniqueConstraint("AK_BillingCharges_Id_AccountId_SubscriptionId", x => new { x.Id, x.AccountId, x.SubscriptionId });
                    table.ForeignKey(
                        name: "FK_BillingCharges_BillingInvoices_InvoiceId_AccountId_Subscrip~",
                        columns: x => new { x.InvoiceId, x.AccountId, x.SubscriptionId },
                        principalTable: "BillingInvoices",
                        principalColumns: new[] { "Id", "AccountId", "SubscriptionId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingFinancialTransitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EntityType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RelatedEntityId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingFinancialTransitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingFinancialTransitions_BillingCharges_ChargeId",
                        column: x => x.ChargeId,
                        principalTable: "BillingCharges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BillingFinancialTransitions_BillingInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "BillingInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingPaymentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalAttemptId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPaymentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingPaymentAttempts_BillingCharges_ChargeId_AccountId_Su~",
                        columns: x => new { x.ChargeId, x.AccountId, x.SubscriptionId },
                        principalTable: "BillingCharges",
                        principalColumns: new[] { "Id", "AccountId", "SubscriptionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChargeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ExternalPaymentId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingPayments_BillingCharges_ChargeId_AccountId_Subscript~",
                        columns: x => new { x.ChargeId, x.AccountId, x.SubscriptionId },
                        principalTable: "BillingCharges",
                        principalColumns: new[] { "Id", "AccountId", "SubscriptionId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_AccountId_IdempotencyKey",
                table: "BillingCharges",
                columns: new[] { "AccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_AccountId_InvoiceId",
                table: "BillingCharges",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCharges_InvoiceId_AccountId_SubscriptionId",
                table: "BillingCharges",
                columns: new[] { "InvoiceId", "AccountId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingFinancialTransitions_AccountId_EntityType_EntityId_O~",
                table: "BillingFinancialTransitions",
                columns: new[] { "AccountId", "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingFinancialTransitions_ChargeId",
                table: "BillingFinancialTransitions",
                column: "ChargeId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingFinancialTransitions_InvoiceId",
                table: "BillingFinancialTransitions",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingInvoices_AccountId_DueAtUtc",
                table: "BillingInvoices",
                columns: new[] { "AccountId", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingInvoices_AccountId_IdempotencyKey",
                table: "BillingInvoices",
                columns: new[] { "AccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingInvoices_SubscriptionId_AccountId",
                table: "BillingInvoices",
                columns: new[] { "SubscriptionId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAttempts_AccountId_ChargeId",
                table: "BillingPaymentAttempts",
                columns: new[] { "AccountId", "ChargeId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAttempts_ChargeId_AccountId_SubscriptionId",
                table: "BillingPaymentAttempts",
                columns: new[] { "ChargeId", "AccountId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentAttempts_ProviderCode_ExternalAttemptId",
                table: "BillingPaymentAttempts",
                columns: new[] { "ProviderCode", "ExternalAttemptId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingPayments_AccountId_InvoiceId",
                table: "BillingPayments",
                columns: new[] { "AccountId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPayments_ChargeId_AccountId_SubscriptionId",
                table: "BillingPayments",
                columns: new[] { "ChargeId", "AccountId", "SubscriptionId" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingPayments_ProviderCode_ExternalPaymentId",
                table: "BillingPayments",
                columns: new[] { "ProviderCode", "ExternalPaymentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingFinancialTransitions");

            migrationBuilder.DropTable(
                name: "BillingPaymentAttempts");

            migrationBuilder.DropTable(
                name: "BillingPayments");

            migrationBuilder.DropTable(
                name: "BillingCharges");

            migrationBuilder.DropTable(
                name: "BillingInvoices");
        }
    }
}
