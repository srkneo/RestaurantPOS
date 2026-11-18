using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddKitchenStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StationId",
                table: "OrderItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StationName",
                table: "OrderItems",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StationId",
                table: "MenuItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KitchenStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    OutletId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KitchenStations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KitchenStations_TenantId_OutletId_SortOrder",
                table: "KitchenStations",
                columns: new[] { "TenantId", "OutletId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KitchenStations");

            migrationBuilder.DropColumn(
                name: "StationId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "StationName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "StationId",
                table: "MenuItems");
        }
    }
}
