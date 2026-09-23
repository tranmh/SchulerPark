using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 20 WP1: who cancelled a booking, why and when (admin cancels, capacity
    /// removal, account disable/deletion). Hand-written (no dotnet-ef on the build host);
    /// the model snapshot was updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260923090000_AddBookingCancellationAudit")]
    public partial class AddBookingCancellationAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByUserId",
                table: "Bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Bookings",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "Bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_CancelledByUserId",
                table: "Bookings",
                column: "CancelledByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_Users_CancelledByUserId",
                table: "Bookings",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_Users_CancelledByUserId",
                table: "Bookings");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_CancelledByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledByUserId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "Bookings");
        }
    }
}
