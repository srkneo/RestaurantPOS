using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantsAndOutlets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KitchenOrders_RestaurantTables_TableId",
                table: "KitchenOrders");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "OrderItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(10,2)",
                oldPrecision: 10,
                oldScale: 2);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "MenuItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'2', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outlets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'2', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BusinessDayStartHour = table.Column<int>(type: "integer", nullable: false),
                    Settings = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outlets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Outlets_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "Id", "CreatedAt", "IsActive", "Name" },
                values: new object[] { 1, new DateTime(2026, 11, 14, 0, 0, 0, 0, DateTimeKind.Utc), true, "My Restaurant" });

            migrationBuilder.InsertData(
                table: "Outlets",
                columns: new[] { "Id", "BusinessDayStartHour", "CreatedAt", "Name", "Settings", "TenantId", "TimeZone" },
                values: new object[] { 1, 4, new DateTime(2026, 11, 14, 0, 0, 0, 0, DateTimeKind.Utc), "Main", "{}", 1, "Asia/Kolkata" });

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_OutletId",
                table: "RestaurantTables",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_TenantId_OutletId_TableNumber",
                table: "RestaurantTables",
                columns: new[] { "TenantId", "OutletId", "TableNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_TenantId_KitchenOrderId",
                table: "OrderItems",
                columns: new[] { "TenantId", "KitchenOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_OutletId",
                table: "MenuItems",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_MenuItems_TenantId_OutletId",
                table: "MenuItems",
                columns: new[] { "TenantId", "OutletId" });

            migrationBuilder.CreateIndex(
                name: "IX_KitchenOrders_OutletId",
                table: "KitchenOrders",
                column: "OutletId");

            migrationBuilder.CreateIndex(
                name: "IX_KitchenOrders_TenantId_OutletId_CreatedAt",
                table: "KitchenOrders",
                columns: new[] { "TenantId", "OutletId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KitchenOrders_TenantId_OutletId_Status",
                table: "KitchenOrders",
                columns: new[] { "TenantId", "OutletId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Outlets_TenantId",
                table: "Outlets",
                column: "TenantId");

            migrationBuilder.AddForeignKey(
                name: "FK_KitchenOrders_Outlets_OutletId",
                table: "KitchenOrders",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KitchenOrders_RestaurantTables_TableId",
                table: "KitchenOrders",
                column: "TableId",
                principalTable: "RestaurantTables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KitchenOrders_Tenants_TenantId",
                table: "KitchenOrders",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MenuItems_Outlets_OutletId",
                table: "MenuItems",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MenuItems_Tenants_TenantId",
                table: "MenuItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RestaurantTables_Outlets_OutletId",
                table: "RestaurantTables",
                column: "OutletId",
                principalTable: "Outlets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RestaurantTables_Tenants_TenantId",
                table: "RestaurantTables",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_KitchenOrders_Outlets_OutletId",
                table: "KitchenOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_KitchenOrders_RestaurantTables_TableId",
                table: "KitchenOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_KitchenOrders_Tenants_TenantId",
                table: "KitchenOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_MenuItems_Outlets_OutletId",
                table: "MenuItems");

            migrationBuilder.DropForeignKey(
                name: "FK_MenuItems_Tenants_TenantId",
                table: "MenuItems");

            migrationBuilder.DropForeignKey(
                name: "FK_RestaurantTables_Outlets_OutletId",
                table: "RestaurantTables");

            migrationBuilder.DropForeignKey(
                name: "FK_RestaurantTables_Tenants_TenantId",
                table: "RestaurantTables");

            migrationBuilder.DropTable(
                name: "Outlets");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_RestaurantTables_OutletId",
                table: "RestaurantTables");

            migrationBuilder.DropIndex(
                name: "IX_RestaurantTables_TenantId_OutletId_TableNumber",
                table: "RestaurantTables");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_TenantId_KitchenOrderId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_OutletId",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_MenuItems_TenantId_OutletId",
                table: "MenuItems");

            migrationBuilder.DropIndex(
                name: "IX_KitchenOrders_OutletId",
                table: "KitchenOrders");

            migrationBuilder.DropIndex(
                name: "IX_KitchenOrders_TenantId_OutletId_CreatedAt",
                table: "KitchenOrders");

            migrationBuilder.DropIndex(
                name: "IX_KitchenOrders_TenantId_OutletId_Status",
                table: "KitchenOrders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "OrderItems");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitPrice",
                table: "OrderItems",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "MenuItems",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldPrecision: 12,
                oldScale: 2);

            migrationBuilder.AddForeignKey(
                name: "FK_KitchenOrders_RestaurantTables_TableId",
                table: "KitchenOrders",
                column: "TableId",
                principalTable: "RestaurantTables",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
