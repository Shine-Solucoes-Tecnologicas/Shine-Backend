using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingCommercialContracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCommercialContracts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BillingCommercialContractRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCommercialContractRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingCommercialContractRevisions_BillingCommercialContrac~",
                        column: x => x.ContractId,
                        principalTable: "BillingCommercialContracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BillingCommercialTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Code = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ValueType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    ValidFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VisibleToCustomer = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingCommercialTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingCommercialTerms_BillingCommercialContractRevisions_R~",
                        column: x => x.RevisionId,
                        principalTable: "BillingCommercialContractRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingCommercialContractRevisions_ContractId_RevisionNumber",
                table: "BillingCommercialContractRevisions",
                columns: new[] { "ContractId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCommercialContracts_AccountId_Reference",
                table: "BillingCommercialContracts",
                columns: new[] { "AccountId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingCommercialContracts_SubscriptionId",
                table: "BillingCommercialContracts",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingCommercialTerms_RevisionId_Category_Code",
                table: "BillingCommercialTerms",
                columns: new[] { "RevisionId", "Category", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingCommercialTerms");

            migrationBuilder.DropTable(
                name: "BillingCommercialContractRevisions");

            migrationBuilder.DropTable(
                name: "BillingCommercialContracts");
        }
    }
}
