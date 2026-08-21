using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTenantIdentifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UserTenantId",
                table: "UserTenants",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_UserTenants_UserTenantId",
                table: "UserTenants",
                column: "UserTenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserTenants_UserTenantId",
                table: "UserTenants");

            migrationBuilder.DropColumn(
                name: "UserTenantId",
                table: "UserTenants");
        }
    }
}
