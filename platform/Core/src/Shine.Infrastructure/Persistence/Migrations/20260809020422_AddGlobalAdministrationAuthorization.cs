using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGlobalAdministrationAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalRolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalRolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_GlobalRolePermissions_GlobalRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "GlobalRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GlobalRolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGlobalRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGlobalRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserGlobalRoles_GlobalRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "GlobalRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGlobalRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalRolePermissions_PermissionId",
                table: "GlobalRolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalRoles_Name",
                table: "GlobalRoles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGlobalRoles_RoleId",
                table: "UserGlobalRoles",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalRolePermissions");

            migrationBuilder.DropTable(
                name: "UserGlobalRoles");

            migrationBuilder.DropTable(
                name: "GlobalRoles");
        }
    }
}
