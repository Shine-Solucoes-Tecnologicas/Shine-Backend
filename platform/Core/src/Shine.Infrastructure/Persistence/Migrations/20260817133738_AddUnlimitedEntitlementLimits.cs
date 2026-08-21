using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Shine.Infrastructure.Persistence;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ShineDbContext))]
[Migration("20260817133738_AddUnlimitedEntitlementLimits")]
public partial class AddUnlimitedEntitlementLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "IsUnlimited", table: "TenantEntitlementOverrides", type: "boolean", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<bool>(name: "IsUnlimited", table: "PlanEntitlements", type: "boolean", nullable: false, defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IsUnlimited", table: "TenantEntitlementOverrides");
        migrationBuilder.DropColumn(name: "IsUnlimited", table: "PlanEntitlements");
    }
}
