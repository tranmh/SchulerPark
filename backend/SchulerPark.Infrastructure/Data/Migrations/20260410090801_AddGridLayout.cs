using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGridLayout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GridColumn",
                table: "ParkingSlots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GridRow",
                table: "ParkingSlots",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GridColumns",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GridRows",
                table: "Locations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GridCells",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Row = table.Column<int>(type: "integer", nullable: false),
                    Column = table.Column<int>(type: "integer", nullable: false),
                    CellType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GridCells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GridCells_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ParkingSlots_LocationId_GridRow_GridColumn",
                table: "ParkingSlots",
                columns: new[] { "LocationId", "GridRow", "GridColumn" },
                unique: true,
                filter: "\"GridRow\" IS NOT NULL AND \"GridColumn\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GridCells_LocationId_Row_Column",
                table: "GridCells",
                columns: new[] { "LocationId", "Row", "Column" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GridCells");

            migrationBuilder.DropIndex(
                name: "IX_ParkingSlots_LocationId_GridRow_GridColumn",
                table: "ParkingSlots");

            migrationBuilder.DropColumn(
                name: "GridColumn",
                table: "ParkingSlots");

            migrationBuilder.DropColumn(
                name: "GridRow",
                table: "ParkingSlots");

            migrationBuilder.DropColumn(
                name: "GridColumns",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "GridRows",
                table: "Locations");
        }
    }
}
