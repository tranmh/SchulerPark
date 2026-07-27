using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestrictLocationDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Locations_LocationId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_LotteryHistories_Locations_LocationId",
                table: "LotteryHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Locations_LocationId",
                table: "Bookings",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LotteryHistories_Locations_LocationId",
                table: "LotteryHistories",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Locations_LocationId",
                table: "Bookings");

            migrationBuilder.DropForeignKey(
                name: "FK_LotteryHistories_Locations_LocationId",
                table: "LotteryHistories");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Locations_LocationId",
                table: "Bookings",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LotteryHistories_Locations_LocationId",
                table: "LotteryHistories",
                column: "LocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
