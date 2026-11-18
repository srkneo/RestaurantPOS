using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "OwnerResetExpires",
                table: "ShopLicences",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerResetHash",
                table: "ShopLicences",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OwnerResetExpires",
                table: "LicenceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerResetHash",
                table: "LicenceRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerResetUsedHash",
                table: "LicenceRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerResetExpires",
                table: "ShopLicences");

            migrationBuilder.DropColumn(
                name: "OwnerResetHash",
                table: "ShopLicences");

            migrationBuilder.DropColumn(
                name: "OwnerResetExpires",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "OwnerResetHash",
                table: "LicenceRecords");

            migrationBuilder.DropColumn(
                name: "OwnerResetUsedHash",
                table: "LicenceRecords");
        }
    }
}
