using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RestaurantPOS.Migrations
{
    /// <inheritdoc />
    public partial class AddReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CustomerName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    People = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TableId = table.Column<int>(type: "integer", nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    OutletId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_OutletId_Date",
                table: "Reservations",
                columns: new[] { "TenantId", "OutletId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_OutletId_Status_AtUtc",
                table: "Reservations",
                columns: new[] { "TenantId", "OutletId", "Status", "AtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Reservations");
        }
    }
}
