using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxIdempotencyAndVariableDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DurationAttributeKey",
                table: "Services",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DurationRuleVersion",
                table: "Services",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaximumDurationMinutes",
                table: "Services",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumDurationMinutes",
                table: "Services",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinutesPerAttributeUnit",
                table: "Services",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "OutboxMessages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_IdempotencyKey",
                table: "OutboxMessages",
                column: "IdempotencyKey",
                unique: true,
                filter: "\"IdempotencyKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_IdempotencyKey",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "DurationAttributeKey",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "DurationRuleVersion",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "MaximumDurationMinutes",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "MinimumDurationMinutes",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "MinutesPerAttributeUnit",
                table: "Services");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "OutboxMessages");
        }
    }
}
