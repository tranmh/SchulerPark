using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Phase 20 WP4: <c>Lost</c> becomes terminal ("the day is over, no slot"); bookings still
    /// waiting for a slot are <c>Waitlisted</c>. Data-only: every Lost row whose day has not
    /// passed (Berlin) is moved to Waitlisted so it stays promotable. The status column is a
    /// string, so no schema change is needed. Hand-written (no dotnet-ef on the build host).
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20260923120100_AddWaitlistedStatus")]
    public partial class AddWaitlistedStatus : Migration
    {
        /// <summary>Public so the Postgres integration test can run the exact statement the migration applies.</summary>
        public const string UpSql = """
            UPDATE "Bookings"
            SET "Status" = 'Waitlisted'
            WHERE "Status" = 'Lost' AND "Date" >= (now() AT TIME ZONE 'Europe/Berlin')::date;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UpSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""UPDATE "Bookings" SET "Status" = 'Lost' WHERE "Status" = 'Waitlisted';""");
        }
    }
}
