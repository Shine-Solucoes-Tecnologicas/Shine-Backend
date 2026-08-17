using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelatedEntitlementReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EntitlementReservations",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EntitlementReservations", x => new { x.TenantId, x.Key, x.OperationId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_EntitlementReservations_TenantId_Key_Status",
                table: "EntitlementReservations",
                columns: new[] { "TenantId", "Key", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EntitlementReservations");
        }
    }
}
