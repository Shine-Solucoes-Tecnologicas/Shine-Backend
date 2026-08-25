using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BusinessCatalog.Infrastructure.Migrations;

[DbContext(typeof(BusinessCatalogDbContext))]
[Migration("20260824210100_RemoveSchedulingPoliciesFromCatalog")]
public partial class RemoveSchedulingPoliciesFromCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "MaxConcurrentAppointments", table: "Professionals");
        migrationBuilder.DropColumn(name: "DurationOverrideMinutes", table: "ProfessionalServices");
        migrationBuilder.DropColumn(name: "DurationAttributeKey", table: "Services");
        migrationBuilder.DropColumn(name: "MinutesPerAttributeUnit", table: "Services");
        migrationBuilder.DropColumn(name: "MinimumDurationMinutes", table: "Services");
        migrationBuilder.DropColumn(name: "MaximumDurationMinutes", table: "Services");
        migrationBuilder.DropColumn(name: "DurationRuleVersion", table: "Services");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "MaxConcurrentAppointments", table: "Professionals", type: "integer", nullable: false, defaultValue: 1);
        migrationBuilder.AddColumn<int>(name: "DurationOverrideMinutes", table: "ProfessionalServices", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "DurationAttributeKey", table: "Services", type: "character varying(120)", maxLength: 120, nullable: true);
        migrationBuilder.AddColumn<int>(name: "MinutesPerAttributeUnit", table: "Services", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "MinimumDurationMinutes", table: "Services", type: "integer", nullable: true);
        migrationBuilder.AddColumn<int>(name: "MaximumDurationMinutes", table: "Services", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "DurationRuleVersion", table: "Services", type: "character varying(80)", maxLength: 80, nullable: true);
        migrationBuilder.Sql("""
            UPDATE "Professionals" p SET "MaxConcurrentAppointments" = s."MaxConcurrentAppointments"
            FROM "ProfessionalSchedulingSettings" s WHERE p."TenantId" = s."TenantId" AND p."Id" = s."ProfessionalId";
            UPDATE "Services" c SET "DurationAttributeKey" = s."DurationAttributeKey", "MinutesPerAttributeUnit" = s."MinutesPerAttributeUnit", "MinimumDurationMinutes" = s."MinimumDurationMinutes", "MaximumDurationMinutes" = s."MaximumDurationMinutes", "DurationRuleVersion" = s."DurationRuleVersion"
            FROM "ServiceSchedulingSettings" s WHERE c."TenantId" = s."TenantId" AND c."Id" = s."ServiceId";
            UPDATE "ProfessionalServices" c SET "DurationOverrideMinutes" = s."DurationOverrideMinutes"
            FROM "ProfessionalServiceSchedulingSettings" s WHERE c."TenantId" = s."TenantId" AND c."ProfessionalId" = s."ProfessionalId" AND c."ServiceId" = s."ServiceId";
            """);
    }
}
