using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SecureNotificationDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NotificationReadReceipts",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationReadReceipts", x => new { x.NotificationId, x.UserId });
                    table.ForeignKey(
                        name: "FK_NotificationReadReceipts_Notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "Notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationReadReceipts_TenantId_UserId_ReadAtUtc",
                table: "NotificationReadReceipts",
                columns: new[] { "TenantId", "UserId", "ReadAtUtc" });

            migrationBuilder.Sql("""
                INSERT INTO "Permissions" ("Id", "Code", "Description")
                VALUES ('b3000000-0000-0000-0000-000000000001', 'notifications.manage', 'Criar notificações internas da organização')
                ON CONFLICT ("Code") DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "CustomerAccountRolePermissions" link
                USING "Permissions" permission
                WHERE link."PermissionId" = permission."Id" AND permission."Code" = 'notifications.manage';
                DELETE FROM "RolePermissions" link
                USING "Permissions" permission
                WHERE link."PermissionId" = permission."Id" AND permission."Code" = 'notifications.manage';
                DELETE FROM "Permissions" WHERE "Code" = 'notifications.manage';
                """);
            migrationBuilder.DropTable(
                name: "NotificationReadReceipts");
        }
    }
}
