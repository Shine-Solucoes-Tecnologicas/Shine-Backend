using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCatalog.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnforceProfessionalUserLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Professionals_TenantId_UserId",
                table: "Professionals",
                columns: new[] { "TenantId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Professionals_TenantId_UserId",
                table: "Professionals");
        }
    }
}
