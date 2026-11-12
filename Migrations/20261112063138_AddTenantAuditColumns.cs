using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAuditColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "RestaurantTables",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "RestaurantTables",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutletId",
                table: "RestaurantTables",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "RestaurantTables",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "RestaurantTables",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "RestaurantTables",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "MenuItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "MenuItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutletId",
                table: "MenuItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "MenuItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "MenuItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "MenuItems",
                type: "text",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "KitchenOrders",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientOrderId",
                table: "KitchenOrders",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "KitchenOrders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutletId",
                table: "KitchenOrders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "KitchenOrders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "KitchenOrders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UpdatedBy",
                table: "KitchenOrders",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_KitchenOrders_TenantId_ClientOrderId",
                table: "KitchenOrders",
                columns: new[] { "TenantId", "ClientOrderId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KitchenOrders_TenantId_ClientOrderId",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "OutletId",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "RestaurantTables");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "OutletId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "MenuItems");

            migrationBuilder.DropColumn(
                name: "ClientOrderId",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "OutletId",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "UpdatedBy",
                table: "KitchenOrders");

            migrationBuilder.AlterColumn<DateTime>(
                name: "CreatedAt",
                table: "KitchenOrders",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");
        }
    }
}
