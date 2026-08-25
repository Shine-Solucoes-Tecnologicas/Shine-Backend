using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduling.Infrastructure.Migrations;

/// <summary>
/// Removes catalog entities from the Scheduling model without deleting their shared historical tables.
/// </summary>
public partial class TransferBusinessCatalogOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // BusinessCatalog now owns Professionals, Services and ProfessionalServices.
        // The physical tables and all identifiers are intentionally preserved.
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Model ownership rollback is metadata-only and must not mutate catalog data.
    }
}
