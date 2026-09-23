using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 20 WP4: the confirmation deadline is stored per booking (set when it becomes Won)
    /// and the reminder is tracked, instead of both being derived from a fixed 06:00/13:00 rule.
    /// Existing Won rows are backfilled with the new defaults (07:00 Morning / 13:00 Afternoon
    /// Berlin on the booking day). Hand-written (no dotnet-ef on the build host); the model
    /// snapshot was updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260923120000_AddBookingConfirmationDeadline")]
    public partial class AddBookingConfirmationDeadline : Migration
    {
        /// <summary>
        /// Backfill for Won rows without a deadline. Public so the Postgres integration test
        /// can run the exact statement the migration applies.
        /// </summary>
        public const string BackfillSql = """
            UPDATE "Bookings"
            SET "ConfirmationDeadline" =
                (("Date"::timestamp + CASE WHEN "TimeSlot" = 'Morning' THEN interval '7 hours' ELSE interval '13 hours' END)
                    AT TIME ZONE 'Europe/Berlin')
            WHERE "Status" = 'Won' AND "ConfirmationDeadline" IS NULL;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationDeadline",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_Status_ConfirmationDeadline",
                table: "Bookings",
                columns: new[] { "Status", "ConfirmationDeadline" });

            migrationBuilder.Sql(BackfillSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bookings_Status_ConfirmationDeadline",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ConfirmationDeadline",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Bookings");
        }
    }
}
