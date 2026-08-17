using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoverableStoredFileDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeletionAttempts",
                table: "StoredFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletionRequestedAtUtc",
                table: "StoredFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletionStatus",
                table: "StoredFiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastDeletionError",
                table: "StoredFiles",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextDeletionAttemptAtUtc",
                table: "StoredFiles",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StoredFiles_DeletionStatus_NextDeletionAttemptAtUtc",
                table: "StoredFiles",
                columns: new[] { "DeletionStatus", "NextDeletionAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StoredFiles_DeletionStatus_NextDeletionAttemptAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "DeletionAttempts",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "DeletionRequestedAtUtc",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "DeletionStatus",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "LastDeletionError",
                table: "StoredFiles");

            migrationBuilder.DropColumn(
                name: "NextDeletionAttemptAtUtc",
                table: "StoredFiles");
        }
    }
}
