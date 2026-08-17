using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedCommercialAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Id", "Code", "Description") VALUES
                    ('b1000000-0000-0000-0000-000000000001', 'billing.commercial.read', 'Consultar contratos e condições comerciais da plataforma'),
                    ('b1000000-0000-0000-0000-000000000002', 'billing.commercial.manage', 'Gerenciar contratos e condições comerciais da plataforma')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "GlobalRoles" ("Id", "Name") VALUES
                    ('b1000000-0000-0000-0000-000000000003', 'CommercialManager')
                ON CONFLICT ("Name") DO NOTHING;

                INSERT INTO "GlobalRolePermissions" ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM "GlobalRoles" role
                CROSS JOIN "Permissions" permission
                WHERE role."Name" IN ('PlatformAdmin', 'CommercialManager')
                  AND permission."Code" IN ('billing.commercial.read', 'billing.commercial.manage')
                ON CONFLICT DO NOTHING;

                INSERT INTO "GlobalRolePermissions" ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM "GlobalRoles" role
                CROSS JOIN "Permissions" permission
                WHERE role."Name" = 'Auditor'
                  AND permission."Code" = 'billing.commercial.read'
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "GlobalRolePermissions" link
                USING "GlobalRoles" role, "Permissions" permission
                WHERE link."RoleId" = role."Id"
                  AND link."PermissionId" = permission."Id"
                  AND (role."Name" = 'CommercialManager'
                       OR permission."Code" IN ('billing.commercial.read', 'billing.commercial.manage'));

                DELETE FROM "UserGlobalRoles" link
                USING "GlobalRoles" role
                WHERE link."RoleId" = role."Id" AND role."Name" = 'CommercialManager';

                DELETE FROM "GlobalRoles" WHERE "Name" = 'CommercialManager';
                DELETE FROM "Permissions" WHERE "Code" IN ('billing.commercial.read', 'billing.commercial.manage');
                """);
        }
    }
}
