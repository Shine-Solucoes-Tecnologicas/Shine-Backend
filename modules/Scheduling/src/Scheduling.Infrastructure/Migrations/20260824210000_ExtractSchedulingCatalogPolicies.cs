using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduling.Infrastructure.Migrations;

[DbContext(typeof(SchedulingDbContext))]
[Migration("20260824210000_ExtractSchedulingCatalogPolicies")]
public partial class ExtractSchedulingCatalogPolicies : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProfessionalSchedulingSettings",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                MaxConcurrentAppointments = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
            },
            constraints: table => table.PrimaryKey("PK_ProfessionalSchedulingSettings", x => new { x.TenantId, x.ProfessionalId }));

        migrationBuilder.CreateTable(
            name: "ServiceSchedulingSettings",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                DurationAttributeKey = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                MinutesPerAttributeUnit = table.Column<int>(type: "integer", nullable: true),
                MinimumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                MaximumDurationMinutes = table.Column<int>(type: "integer", nullable: true),
                DurationRuleVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ServiceSchedulingSettings", x => new { x.TenantId, x.ServiceId }));

        migrationBuilder.CreateTable(
            name: "ProfessionalServiceSchedulingSettings",
            columns: table => new
            {
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ProfessionalId = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                DurationOverrideMinutes = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_ProfessionalServiceSchedulingSettings", x => new { x.TenantId, x.ProfessionalId, x.ServiceId }));

        migrationBuilder.Sql("""
            INSERT INTO "ProfessionalSchedulingSettings" ("TenantId", "ProfessionalId", "MaxConcurrentAppointments")
            SELECT "TenantId", "Id", "MaxConcurrentAppointments" FROM "Professionals"
            ON CONFLICT ("TenantId", "ProfessionalId") DO NOTHING;
            INSERT INTO "ServiceSchedulingSettings" ("TenantId", "ServiceId", "DurationAttributeKey", "MinutesPerAttributeUnit", "MinimumDurationMinutes", "MaximumDurationMinutes", "DurationRuleVersion")
            SELECT "TenantId", "Id", "DurationAttributeKey", "MinutesPerAttributeUnit", "MinimumDurationMinutes", "MaximumDurationMinutes", "DurationRuleVersion" FROM "Services"
            WHERE "DurationAttributeKey" IS NOT NULL
            ON CONFLICT ("TenantId", "ServiceId") DO NOTHING;
            INSERT INTO "ProfessionalServiceSchedulingSettings" ("TenantId", "ProfessionalId", "ServiceId", "DurationOverrideMinutes")
            SELECT "TenantId", "ProfessionalId", "ServiceId", "DurationOverrideMinutes" FROM "ProfessionalServices"
            WHERE "DurationOverrideMinutes" IS NOT NULL
            ON CONFLICT ("TenantId", "ProfessionalId", "ServiceId") DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProfessionalServiceSchedulingSettings");
        migrationBuilder.DropTable(name: "ProfessionalSchedulingSettings");
        migrationBuilder.DropTable(name: "ServiceSchedulingSettings");
    }
}
