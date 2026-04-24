using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferredSlot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreferredSlotId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PreferredSlotId",
                table: "Users",
                column: "PreferredSlotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_ParkingSlots_PreferredSlotId",
                table: "Users",
                column: "PreferredSlotId",
                principalTable: "ParkingSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_ParkingSlots_PreferredSlotId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PreferredSlotId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredSlotId",
                table: "Users");
        }
    }
}
