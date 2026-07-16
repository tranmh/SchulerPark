using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class BlockedDayUnique_SlotRestrict_EmailFilter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_ParkingSlots_ParkingSlotId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_BlockedDays_LocationId_Date",
                table: "BlockedDays");

            migrationBuilder.DropIndex(
                name: "IX_BlockedDays_ParkingSlotId",
                table: "BlockedDays");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"DeletedAt\" is null");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDays_LocationId_Date",
                table: "BlockedDays",
                columns: new[] { "LocationId", "Date" },
                unique: true,
                filter: "\"ParkingSlotId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDays_ParkingSlotId_Date",
                table: "BlockedDays",
                columns: new[] { "ParkingSlotId", "Date" },
                unique: true,
                filter: "\"ParkingSlotId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_ParkingSlots_ParkingSlotId",
                table: "Bookings",
                column: "ParkingSlotId",
                principalTable: "ParkingSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_ParkingSlots_ParkingSlotId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_BlockedDays_LocationId_Date",
                table: "BlockedDays");

            migrationBuilder.DropIndex(
                name: "IX_BlockedDays_ParkingSlotId_Date",
                table: "BlockedDays");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDays_LocationId_Date",
                table: "BlockedDays",
                columns: new[] { "LocationId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedDays_ParkingSlotId",
                table: "BlockedDays",
                column: "ParkingSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_ParkingSlots_ParkingSlotId",
                table: "Bookings",
                column: "ParkingSlotId",
                principalTable: "ParkingSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
