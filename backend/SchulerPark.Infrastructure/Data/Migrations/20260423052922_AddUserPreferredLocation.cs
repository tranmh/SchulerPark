using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPreferredLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PreferredLocationId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_PreferredLocationId",
                table: "Users",
                column: "PreferredLocationId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Locations_PreferredLocationId",
                table: "Users",
                column: "PreferredLocationId",
                principalTable: "Locations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Locations_PreferredLocationId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_PreferredLocationId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PreferredLocationId",
                table: "Users");
        }
    }
}
