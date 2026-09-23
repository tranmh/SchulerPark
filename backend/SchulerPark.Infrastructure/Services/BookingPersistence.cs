namespace SchulerPark.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchulerPark.Infrastructure.Data;

/// <summary>
/// Shared save-with-retry for code paths that assign a physical slot to a booking
/// (direct assignment, waitlist promotion, capacity reassignment). The filtered unique
/// index <see cref="SlotUniqueIndex"/> is the DB backstop against two writers grabbing
/// the same slot; when it fires, the caller re-places against the now-committed state
/// and the save is retried.
/// </summary>
public static class BookingPersistence
{
    public const string SlotUniqueIndex = "IX_Bookings_ParkingSlotId_Date_TimeSlot";
    public const int DefaultMaxAttempts = 3;

    /// <summary>True when the exception is a PostgreSQL unique violation on the slot index.</summary>
    public static bool IsSlotConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg
        && pg.ConstraintName == SlotUniqueIndex;

    /// <summary>
    /// Saves pending changes. On a slot conflict, <paramref name="onSlotConflict"/> is
    /// invoked to re-place the colliding booking(s); it returns false to give up (the
    /// exception is then rethrown) or true to retry the save. At most
    /// <paramref name="maxAttempts"/> retries.
    /// </summary>
    public static async Task SaveWithSlotConflictRetryAsync(
        AppDbContext db, Func<Task<bool>> onSlotConflict, int maxAttempts = DefaultMaxAttempts)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await db.SaveChangesAsync();
                return;
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts && IsSlotConflict(ex))
            {
                if (!await onSlotConflict())
                    throw;
            }
        }
    }
}
