using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Shine.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConsolidateCustomerAuthorizationAndEntitlements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "TenantEntitlementOverrides",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAtUtc",
                table: "TenantEntitlementOverrides",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "TenantEntitlementOverrides",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "TenantEntitlementOverrides",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                table: "PlanEntitlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartsAtUtc",
                table: "PlanEntitlements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "State",
                table: "PlanEntitlements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "PlanEntitlements",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "TenantEntitlementOverrides");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "TenantEntitlementOverrides");

            migrationBuilder.DropColumn(
                name: "State",
                table: "TenantEntitlementOverrides");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "TenantEntitlementOverrides");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                table: "PlanEntitlements");

            migrationBuilder.DropColumn(
                name: "StartsAtUtc",
                table: "PlanEntitlements");

            migrationBuilder.DropColumn(
                name: "State",
                table: "PlanEntitlements");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "PlanEntitlements");
        }
    }
}
