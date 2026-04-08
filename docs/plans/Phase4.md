# Phase 4: Core Booking Flow

## Objective
Implement the location browsing, booking creation, and booking management features. Users can view locations, check availability, create bookings for morning/afternoon time slots, view their bookings, and cancel pending ones. Admins cannot yet manage locations (Phase 8), but all user-facing booking flows are complete.

---

## Architecture Overview

```
Frontend                              Backend
──────────────────────────────────────────────────────
DashboardPage ──→ GET /locations      LocationController
BookingPage   ──→ GET /locations/{id}/slots
              ──→ GET /locations/{id}/blocked-days
              ──→ GET /locations/{id}/availability
              ──→ POST /bookings      BookingController
MyBookingsPage──→ GET /bookings/my
              ──→ DELETE /bookings/{id}
              ──→ POST /bookings/{id}/confirm
```

---

## Key Design Decisions

### No Repository Pattern
The master plan mentions repositories, but for this phase we keep it simple: controllers call services, services use `AppDbContext` directly. EF Core's `DbSet<T>` already acts as a repository. We add repositories only if query logic becomes complex enough to warrant extraction.

### Date Boundary Timezone
All "today" / "future date" checks use **Europe/Berlin** time, not UTC. This ensures a user in Germany at 11 PM can still book for the next day. Use `TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin")` to convert `DateTime.UtcNow` before extracting `DateOnly`.

### Cross-Location Bookings
A user **can** book the same date + time slot at multiple locations. The duplicate check is scoped to (user, date, timeSlot, **location**).

### Booking Validation Rules
1. **Date range:** Only future dates (Europe/Berlin), up to 1 month ahead
2. **No duplicates:** One booking per user per (date + timeSlot + location), excluding Cancelled/Expired status
3. **Blocked days:** Cannot book if the location or all its slots are blocked on that date
4. **Active only:** Location and at least one slot must be active
5. **Cancellation:** Only Pending or Won bookings can be cancelled by the user

### Availability Calculation
`GET /locations/{id}/availability` returns slot counts for a date range:
- Total active slots for the location (minus individually blocked slots for that date)
- Minus location-wide blocked days
- Current number of non-cancelled bookings per (date, timeSlot)
- Returns: available count and total count per (date, timeSlot) pair

### Pagination
`GET /bookings/my` uses simple offset/limit with sensible defaults (page=1, pageSize=20). Return total count for frontend pagination.

### UI Framework: Tailwind CSS
Introduce **Tailwind CSS v4** in this phase. All new components use Tailwind utility classes. Existing Login/Register pages can be migrated to Tailwind in this phase or left for later polish (Phase 10).

### Navigation: Sidebar
Use a **sidebar navigation** layout (not a top navbar). This aligns with the admin sidebar planned for Phase 8 — both user and admin views share the same layout pattern, with the admin section showing additional nav items.

---

## Steps

### Step 1: Booking DTOs

**New directory:** `Api/DTOs/Booking/`

**New file:** `Api/DTOs/Booking/CreateBookingRequest.cs`
```csharp
public record CreateBookingRequest(Guid LocationId, DateOnly Date, string TimeSlot);
```

**New file:** `Api/DTOs/Booking/BookingDto.cs`
```csharp
public record BookingDto(
    Guid Id,
    Guid LocationId,
    string LocationName,
    Guid? ParkingSlotId,
    string? ParkingSlotNumber,
    DateOnly Date,
    string TimeSlot,
    string Status,
    DateTime? ConfirmedAt,
    DateTime CreatedAt);
```

**New file:** `Api/DTOs/Booking/MyBookingsResponse.cs`
```csharp
public record MyBookingsResponse(List<BookingDto> Bookings, int TotalCount, int Page, int PageSize);
```

---

### Step 2: Location DTOs

**New directory:** `Api/DTOs/Location/`

**New file:** `Api/DTOs/Location/LocationDto.cs`
```csharp
public record LocationDto(Guid Id, string Name, string Address, int TotalSlots);
```

**New file:** `Api/DTOs/Location/ParkingSlotDto.cs`
```csharp
public record ParkingSlotDto(Guid Id, string SlotNumber, string? Label, bool IsActive);
```

**New file:** `Api/DTOs/Location/BlockedDayDto.cs`
```csharp
public record BlockedDayDto(Guid Id, DateOnly Date, Guid? ParkingSlotId, string? Reason);
```

**New file:** `Api/DTOs/Location/AvailabilityDto.cs`
```csharp
public record AvailabilityDto(DateOnly Date, string TimeSlot, int AvailableSlots, int TotalSlots, int BookingCount);
```

---

### Step 3: Booking Service Interface

**New file:** `Core/Interfaces/IBookingService.cs`
```csharp
public interface IBookingService
{
    Task<Booking> CreateBookingAsync(Guid userId, Guid locationId, DateOnly date, TimeSlot timeSlot);
    Task<(List<Booking> Bookings, int TotalCount)> GetUserBookingsAsync(
        Guid userId, int page, int pageSize,
        BookingStatus? statusFilter = null, DateOnly? fromDate = null, DateOnly? toDate = null);
    Task CancelBookingAsync(Guid bookingId, Guid userId);
    Task<Booking> ConfirmBookingAsync(Guid bookingId, Guid userId);
}
```

**New file:** `Core/Interfaces/ILocationService.cs`
```csharp
public interface ILocationService
{
    Task<List<Location>> GetActiveLocationsAsync();
    Task<List<ParkingSlot>> GetLocationSlotsAsync(Guid locationId);
    Task<List<BlockedDay>> GetBlockedDaysAsync(Guid locationId, DateOnly from, DateOnly to);
    Task<List<(DateOnly Date, TimeSlot TimeSlot, int Available, int Total, int Booked)>>
        GetAvailabilityAsync(Guid locationId, DateOnly from, DateOnly to);
}
```

---

### Step 4: BookingService Implementation

**New file:** `Infrastructure/Services/BookingService.cs`

Dependencies: `AppDbContext`

**CreateBookingAsync(userId, locationId, date, timeSlot):**
1. Validate date is in the future and within 1 month
2. Validate location exists and is active
3. Validate at least one active slot exists at the location
4. Check for location-wide blocked day on that date
5. Check if all individual slots are blocked on that date (edge case)
6. Check for duplicate booking (same user, date, timeSlot, location — excluding Cancelled/Expired)
7. Create Booking with Status = Pending, save, return

```csharp
private static readonly TimeZoneInfo BerlinTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

public async Task<Booking> CreateBookingAsync(Guid userId, Guid locationId, DateOnly date, TimeSlot timeSlot)
{
    // 1. Date validation (Europe/Berlin timezone)
    var berlinNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BerlinTz);
    var today = DateOnly.FromDateTime(berlinNow);
    if (date <= today)
        throw new ValidationException("Cannot book for today or past dates.");
    if (date > today.AddMonths(1))
        throw new ValidationException("Cannot book more than 1 month in advance.");

    // 2. Location exists and is active
    var location = await _db.Locations
        .Include(l => l.ParkingSlots.Where(s => s.IsActive))
        .FirstOrDefaultAsync(l => l.Id == locationId && l.IsActive)
        ?? throw new NotFoundException("Location not found or inactive.");

    // 3. At least one active slot
    if (location.ParkingSlots.Count == 0)
        throw new ValidationException("No active parking slots at this location.");

    // 4. Location-wide block check
    var isLocationBlocked = await _db.BlockedDays.AnyAsync(b =>
        b.LocationId == locationId && b.Date == date && b.ParkingSlotId == null);
    if (isLocationBlocked)
        throw new ValidationException("This location is blocked on the selected date.");

    // 5. Check if ALL individual slots are blocked
    var activeSlotIds = location.ParkingSlots.Select(s => s.Id).ToList();
    var blockedSlotCount = await _db.BlockedDays.CountAsync(b =>
        b.LocationId == locationId && b.Date == date &&
        b.ParkingSlotId != null && activeSlotIds.Contains(b.ParkingSlotId.Value));
    if (blockedSlotCount >= activeSlotIds.Count)
        throw new ValidationException("All parking slots are blocked on the selected date.");

    // 6. Duplicate check
    var duplicate = await _db.Bookings.AnyAsync(b =>
        b.UserId == userId && b.LocationId == locationId &&
        b.Date == date && b.TimeSlot == timeSlot &&
        b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired);
    if (duplicate)
        throw new ValidationException("You already have a booking for this date, time slot, and location.");

    // 7. Create
    var booking = new Booking
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        LocationId = locationId,
        Date = date,
        TimeSlot = timeSlot,
        Status = BookingStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };
    _db.Bookings.Add(booking);
    await _db.SaveChangesAsync();
    return booking;
}
```

**GetUserBookingsAsync(userId, page, pageSize, statusFilter?, fromDate?, toDate?):**
- Query Bookings where UserId matches
- Apply optional filters (status, date range)
- Include Location and ParkingSlot navigation for DTO mapping
- Order by Date descending, then CreatedAt descending
- Return paged results with total count

**CancelBookingAsync(bookingId, userId):**
- Find booking by ID, verify ownership (userId matches)
- Verify status is Pending or Won (only cancellable states)
- Set Status = Cancelled, save

**ConfirmBookingAsync(bookingId, userId):**
- Find booking by ID, verify ownership
- Verify status is Won (only confirmable state)
- Set Status = Confirmed, ConfirmedAt = DateTime.UtcNow, save

---

### Step 5: LocationService Implementation

**New file:** `Infrastructure/Services/LocationService.cs`

Dependencies: `AppDbContext`

**GetActiveLocationsAsync():**
- Return all locations where IsActive = true
- Include count of active parking slots (for the LocationDto.TotalSlots field)

**GetLocationSlotsAsync(locationId):**
- Return ParkingSlots where LocationId matches and IsActive = true
- Order by SlotNumber

**GetBlockedDaysAsync(locationId, from, to):**
- Return BlockedDays for location within date range
- Include both location-wide (ParkingSlotId == null) and slot-specific blocks

**GetAvailabilityAsync(locationId, from, to):**
- For each date in range, for each TimeSlot:
  - Count active slots at location
  - Subtract location-wide blocked day (if exists, available = 0)
  - Subtract individually blocked slots on that date
  - Count non-cancelled/expired bookings
  - Available = totalSlots - blockedSlots - bookedCount (min 0)
- Optimize: batch query blocked days and bookings for the date range, then compute in memory

```csharp
public async Task<List<(DateOnly Date, TimeSlot TimeSlot, int Available, int Total, int Booked)>>
    GetAvailabilityAsync(Guid locationId, DateOnly from, DateOnly to)
{
    var activeSlotCount = await _db.ParkingSlots
        .CountAsync(s => s.LocationId == locationId && s.IsActive);

    var activeSlotIds = await _db.ParkingSlots
        .Where(s => s.LocationId == locationId && s.IsActive)
        .Select(s => s.Id)
        .ToListAsync();

    // Batch load blocked days in range
    var blockedDays = await _db.BlockedDays
        .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to)
        .ToListAsync();

    // Batch load bookings in range (non-cancelled/expired)
    var bookings = await _db.Bookings
        .Where(b => b.LocationId == locationId && b.Date >= from && b.Date <= to &&
                     b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.Expired)
        .GroupBy(b => new { b.Date, b.TimeSlot })
        .Select(g => new { g.Key.Date, g.Key.TimeSlot, Count = g.Count() })
        .ToListAsync();

    var result = new List<(DateOnly, TimeSlot, int, int, int)>();
    for (var date = from; date <= to; date = date.AddDays(1))
    {
        foreach (var timeSlot in Enum.GetValues<TimeSlot>())
        {
            var isLocationBlocked = blockedDays.Any(b => b.Date == date && b.ParkingSlotId == null);
            if (isLocationBlocked)
            {
                result.Add((date, timeSlot, 0, activeSlotCount, 0));
                continue;
            }

            var blockedSlotIds = blockedDays
                .Where(b => b.Date == date && b.ParkingSlotId != null)
                .Select(b => b.ParkingSlotId!.Value)
                .Where(id => activeSlotIds.Contains(id))
                .Distinct().Count();

            var totalAvailable = activeSlotCount - blockedSlotIds;
            var booked = bookings
                .FirstOrDefault(b => b.Date == date && b.TimeSlot == timeSlot)?.Count ?? 0;

            var available = Math.Max(0, totalAvailable - booked);
            result.Add((date, timeSlot, available, totalAvailable, booked));
        }
    }
    return result;
}
```

---

### Step 6: Custom Exception Types

**New file:** `Core/Exceptions/ValidationException.cs`
```csharp
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
```

**New file:** `Core/Exceptions/NotFoundException.cs`
```csharp
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
}
```

**New file:** `Core/Exceptions/ForbiddenException.cs`
```csharp
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}
```

These are used by services to signal different HTTP status codes. The exception handling middleware (Step 9) maps them to 400, 404, 403 respectively.

---

### Step 7: LocationController

**New file:** `Api/Controllers/LocationController.cs`

```csharp
[ApiController]
[Route("api/locations")]
[Authorize]
public class LocationController : ControllerBase
```

| Method | Route | Auth | Returns |
|--------|-------|------|---------|
| GET | `/api/locations` | `[Authorize]` | `List<LocationDto>` (200) |
| GET | `/api/locations/{id}/slots` | `[Authorize]` | `List<ParkingSlotDto>` (200) |
| GET | `/api/locations/{id}/blocked-days` | `[Authorize]` | `List<BlockedDayDto>` (200) |
| GET | `/api/locations/{id}/availability` | `[Authorize]` | `List<AvailabilityDto>` (200) |

**Query parameters for availability:**
- `from` (DateOnly, optional, default: tomorrow)
- `to` (DateOnly, optional, default: from + 1 month)

**Implementation notes:**
- All endpoints require authentication (users must be logged in)
- Availability and blocked-days accept date range query params
- LocationDto includes `TotalSlots` count for display
- Returns 404 if location not found or inactive

---

### Step 8: BookingController

**New file:** `Api/Controllers/BookingController.cs`

```csharp
[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingController : ControllerBase
```

| Method | Route | Auth | Body / Params | Returns |
|--------|-------|------|---------------|---------|
| POST | `/api/bookings` | `[Authorize]` | `CreateBookingRequest` | `BookingDto` (201) |
| GET | `/api/bookings/my` | `[Authorize]` | query: page, pageSize, status?, from?, to? | `MyBookingsResponse` (200) |
| DELETE | `/api/bookings/{id}` | `[Authorize]` | — | 204 |
| POST | `/api/bookings/{id}/confirm` | `[Authorize]` | — | `BookingDto` (200) |

**UserId extraction** (same pattern as AuthController):
```csharp
private Guid GetUserId() =>
    Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
```

**POST /api/bookings:**
- Parse TimeSlot from string (case-insensitive)
- Call `IBookingService.CreateBookingAsync`
- Eager-load Location for DTO mapping
- Return 201 with BookingDto

**GET /api/bookings/my:**
- Extract filters from query string
- Call `IBookingService.GetUserBookingsAsync`
- Map to BookingDto list with location/slot info
- Return paged response

**DELETE /api/bookings/{id}:**
- Call `IBookingService.CancelBookingAsync(id, GetUserId())`
- Return 204 No Content

**POST /api/bookings/{id}/confirm:**
- Call `IBookingService.ConfirmBookingAsync(id, GetUserId())`
- Return updated BookingDto

---

### Step 9: Exception Handling Middleware

**New file:** `Api/Middleware/ExceptionHandlingMiddleware.cs`

Maps domain exceptions to Problem Details (RFC 9457) responses:

| Exception | HTTP Status | Type |
|-----------|-------------|------|
| `ValidationException` | 400 Bad Request | validation-error |
| `NotFoundException` | 404 Not Found | not-found |
| `ForbiddenException` | 403 Forbidden | forbidden |
| Unhandled | 500 Internal Server Error | internal-error |

```csharp
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = 400;
            await WriteProblemDetails(context, "Bad Request", ex.Message);
        }
        catch (NotFoundException ex)
        {
            context.Response.StatusCode = 404;
            await WriteProblemDetails(context, "Not Found", ex.Message);
        }
        catch (ForbiddenException ex)
        {
            context.Response.StatusCode = 403;
            await WriteProblemDetails(context, "Forbidden", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            context.Response.StatusCode = 500;
            await WriteProblemDetails(context, "Internal Server Error", "An unexpected error occurred.");
        }
    }

    private static async Task WriteProblemDetails(HttpContext context, string title, string detail)
    {
        var problem = new ProblemDetails { Title = title, Detail = detail, Status = context.Response.StatusCode };
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
```

---

### Step 10: Register Services in Program.cs

**Modify:** `Api/Program.cs`

Add after existing auth service registrations:
```csharp
// Phase 4: Booking services
builder.Services.AddScoped<IBookingService, BookingService>();
builder.Services.AddScoped<ILocationService, LocationService>();
```

Add exception middleware early in the pipeline (before auth):
```csharp
app.UseMiddleware<ExceptionHandlingMiddleware>();
// existing: app.UseAuthentication(); app.UseAuthorization();
```

---

### Step 11: Frontend Types

**New file:** `frontend/src/types/booking.ts`

```typescript
export type TimeSlot = 'Morning' | 'Afternoon';

export type BookingStatus = 'Pending' | 'Won' | 'Lost' | 'Confirmed' | 'Cancelled' | 'Expired';

export interface Location {
  id: string;
  name: string;
  address: string;
  totalSlots: number;
}

export interface ParkingSlot {
  id: string;
  slotNumber: string;
  label: string | null;
  isActive: boolean;
}

export interface BlockedDay {
  id: string;
  date: string;            // "YYYY-MM-DD"
  parkingSlotId: string | null;
  reason: string | null;
}

export interface Booking {
  id: string;
  locationId: string;
  locationName: string;
  parkingSlotId: string | null;
  parkingSlotNumber: string | null;
  date: string;            // "YYYY-MM-DD"
  timeSlot: TimeSlot;
  status: BookingStatus;
  confirmedAt: string | null;
  createdAt: string;
}

export interface Availability {
  date: string;            // "YYYY-MM-DD"
  timeSlot: TimeSlot;
  availableSlots: number;
  totalSlots: number;
  bookingCount: number;
}

export interface CreateBookingRequest {
  locationId: string;
  date: string;            // "YYYY-MM-DD"
  timeSlot: TimeSlot;
}

export interface MyBookingsResponse {
  bookings: Booking[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface BookingFilters {
  page?: number;
  pageSize?: number;
  status?: BookingStatus;
  from?: string;
  to?: string;
}
```

---

### Step 12: Location & Booking API Services

**New file:** `frontend/src/services/locationService.ts`

```typescript
import api from './api';
import type { Location, ParkingSlot, BlockedDay, Availability } from '../types/booking';

export const locationService = {
  getLocations: () =>
    api.get<Location[]>('/locations').then(r => r.data),

  getSlots: (locationId: string) =>
    api.get<ParkingSlot[]>(`/locations/${locationId}/slots`).then(r => r.data),

  getBlockedDays: (locationId: string, from?: string, to?: string) =>
    api.get<BlockedDay[]>(`/locations/${locationId}/blocked-days`, {
      params: { from, to }
    }).then(r => r.data),

  getAvailability: (locationId: string, from?: string, to?: string) =>
    api.get<Availability[]>(`/locations/${locationId}/availability`, {
      params: { from, to }
    }).then(r => r.data),
};
```

**New file:** `frontend/src/services/bookingService.ts`

```typescript
import api from './api';
import type { Booking, CreateBookingRequest, MyBookingsResponse, BookingFilters } from '../types/booking';

export const bookingService = {
  create: (data: CreateBookingRequest) =>
    api.post<Booking>('/bookings', data).then(r => r.data),

  getMyBookings: (filters?: BookingFilters) =>
    api.get<MyBookingsResponse>('/bookings/my', { params: filters }).then(r => r.data),

  cancel: (bookingId: string) =>
    api.delete(`/bookings/${bookingId}`),

  confirm: (bookingId: string) =>
    api.post<Booking>(`/bookings/${bookingId}/confirm`).then(r => r.data),
};
```

---

### Step 13: Shared UI Components

**New file:** `frontend/src/components/BookingStatusBadge.tsx`

A styled `<span>` that maps `BookingStatus` to color-coded badges:

| Status | Color | Label |
|--------|-------|-------|
| Pending | Yellow/Amber | Pending |
| Won | Green | Won — Confirm! |
| Lost | Red | Lost |
| Confirmed | Blue | Confirmed |
| Cancelled | Gray | Cancelled |
| Expired | Gray | Expired |

**New file:** `frontend/src/components/TimeSlotSelector.tsx`

Radio button group for Morning / Afternoon selection. Props:
```typescript
interface Props {
  value: TimeSlot | null;
  onChange: (slot: TimeSlot) => void;
  morningAvailable?: number;
  afternoonAvailable?: number;
}
```
Shows available count next to each option. Disables options with 0 availability.

**New file:** `frontend/src/components/LocationSelector.tsx`

Dropdown or card-based selector for choosing a location. Props:
```typescript
interface Props {
  locations: Location[];
  selectedId: string | null;
  onChange: (locationId: string) => void;
}
```
Shows location name, address, and total slot count.

**New file:** `frontend/src/components/CalendarPicker.tsx`

A simple month-view calendar for date selection. Props:
```typescript
interface Props {
  selectedDate: string | null;       // "YYYY-MM-DD"
  onSelect: (date: string) => void;
  blockedDates: Set<string>;         // location-wide blocked dates
  availability?: Map<string, { morning: number; afternoon: number }>;
  minDate?: string;                  // tomorrow
  maxDate?: string;                  // +1 month
}
```
- Days before today: greyed out, not clickable
- Blocked dates: visually distinct (red/strikethrough), not clickable
- Available dates: show a small availability indicator (green/yellow/red dot based on remaining slots)
- Selected date: highlighted

Implementation: Pure CSS grid calendar (no external date-picker library). Keep it simple — 7-column grid, month navigation arrows, render day cells.

**New file:** `frontend/src/components/ConfirmDialog.tsx`

Generic confirmation modal. Props:
```typescript
interface Props {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
  isLoading?: boolean;
}
```

---

### Step 14: Dashboard Page

**New file:** `frontend/src/pages/Dashboard/DashboardPage.tsx`

Replace the inline Dashboard component in App.tsx.

**Layout:**
- Header: "SchulerPark" + user welcome + logout button (move from inline)
- Section 1: **Upcoming Bookings** — next 5 bookings (any status except Cancelled/Expired)
  - Each shows: date, time slot, location, status badge
  - "Won" bookings show prominent "Confirm" button
  - Links to My Bookings for full list
- Section 2: **Quick Book** — card per location showing name, address, slot count
  - Click → navigate to `/booking?location={id}`
- Empty state: "No upcoming bookings. Book a parking spot!" with CTA

**Data fetching:**
- On mount: call `bookingService.getMyBookings({ pageSize: 5, from: todayString })`
- On mount: call `locationService.getLocations()`

---

### Step 15: Booking Page

**New file:** `frontend/src/pages/Booking/BookingPage.tsx`

**Multi-step flow (single page with steps, not separate routes):**

1. **Select Location** — LocationSelector component. Pre-select if `?location={id}` in URL.
2. **Select Date** — CalendarPicker. Load blocked days + availability for selected location.
3. **Select Time Slot** — TimeSlotSelector. Show availability counts for the selected date.
4. **Review & Submit** — Summary of selection + "Book" button.

**State management:**
```typescript
const [step, setStep] = useState(1);
const [locationId, setLocationId] = useState<string | null>(searchParams.get('location'));
const [date, setDate] = useState<string | null>(null);
const [timeSlot, setTimeSlot] = useState<TimeSlot | null>(null);

const [locations, setLocations] = useState<Location[]>([]);
const [blockedDates, setBlockedDates] = useState<Set<string>>(new Set());
const [availability, setAvailability] = useState<Availability[]>([]);
const [isSubmitting, setIsSubmitting] = useState(false);
const [error, setError] = useState<string | null>(null);
```

**Data loading:**
- Step 1: Load locations on mount
- Step 2 (on location select): Load blocked days + availability for next month
- Step 3: Filter availability for selected date

**On submit:**
1. Call `bookingService.create({ locationId, date, timeSlot })`
2. On success: navigate to `/my-bookings` with success message (via URL param or state)
3. On error: display error message (validation errors from backend)

**Navigation:** Back buttons on each step. Steps indicator at top.

---

### Step 16: My Bookings Page

**New file:** `frontend/src/pages/MyBookings/MyBookingsPage.tsx`

**Layout:**
- Header: "My Bookings"
- Filters row: status dropdown (All/Pending/Won/Lost/Confirmed/Cancelled/Expired), date range (optional)
- Bookings list: cards or table rows

**Each booking card shows:**
- Date + time slot
- Location name
- Assigned parking slot (if Won/Confirmed — show slot number)
- Status badge (BookingStatusBadge component)
- Actions:
  - **Pending**: "Cancel" button
  - **Won**: "Confirm" button (prominent), "Cancel" button
  - **Lost/Confirmed/Cancelled/Expired**: no actions (read-only)

**Pagination:** "Load more" button or numbered pages at bottom.

**Cancel flow:** ConfirmDialog → on confirm → `bookingService.cancel(id)` → refresh list
**Confirm flow:** Direct call → `bookingService.confirm(id)` → refresh list (no confirmation dialog needed for confirming, but show error toast if it fails)

---

### Step 17: Install & Configure Tailwind CSS

**Install:**
```bash
cd frontend
npm install tailwindcss @tailwindcss/vite
```

**Modify:** `frontend/vite.config.ts` — add Tailwind plugin:
```typescript
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  // ... existing config
});
```

**Modify:** `frontend/src/index.css` — add Tailwind import at top:
```css
@import "tailwindcss";
```

All new Phase 4 components use Tailwind utility classes.

---

### Step 18: App Layout Component (Sidebar)

**New file:** `frontend/src/components/AppLayout.tsx`

Shared layout wrapper for authenticated pages with **sidebar navigation**:
```typescript
interface Props { children: ReactNode; }
```

**Layout structure (Tailwind):**
```
┌─────────────┬────────────────────────────────────┐
│  Sidebar    │  Main Content                      │
│  (fixed)    │  (scrollable)                      │
│             │                                    │
│  Logo       │  {children}                        │
│  ─────────  │                                    │
│  Dashboard  │                                    │
│  Book       │                                    │
│  My Bookings│                                    │
│             │                                    │
│  ─────────  │                                    │
│  User info  │                                    │
│  Logout     │                                    │
└─────────────┴────────────────────────────────────┘
```

- Sidebar: fixed width (`w-64`), full height, dark background
- Logo/brand at top
- Nav links with active state highlighting (use `useLocation()` from react-router)
- User display name + role badge at bottom
- Logout button
- Main content: `ml-64` offset, padding, scrollable
- Mobile: sidebar collapses to hamburger menu (can defer to Phase 10 polish)

All protected pages wrap in `<AppLayout>`. Login/Register pages do NOT use this layout.

---

### Step 19: Update App.tsx Routes

**Modify:** `frontend/src/App.tsx`

Remove inline Dashboard component. Add routes:

```typescript
<Routes>
  <Route path="/login" element={<LoginPage />} />
  <Route path="/register" element={<RegisterPage />} />
  <Route path="/" element={
    <ProtectedRoute>
      <AppLayout>
        <DashboardPage />
      </AppLayout>
    </ProtectedRoute>
  } />
  <Route path="/booking" element={
    <ProtectedRoute>
      <AppLayout>
        <BookingPage />
      </AppLayout>
    </ProtectedRoute>
  } />
  <Route path="/my-bookings" element={
    <ProtectedRoute>
      <AppLayout>
        <MyBookingsPage />
      </AppLayout>
    </ProtectedRoute>
  } />
</Routes>
```

---

## Files Summary

### New Backend Files (12)
| # | File | Purpose |
|---|------|---------|
| 1 | `Core/Interfaces/IBookingService.cs` | Booking service interface |
| 2 | `Core/Interfaces/ILocationService.cs` | Location service interface |
| 3 | `Core/Exceptions/ValidationException.cs` | 400 Bad Request exception |
| 4 | `Core/Exceptions/NotFoundException.cs` | 404 Not Found exception |
| 5 | `Core/Exceptions/ForbiddenException.cs` | 403 Forbidden exception |
| 6 | `Api/DTOs/Booking/CreateBookingRequest.cs` | Create booking DTO |
| 7 | `Api/DTOs/Booking/BookingDto.cs` | Booking response DTO |
| 8 | `Api/DTOs/Booking/MyBookingsResponse.cs` | Paged bookings response |
| 9 | `Api/DTOs/Location/LocationDto.cs` | Location response DTO |
| 10 | `Api/DTOs/Location/ParkingSlotDto.cs` | Parking slot response DTO |
| 11 | `Api/DTOs/Location/BlockedDayDto.cs` | Blocked day response DTO |
| 12 | `Api/DTOs/Location/AvailabilityDto.cs` | Availability response DTO |

### New Backend Files — Services & Controllers (5)
| # | File | Purpose |
|---|------|---------|
| 13 | `Infrastructure/Services/BookingService.cs` | Booking business logic |
| 14 | `Infrastructure/Services/LocationService.cs` | Location queries |
| 15 | `Api/Controllers/LocationController.cs` | Location endpoints |
| 16 | `Api/Controllers/BookingController.cs` | Booking endpoints |
| 17 | `Api/Middleware/ExceptionHandlingMiddleware.cs` | Domain exception → HTTP mapping |

### Modified Backend Files (1)
| File | Change |
|------|--------|
| `Api/Program.cs` | Register IBookingService, ILocationService, exception middleware |

### New Frontend Files (12)
| # | File | Purpose |
|---|------|---------|
| 1 | `src/types/booking.ts` | Booking/Location TypeScript types |
| 2 | `src/services/locationService.ts` | Location API calls |
| 3 | `src/services/bookingService.ts` | Booking API calls |
| 4 | `src/components/BookingStatusBadge.tsx` | Status badge component (Tailwind) |
| 5 | `src/components/TimeSlotSelector.tsx` | Morning/Afternoon selector (Tailwind) |
| 6 | `src/components/LocationSelector.tsx` | Location dropdown/cards (Tailwind) |
| 7 | `src/components/CalendarPicker.tsx` | CSS-grid date picker with availability |
| 8 | `src/components/ConfirmDialog.tsx` | Generic confirmation modal (Tailwind) |
| 9 | `src/components/AppLayout.tsx` | Sidebar navigation + layout (Tailwind) |
| 10 | `src/pages/Dashboard/DashboardPage.tsx` | Dashboard page |
| 11 | `src/pages/Booking/BookingPage.tsx` | Multi-step booking flow |
| 12 | `src/pages/MyBookings/MyBookingsPage.tsx` | User bookings list |

### Modified Frontend Files (4)
| File | Change |
|------|--------|
| `src/App.tsx` | Replace inline dashboard, add booking/my-bookings routes |
| `src/index.css` | Add `@import "tailwindcss"` |
| `vite.config.ts` | Add Tailwind CSS plugin |
| `package.json` | Add `tailwindcss`, `@tailwindcss/vite` dependencies |

---

## Implementation Order

1. Exception types (Step 6) — used by everything
2. DTOs (Steps 1-2) — needed by controllers
3. Service interfaces (Step 3) — needed by implementations and controllers
4. BookingService + LocationService (Steps 4-5)
5. ExceptionHandlingMiddleware (Step 9)
6. LocationController + BookingController (Steps 7-8)
7. Program.cs registration (Step 10)
8. **Backend verification with curl/Swagger**
9. Install & configure Tailwind CSS (Step 17)
10. Frontend types (Step 11)
11. API services (Step 12)
12. Shared components (Step 13)
13. AppLayout with sidebar (Step 18)
14. DashboardPage (Step 14)
15. BookingPage (Step 15)
16. MyBookingsPage (Step 16)
17. Update App.tsx routes (Step 19)
18. **Full end-to-end verification**

---

## Verification Checklist

### Backend
- [ ] `dotnet build` succeeds
- [ ] `GET /api/locations` → 200, returns 4 seeded locations with slot counts
- [ ] `GET /api/locations/{id}/slots` → 200, returns active slots
- [ ] `GET /api/locations/{id}/blocked-days` → 200 (empty initially)
- [ ] `GET /api/locations/{id}/availability` → 200, returns availability per date/slot
- [ ] `POST /api/bookings` (valid) → 201, booking created with Pending status
- [ ] `POST /api/bookings` (past date) → 400, validation error
- [ ] `POST /api/bookings` (>1 month ahead) → 400, validation error
- [ ] `POST /api/bookings` (duplicate) → 400, validation error
- [ ] `POST /api/bookings` (inactive location) → 404
- [ ] `GET /api/bookings/my` → 200, returns user's bookings
- [ ] `GET /api/bookings/my?status=Pending` → 200, filtered results
- [ ] `DELETE /api/bookings/{id}` (own Pending) → 204
- [ ] `DELETE /api/bookings/{id}` (other user's) → 403
- [ ] `DELETE /api/bookings/{id}` (Confirmed) → 400 (not cancellable)
- [ ] `POST /api/bookings/{id}/confirm` (Won) → 200, status = Confirmed
- [ ] `POST /api/bookings/{id}/confirm` (Pending) → 400 (not confirmable yet)
- [ ] All endpoints return 401 without JWT
- [ ] Exception middleware returns ProblemDetails format

### Frontend
- [ ] `npm run build` succeeds
- [ ] Dashboard loads with location cards and upcoming bookings
- [ ] Clicking location card navigates to booking page with location pre-selected
- [ ] Booking flow: select location → calendar shows → select date → select time slot → submit
- [ ] Blocked dates are greyed out in calendar
- [ ] Availability counts update per date/time slot
- [ ] Successful booking redirects to My Bookings
- [ ] My Bookings shows all user's bookings with correct status badges
- [ ] Cancel button works on Pending/Won bookings
- [ ] Confirm button works on Won bookings
- [ ] Filter by status works
- [ ] Navigation between Dashboard, Book, and My Bookings works
- [ ] Error messages display for validation failures (duplicate, past date, etc.)

---

## Dependencies on Later Phases

| Set Up Here | Used By |
|-------------|---------|
| BookingService.CreateBookingAsync | Phase 5 (Lottery assigns ParkingSlotId to Won bookings) |
| BookingService.ConfirmBookingAsync | Phase 6 (Confirmation deadline + expiry job) |
| ExceptionHandlingMiddleware | Phase 5+ (all controllers benefit from centralized error handling) |
| LocationService queries | Phase 5 (Lottery needs available slots), Phase 8 (Admin location management) |
| BookingDto / MyBookingsResponse | Phase 7 (Email includes booking details) |
| AppLayout with navigation | Phase 6+ (all new pages slot into the same layout) |
| CalendarPicker component | Phase 8 (Admin blocked day management reuses calendar) |
