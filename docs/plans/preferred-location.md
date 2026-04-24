# Plan: User Preferred Parking Location

## Context

Users currently pick a location manually every time they book a parking slot. We want them to set a **preferred parking location** on their profile so bookings go there automatically. If the preferred location is unavailable on a given date (inactive, location-wide blocked, or all slots blocked), the system falls back to a **simple lottery over other available active locations** — we drop the "nearest" concept per user's decision. Applies to both single-day and week bookings. When fallback happens, the user is notified which location was used instead and why.

## Scope

- Persist `PreferredLocationId` on `User`.
- Expose it via profile GET/PUT.
- Make `LocationId` optional in booking requests. If omitted, resolve from the user's preferred location with fallback.
- Surface fallback info in booking responses so the UI can tell the user.
- Update profile and booking pages accordingly.

## Backend Changes

### 1. Entity — `backend/SchulerPark.Core/Entities/User.cs`
Add:
```csharp
public Guid? PreferredLocationId { get; set; }
public Location? PreferredLocation { get; set; } // navigation
```

### 2. EF Configuration — `backend/SchulerPark.Infrastructure/Data/Configurations/UserConfiguration.cs`
Add FK: `User → Location` via `PreferredLocationId`, `OnDelete(DeleteBehavior.SetNull)`. No index needed (not queried by this column in hot paths).

### 3. Migration
Create `AddUserPreferredLocation` migration via `dotnet ef migrations add`. Adds a nullable `PreferredLocationId uuid` column, an FK to `Locations(Id) ON DELETE SET NULL`, and regenerates the model snapshot.

### 4. UserDto — `backend/SchulerPark.Api/DTOs/Auth/UserDto.cs`
Append `Guid? PreferredLocationId`:
```csharp
public record UserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate,
    string Role, bool HasAzureAd, Guid? PreferredLocationId);
```
Update both `ProfileController.GetProfile/UpdateProfile` and `AuthController.ToUserDto`.

### 5. UpdateProfileRequest — `backend/SchulerPark.Api/DTOs/Profile/UpdateProfileRequest.cs`
```csharp
public record UpdateProfileRequest(string DisplayName, string? CarLicensePlate, Guid? PreferredLocationId);
```

### 6. ProfileController — `backend/SchulerPark.Api/Controllers/ProfileController.cs`
In `UpdateProfile`:
- If `request.PreferredLocationId` is non-null, validate that a matching active Location exists; otherwise return 400 `ValidationException`.
- Persist to `user.PreferredLocationId`.
- Include it in the returned `UserDto`.

### 7. Booking request DTOs
Make `LocationId` nullable:
- `backend/SchulerPark.Api/DTOs/Booking/CreateBookingRequest.cs` → `public record CreateBookingRequest(Guid? LocationId, DateOnly Date, string TimeSlot);`
- `backend/SchulerPark.Api/DTOs/Booking/CreateWeekBookingRequest.cs` → same change

### 8. BookingDto — `backend/SchulerPark.Api/DTOs/Booking/BookingDto.cs`
Append `string? FallbackReason`. Populated only on create; `null` elsewhere. UI uses it to decide whether to show a fallback banner.

### 9. Interface — `backend/SchulerPark.Core/Interfaces/IBookingService.cs`
Change signatures to accept nullable `Guid? locationId` and return fallback info:
```csharp
Task<(Booking Booking, string? FallbackReason)> CreateBookingAsync(
    Guid userId, Guid? locationId, DateOnly date, TimeSlot timeSlot);

Task<(List<(Booking Booking, string? FallbackReason)> Created,
      List<(DateOnly Date, string Reason)> Skipped)>
    CreateWeekBookingAsync(Guid userId, Guid? locationId, DateOnly weekStartDate, TimeSlot timeSlot);
```

### 10. BookingService — `backend/SchulerPark.Infrastructure/Services/BookingService.cs`

Add a private resolver:
```csharp
private async Task<(Guid LocationId, string? FallbackReason)> ResolveLocationAsync(
    Guid userId, Guid? requestedLocationId, DateOnly date)
```
Behavior:
- If `requestedLocationId` is non-null → return `(requestedLocationId, null)`. Explicit client choice = no fallback.
- Else load user's `PreferredLocationId`. If null → throw `ValidationException("No location specified and no preferred location set. Please choose a location or set a preferred one in your profile.")`.
- Check preferred location validity for `date`:
  1. Location exists AND `IsActive`.
  2. No location-wide `BlockedDay` (where `ParkingSlotId is null`) for that date.
  3. Not ALL active slots blocked for that date (same check as current inline validation).
- If preferred is valid → `(preferredId, null)`.
- Else: pick a random fallback. Query all active locations with ≥1 active slot, not location-blocked on `date`, and not fully slot-blocked on `date`; exclude preferred. If none → throw `ValidationException("Your preferred location is unavailable on {date} and no alternative locations are available.")`.
- Return `(fallbackLocationId, "Your preferred location '{preferredName}' is unavailable on {date} ({reason}). Booked at '{fallbackName}' instead.")`.

Use this resolver in both `CreateBookingAsync` and each day of `CreateWeekBookingAsync`. Day-level fallback: a week booking may end up spanning multiple locations when preferred is blocked on some days.

Keep existing validations (duplicate check, date range, etc.) running against the **resolved** location.

### 11. BookingController — `backend/SchulerPark.Api/Controllers/BookingController.cs`
- `Create`: pass `request.LocationId` (now nullable) to service; map returned `(Booking, FallbackReason)` to `BookingDto` with `FallbackReason`.
- `CreateWeek`: pass `request.LocationId` (nullable); map `Created` list preserving `FallbackReason` per item.
- `ToBookingDto` helper: add optional `string? fallbackReason` parameter.

### 12. Tests — `backend/SchulerPark.Tests/Integration/BookingTests.cs`
Update the local `UserDto` record so JSON deserialization keeps working after the field is added.

## Frontend Changes

### 13. Types
- `frontend/src/types/auth.ts` — add `preferredLocationId: string | null` to `User`.
- `frontend/src/types/profile.ts` — add `preferredLocationId: string | null` to `UpdateProfileRequest`.
- `frontend/src/types/booking.ts`:
  - `CreateBookingRequest.locationId: string | null`
  - `CreateWeekBookingRequest.locationId: string | null`
  - `Booking.fallbackReason: string | null`

### 14. ProfilePage — `frontend/src/pages/Profile/ProfilePage.tsx`
- Fetch active locations on mount via `locationService.getLocations()`.
- Add a `<select>` labeled "Preferred Parking Location" with options `[None, …locations]`, wired to a new `preferredLocationId` state seeded from `user.preferredLocationId`.
- Include it in the `profileService.updateProfile` payload.

### 15. BookingPage — `frontend/src/pages/Booking/BookingPage.tsx`
- On mount, when `preselectedLocation` URL param is absent but `user.preferredLocationId` is set AND that location is still active in the loaded list, preselect it and start at step 2.
- After a successful `create` or `createWeek`:
  - Single booking: if the response's booking has `fallbackReason`, show a summary screen with the reason + "Go to My Bookings" button (reuse the existing `weekResult` summary pattern) instead of immediately navigating. No fallback → keep current direct navigation.
  - Week booking: extend the existing summary so it also renders per-day `fallbackReason` entries. Show whenever any created booking has a `fallbackReason` OR any day was skipped.

## Verification

1. **Build**:
   - `cd backend && dotnet build`
   - `cd frontend && npm run build`
2. **Migration applies cleanly** against a fresh dev DB: `docker compose up --build`, check `\d "Users"` for the new column + FK.
3. **Backend tests**: `cd backend && dotnet test`
4. **Manual E2E** (dev stack):
   - Register a user, go to Profile, pick a preferred location, save. Re-fetch → preference persisted.
   - Create a booking without selecting a location → booking lands at preferred location.
   - Admin blocks the preferred location for a future date. User books that date (no location chosen) → booking lands at a different location; UI shows fallback banner.
   - Admin blocks ALL locations for a date → booking attempt returns a clear error.
   - Week booking where preferred is blocked on Wed only → summary shows Wed used fallback, others used preferred.
   - User with no preference set and no `locationId` in request → 400 with clear message.
