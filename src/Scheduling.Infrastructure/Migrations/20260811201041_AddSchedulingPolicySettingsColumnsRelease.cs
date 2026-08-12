using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Scheduling.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSchedulingPolicySettingsColumnsRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConflictMode",
                table: "SchedulingSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "DefaultMaxConcurrentAppointments",
                table: "SchedulingSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConflictMode",
                table: "SchedulingSettings");

            migrationBuilder.DropColumn(
                name: "DefaultMaxConcurrentAppointments",
                table: "SchedulingSettings");
        }
    }
}
