using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddMenuSyncShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CloudId",
                table: "MenuItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloudId",
                table: "MenuCategories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MenuFromCloud",
                table: "LicenceRecords",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MenuSyncError",
                table: "LicenceRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MenuSyncedUtc",
                table: "LicenceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MenuVersion",
                table: "LicenceRecords",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloudId",
                table: "KitchenStations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloudId",
                table: "ItemVariants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CloudId",
                table: "ItemAddOns",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "MenuCategories");

            migrationBuilder.DropColumn(
                name: "MenuFromCloud",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "MenuSyncError",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "MenuSyncedUtc",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "MenuVersion",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "KitchenStations");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "ItemVariants");

            migrationBuilder.DropColumn(
                name: "CloudId",
                table: "ItemAddOns");
        }
    }
}
