using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScopedSchedulingPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "RolePermissions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Scope",
                table: "CustomerAccountRolePermissions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Id", "Code", "Description")
                VALUES ('5c8ed400-0000-0000-0000-000000000001', 'scheduling.configure',
                        'Configurar políticas, capacidade e parâmetros administrativos da agenda')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "CustomerAccountRoles" ("Id", "AccountId", "Name", "IsSystem")
                SELECT md5(account."Id"::text || ':' || role_name)::uuid,
                       account."Id", role_name, TRUE
                FROM "CustomerAccounts" account
                CROSS JOIN (VALUES
                    ('SchedulingProfessional'),
                    ('SchedulingReception'),
                    ('SchedulingManager')) definition(role_name)
                ON CONFLICT ("AccountId", "Name") DO UPDATE SET "IsSystem" = TRUE;

                WITH grants(role_name, permission_code, scope) AS (VALUES
                    ('SchedulingProfessional', 'scheduling.read', 0),
                    ('SchedulingProfessional', 'scheduling.manage', 0),
                    ('SchedulingReception', 'scheduling.read', 1),
                    ('SchedulingReception', 'scheduling.manage', 1),
                    ('SchedulingManager', 'scheduling.read', 1),
                    ('SchedulingManager', 'scheduling.manage', 1),
                    ('SchedulingManager', 'scheduling.configure', 1)
                )
                INSERT INTO "CustomerAccountRolePermissions" ("RoleId", "PermissionId", "Scope")
                SELECT role."Id", permission."Id", grant_definition.scope
                FROM grants grant_definition
                JOIN "CustomerAccountRoles" role ON role."Name" = grant_definition.role_name
                JOIN "Permissions" permission ON permission."Code" = grant_definition.permission_code
                ON CONFLICT ("RoleId", "PermissionId")
                DO UPDATE SET "Scope" = EXCLUDED."Scope";

                INSERT INTO "RolePermissions" ("RoleId", "PermissionId", "Scope")
                SELECT role."Id", permission."Id", 1
                FROM "Roles" role
                CROSS JOIN "Permissions" permission
                WHERE role."Name" IN ('Owner', 'Administrator')
                  AND permission."Code" = 'scheduling.configure'
                ON CONFLICT ("RoleId", "PermissionId")
                DO UPDATE SET "Scope" = EXCLUDED."Scope";

                INSERT INTO "AuditEntries" (
                    "Id", "EntityType", "EntityId", "Action", "UserId", "TenantId",
                    "OccurredAtUtc", "CorrelationId", "IpAddress", "UserAgent",
                    "OldValuesJson", "NewValuesJson", "IsSystemOperation")
                SELECT md5(account."Id"::text || ':scoped-scheduling-permissions')::uuid,
                       'CustomerAccountAuthorization', account."Id"::text,
                       'SCHEDULING_ROLES_MIGRATED', NULL, NULL, CURRENT_TIMESTAMP,
                       'migration:AddScopedSchedulingPermissions', NULL, NULL, NULL,
                       '{"roles":["SchedulingProfessional","SchedulingReception","SchedulingManager"]}'::jsonb,
                       TRUE
                FROM "CustomerAccounts" account
                ON CONFLICT ("Id") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CustomerAccountUserRoles" assignment
                USING "CustomerAccountRoles" role
                WHERE assignment."RoleId" = role."Id"
                  AND role."Name" IN ('SchedulingProfessional', 'SchedulingReception', 'SchedulingManager');

                DELETE FROM "CustomerAccountRoles"
                WHERE "Name" IN ('SchedulingProfessional', 'SchedulingReception', 'SchedulingManager');

                DELETE FROM "Permissions" WHERE "Code" = 'scheduling.configure';

                DELETE FROM "AuditEntries"
                WHERE "CorrelationId" = 'migration:AddScopedSchedulingPermissions';
                """);

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "RolePermissions");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "CustomerAccountRolePermissions");
        }
    }
}
