namespace SchulerPark.Tests.Integration;

using Microsoft.EntityFrameworkCore;
using SchulerPark.Core.Entities;
using SchulerPark.Core.Enums;
using Xunit;

// DB-integrity tests for bugs #9, #18, #19. These exercise real PostgreSQL
// constraints (filtered unique indexes, FK Restrict) that EF InMemory ignores,
// so they run only against the Testcontainers fixture. Skipped (not failed)
// when Docker is unavailable.
[Collection("Postgres")]
[Trait("Category", "Integration")]
public class DbIntegrityTests
{
    private readonly PostgresFixture _fx;
    public DbIntegrityTests(PostgresFixture fx) => _fx = fx;

    // ---- Bug #9: re-registering the email of a soft-deleted user must succeed ----
    [SkippableFact]
    public async Task Reusing_email_of_soft_deleted_user_succeeds()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        await using var db = _fx.NewContext();

        var email = $"reuse-{Guid.NewGuid():N}@schuler.local";
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            DisplayName = "Old Account",
            DeletedAt = DateTime.UtcNow,     // soft-deleted (DSGVO deletion)
        });
        await db.SaveChangesAsync();

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Email = email,                   // same corporate email, fresh registration
            DisplayName = "New Account",
        });

        // RED today: the unfiltered unique Email index throws a unique violation.
        // GREEN after filtering the index on "DeletedAt IS NULL".
        var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.Null(ex);
    }

    // ---- Bug #18: deleting a slot that still has an active booking must be blocked ----
    [SkippableFact]
    public async Task Deleting_slot_with_active_booking_is_blocked()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var slotId = Guid.NewGuid();

        // Seed in one context, then delete in a fresh one: with no tracked dependent,
        // EF issues a raw DELETE and the DATABASE FK rule governs the outcome (not EF
        // client-side relationship fixup). This is what actually tests #18.
        await using (var seed = _fx.NewContext())
        {
            var location = new Location { Id = Guid.NewGuid(), Name = "GP", Address = "A" };
            var slot = new ParkingSlot { Id = slotId, LocationId = location.Id, SlotNumber = "S1" };
            var user = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@x.de", DisplayName = "U" };
            seed.AddRange(location, slot, user);
            await seed.SaveChangesAsync();

            seed.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                LocationId = location.Id,
                ParkingSlotId = slotId,
                Date = new DateOnly(2026, 8, 1),
                TimeSlot = TimeSlot.Morning,
                Status = BookingStatus.Confirmed,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.NewContext();
        var toDelete = await db.ParkingSlots.FindAsync(slotId);
        db.ParkingSlots.Remove(toDelete!);

        // RED with SetNull: the confirmed booking is silently orphaned (no throw).
        // GREEN with Restrict: the DB blocks the delete.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Bug #42: deleting a location that still has bookings must be blocked ----
    [SkippableFact]
    public async Task Deleting_location_with_booking_is_blocked()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var locationId = Guid.NewGuid();

        // Seed in one context, then delete in a fresh one so EF issues a raw DELETE and the
        // DATABASE FK rule governs the outcome (mirrors the slot test above).
        await using (var seed = _fx.NewContext())
        {
            var location = new Location { Id = locationId, Name = $"L-{locationId:N}", Address = "A" };
            var user = new User { Id = Guid.NewGuid(), Email = $"u-{Guid.NewGuid():N}@x.de", DisplayName = "U" };
            seed.AddRange(location, user);
            await seed.SaveChangesAsync();

            seed.Bookings.Add(new Booking
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                LocationId = locationId,
                Date = new DateOnly(2026, 8, 1),
                TimeSlot = TimeSlot.Morning,
                Status = BookingStatus.Confirmed,
            });
            await seed.SaveChangesAsync();
        }

        await using var db = _fx.NewContext();
        var toDelete = await db.Locations.FindAsync(locationId);
        db.Locations.Remove(toDelete!);

        // RED with Cascade: the delete silently erases the booking (no throw).
        // GREEN with Restrict: the DB blocks the delete.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Bug #19: duplicate whole-location block for the same day must be rejected ----
    [SkippableFact]
    public async Task Duplicate_location_block_is_rejected()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
        await using var db = _fx.NewContext();

        var location = new Location { Id = Guid.NewGuid(), Name = "EF", Address = "A" };
        var admin = new User { Id = Guid.NewGuid(), Email = $"a-{Guid.NewGuid():N}@x.de", DisplayName = "Admin" };
        db.AddRange(location, admin);
        await db.SaveChangesAsync();

        var date = new DateOnly(2026, 12, 24);
        db.BlockedDays.Add(new BlockedDay
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            Date = date,
            ParkingSlotId = null,            // whole-location block
            BlockedByUserId = admin.Id,
        });
        await db.SaveChangesAsync();

        db.BlockedDays.Add(new BlockedDay
        {
            Id = Guid.NewGuid(),
            LocationId = location.Id,
            Date = date,
            ParkingSlotId = null,            // duplicate whole-location block
            BlockedByUserId = admin.Id,
        });

        // RED today: the non-unique (LocationId, Date) index allows the duplicate.
        // GREEN after the filtered unique index on ParkingSlotId IS NULL.
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---- Bug #2: two concurrent updates of one booking must not silently clobber ----
    [SkippableFact]
    public async Task Concurrent_update_of_same_booking_throws_concurrency()
    {
        Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);

        var bookingId = Guid.NewGuid();
        await using (var seed = _fx.NewContext())
        {
            var location = new Location { Id = Guid.NewGuid(), Name = "C", Address = "A" };
            var user = new User { Id = Guid.NewGuid(), Email = $"c-{Guid.NewGuid():N}@x.de", DisplayName = "C" };
            seed.AddRange(location, user);
            await seed.SaveChangesAsync();

            seed.Bookings.Add(new Booking
            {
                Id = bookingId,
                UserId = user.Id,
                LocationId = location.Id,
                Date = new DateOnly(2026, 9, 1),
                TimeSlot = TimeSlot.Morning,
                Status = BookingStatus.Pending,
            });
            await seed.SaveChangesAsync();
        }

        // Two independent contexts read the same row, then both write.
        await using var ctxA = _fx.NewContext();
        await using var ctxB = _fx.NewContext();
        var a = await ctxA.Bookings.FindAsync(bookingId);
        var b = await ctxB.Bookings.FindAsync(bookingId);

        a!.Status = BookingStatus.Confirmed;
        await ctxA.SaveChangesAsync();          // wins; bumps the row's xmin

        b!.Status = BookingStatus.Cancelled;    // b still holds the stale xmin

        // RED without a concurrency token: last-write-wins, no throw.
        // GREEN with UseXminAsConcurrencyToken: the stale update matches 0 rows.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctxB.SaveChangesAsync());
    }
}
