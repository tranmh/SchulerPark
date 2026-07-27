using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotUniquePerDateTimeSlotIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ParkingSlotId_Date_TimeSlot",
                table: "Bookings",
                columns: new[] { "ParkingSlotId", "Date", "TimeSlot" },
                unique: true,
                filter: "\"ParkingSlotId\" IS NOT NULL AND \"Status\" IN ('Won', 'Confirmed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_ParkingSlotId_Date_TimeSlot",
                table: "Bookings");
        }
    }
}
