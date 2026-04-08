# Phase 6: Confirmation & Expiry

## Context
After the lottery assigns Won status to bookings, users must confirm actual usage before a deadline. Morning slots must be confirmed by 07:00 AM, Afternoon slots by 14:00 (Europe/Berlin). Unconfirmed Won bookings are marked Expired by an hourly Hangfire job. This prevents slot hoarding and ensures accurate usage data.

---

## Key Design Decision: No New DB Column

The confirmation deadline is deterministic from existing fields (`Date` + `TimeSlot`).
Deadline = 1 hour before slot start:
- Morning (starts 07:00) → deadline at **06:00** Europe/Berlin
- Afternoon (starts 14:00) → deadline at **13:00** Europe/Berlin

No `DeadlineAt` column or migration needed. Compute it in DTOs and the expiry job.

---

## Implementation Plan

### Step 1: Add Deadline Helper

**New:** `Core/Helpers/DeadlineHelper.cs`
```csharp
public static class DeadlineHelper
{
    private static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    public static DateTime GetConfirmationDeadline(DateOnly date, TimeSlot timeSlot)
    {
        // 1 hour before slot start: Morning 06:00, Afternoon 13:00
        var hour = timeSlot == TimeSlot.Morning ? 6 : 13;
        var berlinTime = new DateTime(date.Year, date.Month, date.Day, hour, 0, 0);
        return TimeZoneInfo.ConvertTimeToUtc(berlinTime, BerlinTz);
    }
}
```

Used by: ConfirmationExpiryJob, BookingService (optional deadline validation), BookingDto mapping.

### Step 2: Add `confirmationDeadline` to BookingDto

**Modify:** `Api/DTOs/Booking/BookingDto.cs`
- Add `DateTime? ConfirmationDeadline` field (null for non-Won bookings)

**Modify:** `Api/Controllers/BookingController.cs`
- Update `ToBookingDto()` mapping to compute deadline for Won bookings using `DeadlineHelper`

### Step 3: Add Deadline Validation to ConfirmBookingAsync

**Modify:** `Infrastructure/Services/BookingService.cs`
- In `ConfirmBookingAsync`: after status check, verify deadline hasn't passed
- If past deadline, throw `ValidationException("Confirmation deadline has passed.")`

### Step 4: Create ConfirmationExpiryJob

**New:** `Infrastructure/Jobs/ConfirmationExpiryJob.cs`

```
ExecuteAsync():
1. Get current UTC time
2. Find all bookings where Status == Won
3. For each, compute deadline via DeadlineHelper
4. If deadline has passed → set Status = Expired
5. SaveChanges
6. Log count of expired bookings
```

Expires **all** overdue Won bookings regardless of age (cleans up stale data).

Optimized approach: instead of loading all Won bookings, compute the cutoff times for Morning/Afternoon and query directly:
- All Won bookings with Date < today → expired (past both slots)
- Won Morning bookings with Date == today and current Berlin time >= 06:00 → expired
- Won Afternoon bookings with Date == today and current Berlin time >= 13:00 → expired

### Step 5: Register ConfirmationExpiryJob in Program.cs

**Modify:** `Api/Program.cs`
- Add recurring job: runs every hour at minute 0
- `"0 * * * *"` with Europe/Berlin timezone

### Step 6: Frontend — Add Deadline to Booking Type

**Modify:** `frontend/src/types/booking.ts`
- Add `confirmationDeadline: string | null` to `Booking` interface

### Step 7: Frontend — Deadline Countdown on Won Bookings

**Modify:** `frontend/src/pages/MyBookings/MyBookingsPage.tsx`
- For Won bookings, show deadline countdown below the confirm button
- Format: "Confirm by {time}" or "X hours Y minutes remaining"
- If deadline passed: show "Deadline passed" in red (button still works until backend rejects)

**Modify:** `frontend/src/pages/Dashboard/DashboardPage.tsx`
- Show deadline info on Won bookings in the upcoming bookings section

---

## Decisions

- **Deadline timing**: 1 hour before slot start (Morning 06:00, Afternoon 13:00 Berlin)
- **Past-day expiry**: Yes — expire all overdue Won bookings regardless of age
- **No new DB column**: Deadline computed from Date + TimeSlot, no migration needed
- **Save plan**: docs/plans/Phase6.md

---

## Files Summary

| Action | File |
|--------|------|
| NEW | `Core/Helpers/DeadlineHelper.cs` |
| NEW | `Infrastructure/Jobs/ConfirmationExpiryJob.cs` |
| MODIFY | `Api/DTOs/Booking/BookingDto.cs` — add ConfirmationDeadline |
| MODIFY | `Api/Controllers/BookingController.cs` — compute deadline in DTO mapping |
| MODIFY | `Infrastructure/Services/BookingService.cs` — deadline validation in confirm |
| MODIFY | `Api/Program.cs` — register hourly expiry job |
| MODIFY | `frontend/src/types/booking.ts` — add confirmationDeadline field |
| MODIFY | `frontend/src/pages/MyBookings/MyBookingsPage.tsx` — deadline countdown |
| MODIFY | `frontend/src/pages/Dashboard/DashboardPage.tsx` — deadline info |
| NEW | `docs/plans/Phase6.md` |

---

## Verification

- [ ] `dotnet build` succeeds
- [ ] `npm run build` succeeds
- [ ] Won booking DTO includes `confirmationDeadline` timestamp
- [ ] Confirming Won booking before deadline → 200 success
- [ ] Confirming Won booking after deadline → 400 validation error
- [ ] ConfirmationExpiryJob marks past-deadline Won bookings as Expired
- [ ] Expired bookings show "Expired" badge in frontend
- [ ] Won bookings show countdown timer in My Bookings page
- [ ] Dashboard shows deadline info for upcoming Won bookings
- [ ] Non-Won bookings have null confirmationDeadline
