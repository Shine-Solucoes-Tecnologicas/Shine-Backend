using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCatalog.Infrastructure.Migrations;

/// <summary>
/// Transfers migration ownership without recreating the existing catalog tables.
/// The historical Scheduling migrations remain responsible for creating them on a fresh database.
/// </summary>
public partial class BusinessCatalogOwnershipBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Ownership baseline only. Existing identifiers and records stay untouched.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverting ownership must never delete catalog data.
    }
}
