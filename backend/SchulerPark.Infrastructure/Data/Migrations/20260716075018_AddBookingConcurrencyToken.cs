using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchulerPark.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingConcurrencyToken : Migration
    {
        // Bug #2: maps Booking to PostgreSQL's built-in system column `xmin` as an
        // optimistic-concurrency token. `xmin` already exists on every table, so there
        // is NO schema change to apply — the scaffolder's AddColumn/DropColumn would
        // fail ("column xmin already exists"). The concurrency check is enforced by the
        // model (AppDbContext.OnModelCreating) at runtime; this migration is intentionally
        // a no-op and exists only to keep the model snapshot in sync.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
