using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TaxIdentifier = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
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
                    table.PrimaryKey("PK_Customers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Email",
                table: "Customers",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_NormalizedName",
                table: "Customers",
                columns: new[] { "TenantId", "NormalizedName" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_Phone",
                table: "Customers",
                columns: new[] { "TenantId", "Phone" });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_TaxIdentifier",
                table: "Customers",
                columns: new[] { "TenantId", "TaxIdentifier" },
                unique: true,
                filter: "\"TaxIdentifier\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Id", "Code", "Description")
                VALUES
                    ('6ee4e47f-a57f-4f0f-9d3b-1db92bf1612d', 'customers.read', 'Visualizar clientes da unidade'),
                    ('a7d43ff5-1fc5-42a9-a094-4b2f2406b765', 'customers.manage', 'Criar e gerenciar clientes da unidade')
                ON CONFLICT ("Code") DO NOTHING;

                INSERT INTO "CustomerAccountRolePermissions" ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM "CustomerAccountRoles" role
                JOIN "Permissions" permission ON
                    (role."Name" = 'Viewer' AND permission."Code" = 'customers.read') OR
                    (role."Name" = 'Editor' AND permission."Code" IN ('customers.read', 'customers.manage'))
                ON CONFLICT DO NOTHING;

                INSERT INTO "RolePermissions" ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM "Roles" role
                JOIN "Permissions" permission ON permission."Code" IN ('customers.read', 'customers.manage')
                WHERE role."Name" IN ('Owner', 'Administrator')
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.Sql("""
                DELETE FROM "Permissions"
                WHERE ("Id" = '6ee4e47f-a57f-4f0f-9d3b-1db92bf1612d' AND "Code" = 'customers.read')
                   OR ("Id" = 'a7d43ff5-1fc5-42a9-a094-4b2f2406b765' AND "Code" = 'customers.manage');
                """);
        }
    }
}
