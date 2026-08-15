using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerRoleScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllModules",
                table: "CustomerAccountUserRoles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllUnits",
                table: "CustomerAccountUserRoles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CustomerAccountUserRoleModules",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModuleCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountUserRoleModules", x => new { x.AccountId, x.UserId, x.RoleId, x.ModuleCode });
                    table.ForeignKey(
                        name: "FK_CustomerAccountUserRoleModules_CustomerAccountUserRoles_Acc~",
                        columns: x => new { x.AccountId, x.UserId, x.RoleId },
                        principalTable: "CustomerAccountUserRoles",
                        principalColumns: new[] { "AccountId", "UserId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccountUserRoleUnits",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnitId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountUserRoleUnits", x => new { x.AccountId, x.UserId, x.RoleId, x.UnitId });
                    table.ForeignKey(
                        name: "FK_CustomerAccountUserRoleUnits_CustomerAccountUserRoles_Accou~",
                        columns: x => new { x.AccountId, x.UserId, x.RoleId },
                        principalTable: "CustomerAccountUserRoles",
                        principalColumns: new[] { "AccountId", "UserId", "RoleId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerAccountUserRoleUnits_Tenants_UnitId",
                        column: x => x.UnitId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountUserRoleUnits_UnitId",
                table: "CustomerAccountUserRoleUnits",
                column: "UnitId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerAccountUserRoleModules");

            migrationBuilder.DropTable(
                name: "CustomerAccountUserRoleUnits");

            migrationBuilder.DropColumn(
                name: "AllModules",
                table: "CustomerAccountUserRoles");

            migrationBuilder.DropColumn(
                name: "AllUnits",
                table: "CustomerAccountUserRoles");
        }
    }
}
