using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduling.Infrastructure.Migrations;

public partial class AddOutboxIdempotencyKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "IdempotencyKey", table: "OutboxMessages", type: "character varying(300)", maxLength: 300, nullable: true);
        migrationBuilder.CreateIndex(name: "IX_OutboxMessages_IdempotencyKey", table: "OutboxMessages", column: "IdempotencyKey", unique: true, filter: "\"IdempotencyKey\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_OutboxMessages_IdempotencyKey", table: "OutboxMessages");
        migrationBuilder.DropColumn(name: "IdempotencyKey", table: "OutboxMessages");
    }
}
