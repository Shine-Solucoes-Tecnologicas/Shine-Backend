using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerAccountAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerAccountId",
                table: "Tenants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanEntitlements",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanEntitlements", x => new { x.PlanId, x.Key });
                    table.ForeignKey(
                        name: "FK_PlanEntitlements_Plans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "Plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantEntitlementOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Value = table.Column<long>(type: "bigint", nullable: false),
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
                    table.PrimaryKey("PK_TenantEntitlementOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccountRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerAccountRoles_CustomerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccountUsers",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountUsers", x => new { x.AccountId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CustomerAccountUsers_CustomerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "CustomerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerAccountUsers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccountRolePermissions",
                columns: table => new
                {
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountRolePermissions", x => new { x.RoleId, x.PermissionId });
                    table.ForeignKey(
                        name: "FK_CustomerAccountRolePermissions_CustomerAccountRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "CustomerAccountRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerAccountRolePermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CustomerAccountUserRoles",
                columns: table => new
                {
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerAccountUserRoles", x => new { x.AccountId, x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_CustomerAccountUserRoles_CustomerAccountRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "CustomerAccountRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerAccountUserRoles_CustomerAccountUsers_AccountId_Use~",
                        columns: x => new { x.AccountId, x.UserId },
                        principalTable: "CustomerAccountUsers",
                        principalColumns: new[] { "AccountId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve every existing tenant and its current authorization assignments.
            // A legacy tenant becomes a one-unit customer account; accounts can be merged
            // administratively later without losing memberships or permissions.
            migrationBuilder.Sql("""
                INSERT INTO "CustomerAccounts" ("Id", "Name", "IsActive", "CreatedAtUtc")
                SELECT "Id", "Name", "IsActive", "CreatedAtUtc" FROM "Tenants";

                UPDATE "Tenants" SET "CustomerAccountId" = "Id" WHERE "CustomerAccountId" IS NULL;

                INSERT INTO "CustomerAccountUsers" ("AccountId", "UserId", "IsActive", "CreatedAtUtc")
                SELECT "TenantId", "UserId", "IsActive", "CreatedAtUtc" FROM "UserTenants";

                INSERT INTO "CustomerAccountRoles" ("Id", "AccountId", "Name", "IsSystem")
                SELECT "Id", "TenantId", "Name", "IsSystem" FROM "Roles";

                INSERT INTO "CustomerAccountRolePermissions" ("RoleId", "PermissionId")
                SELECT "RoleId", "PermissionId" FROM "RolePermissions";

                INSERT INTO "CustomerAccountUserRoles" ("AccountId", "UserId", "RoleId")
                SELECT "TenantId", "UserId", "RoleId" FROM "UserTenantRoles";

                INSERT INTO "Permissions" ("Id", "Code", "Description")
                SELECT gen_random_uuid(), source."Code", source."Description"
                FROM (VALUES
                    ('account.read', 'Visualizar a organização'),
                    ('account.manage', 'Gerenciar a organização'),
                    ('account.access.manage', 'Gerenciar usuários, papéis e escopos da organização'),
                    ('billing.read', 'Visualizar cobranças e faturas'),
                    ('billing.manage', 'Gerenciar cobrança da organização'),
                    ('subscriptions.read', 'Visualizar assinaturas e planos contratados'),
                    ('subscriptions.manage', 'Gerenciar assinaturas e planos contratados')
                ) AS source("Code", "Description")
                WHERE NOT EXISTS (SELECT 1 FROM "Permissions" p WHERE p."Code" = source."Code");

                INSERT INTO "CustomerAccountRolePermissions" ("RoleId", "PermissionId")
                SELECT role."Id", permission."Id"
                FROM "CustomerAccountRoles" role
                CROSS JOIN "Permissions" permission
                WHERE role."Name" IN ('Owner', 'Administrator')
                  AND permission."Code" IN ('account.read', 'account.manage', 'account.access.manage', 'billing.read', 'billing.manage', 'subscriptions.read', 'subscriptions.manage')
                  AND NOT EXISTS (
                      SELECT 1 FROM "CustomerAccountRolePermissions" existing
                      WHERE existing."RoleId" = role."Id" AND existing."PermissionId" = permission."Id");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_CustomerAccountId",
                table: "Tenants",
                column: "CustomerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountRolePermissions_PermissionId",
                table: "CustomerAccountRolePermissions",
                column: "PermissionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountRoles_AccountId_Name",
                table: "CustomerAccountRoles",
                columns: new[] { "AccountId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccounts_Name",
                table: "CustomerAccounts",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountUserRoles_RoleId",
                table: "CustomerAccountUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountUserRoles_UserId_RoleId",
                table: "CustomerAccountUserRoles",
                columns: new[] { "UserId", "RoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerAccountUsers_UserId",
                table: "CustomerAccountUsers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantEntitlementOverrides_TenantId_Key",
                table: "TenantEntitlementOverrides",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tenants_CustomerAccounts_CustomerAccountId",
                table: "Tenants",
                column: "CustomerAccountId",
                principalTable: "CustomerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tenants_CustomerAccounts_CustomerAccountId",
                table: "Tenants");

            migrationBuilder.DropTable(
                name: "CustomerAccountRolePermissions");

            migrationBuilder.DropTable(
                name: "CustomerAccountUserRoles");

            migrationBuilder.DropTable(
                name: "PlanEntitlements");

            migrationBuilder.DropTable(
                name: "TenantEntitlementOverrides");

            migrationBuilder.DropTable(
                name: "CustomerAccountRoles");

            migrationBuilder.DropTable(
                name: "CustomerAccountUsers");

            migrationBuilder.DropTable(
                name: "CustomerAccounts");

            migrationBuilder.DropIndex(
                name: "IX_Tenants_CustomerAccountId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CustomerAccountId",
                table: "Tenants");
        }
    }
}
