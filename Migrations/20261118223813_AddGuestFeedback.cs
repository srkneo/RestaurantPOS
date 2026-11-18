using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GuestFeedbacks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BillId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    OutletId = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GuestFeedbacks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GuestFeedbacks_TenantId_BillId",
                table: "GuestFeedbacks",
                columns: new[] { "TenantId", "BillId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GuestFeedbacks_TenantId_OutletId_Id",
                table: "GuestFeedbacks",
                columns: new[] { "TenantId", "OutletId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GuestFeedbacks");
        }
    }
}
