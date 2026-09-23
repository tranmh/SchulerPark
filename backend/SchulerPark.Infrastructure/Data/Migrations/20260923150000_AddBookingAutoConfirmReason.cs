using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 20 WP4 follow-up: a Confirmed booking records why the system confirmed it when the
    /// owner did not (direct assignment, late waitlist promotion, unconfirmed win kept because
    /// nobody was waiting). Null means the user confirmed it themselves. Existing Confirmed rows
    /// stay null — their origin is unknown. Hand-written (no dotnet-ef on the build host); the
    /// model snapshot was updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260923150000_AddBookingAutoConfirmReason")]
    public partial class AddBookingAutoConfirmReason : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutoConfirmReason",
                table: "Bookings",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoConfirmReason",
                table: "Bookings");
        }
    }
}
