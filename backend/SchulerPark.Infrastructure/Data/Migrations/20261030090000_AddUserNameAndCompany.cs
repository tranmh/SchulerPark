using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 21: first name, surname and company so admins can judge external
    /// registrations. All nullable — existing rows keep only DisplayName. Hand-written
    /// (no dotnet-ef on the build host); the model snapshot was updated to match.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261030090000_AddUserNameAndCompany")]
    public partial class AddUserNameAndCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Company",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // DisplayName is now derived as "First Last" (100 + space + 100).
            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "character varying(201)",
                maxLength: 201,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "Users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(201)",
                oldMaxLength: 201);

            migrationBuilder.DropColumn(name: "FirstName", table: "Users");
            migrationBuilder.DropColumn(name: "LastName", table: "Users");
            migrationBuilder.DropColumn(name: "Company", table: "Users");
        }
    }
}
