using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddShopLicences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShopLicences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Customer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Plan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PaidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShopLicences", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShopLicences_Key",
                table: "ShopLicences",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShopLicences");
        }
    }
}
