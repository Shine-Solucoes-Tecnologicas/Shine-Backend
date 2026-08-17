using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceCustomerAccountRoleBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAccountUserRoles_CustomerAccountRoles_RoleId",
                table: "CustomerAccountUserRoles");

            migrationBuilder.DropIndex(
                name: "IX_CustomerAccountUserRoles_RoleId",
                table: "CustomerAccountUserRoles");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CustomerAccountRoles_AccountId_Id",
                table: "CustomerAccountRoles",
                columns: new[] { "AccountId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountUserRoles_AccountId_RoleId",
                table: "CustomerAccountUserRoles",
                columns: new[] { "AccountId", "RoleId" });

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAccountUserRoles_CustomerAccountRoles_AccountId_Rol~",
                table: "CustomerAccountUserRoles",
                columns: new[] { "AccountId", "RoleId" },
                principalTable: "CustomerAccountRoles",
                principalColumns: new[] { "AccountId", "Id" },
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerAccountUserRoles_CustomerAccountRoles_AccountId_Rol~",
                table: "CustomerAccountUserRoles");

            migrationBuilder.DropIndex(
                name: "IX_CustomerAccountUserRoles_AccountId_RoleId",
                table: "CustomerAccountUserRoles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CustomerAccountRoles_AccountId_Id",
                table: "CustomerAccountRoles");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountUserRoles_RoleId",
                table: "CustomerAccountUserRoles",
                column: "RoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerAccountUserRoles_CustomerAccountRoles_RoleId",
                table: "CustomerAccountUserRoles",
                column: "RoleId",
                principalTable: "CustomerAccountRoles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
