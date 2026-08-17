using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredFileOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StoredFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StorageId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ReadPermissionCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ManagePermissionCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StoredFiles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_StorageId",
                table: "StoredFiles",
                column: "StorageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_TenantId_OwnerUserId",
                table: "StoredFiles",
                columns: new[] { "TenantId", "OwnerUserId" });

            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Id", "Code", "Description") VALUES
                    ('b2000000-0000-0000-0000-000000000001', 'files.read', 'Visualizar arquivos da organização'),
                    ('b2000000-0000-0000-0000-000000000002', 'files.manage', 'Enviar e excluir arquivos da organização')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "CustomerAccountRolePermissions" ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM "CustomerAccountRoles" role
                CROSS JOIN "Permissions" permission
                WHERE (role."Name" = 'Viewer' AND permission."Code" = 'files.read')
                   OR (role."Name" = 'Editor' AND permission."Code" IN ('files.read', 'files.manage'))
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CustomerAccountRolePermissions" link
                USING "Permissions" permission
                WHERE link."PermissionId" = permission."Id"
                  AND permission."Code" IN ('files.read', 'files.manage');
                DELETE FROM "RolePermissions" link
                USING "Permissions" permission
                WHERE link."PermissionId" = permission."Id"
                  AND permission."Code" IN ('files.read', 'files.manage');
                DELETE FROM "Permissions" WHERE "Code" IN ('files.read', 'files.manage');
                """);
            migrationBuilder.DropTable(
                name: "StoredFiles");
        }
    }
}
