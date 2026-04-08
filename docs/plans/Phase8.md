# Phase 8: Admin Features

## Context
Admins need to manage locations, parking slots, blocked days, view all bookings, configure lottery algorithms, and see lottery run history. This phase adds a full AdminController with CRUD endpoints and a comprehensive admin frontend with sidebar navigation, tables, forms, and a calendar-based blocked day manager.

---

## Key Design Decisions

- **Single AdminController**: All admin endpoints under `/api/admin/*` in one controller, following the master plan's route structure. The existing LotteryController (`/api/lottery/run`) stays separate for manual trigger — we add `/api/admin/lottery-runs` for history.
- **Soft delete for locations/slots**: `DELETE` sets `IsActive = false` rather than deleting rows (preserves foreign key integrity with existing bookings).
- **Admin pages use the existing AppLayout sidebar**: Add admin nav section visible only when `isAdmin` is true.
- **Reuse CalendarPicker**: For blocked day management calendar view.
- **No new DB migration**: All entities/tables already exist.

---

## Implementation Plan

### Backend

#### Step 1: Admin DTOs

**New directory:** `Api/DTOs/Admin/`

| File | Fields |
|------|--------|
| `CreateLocationRequest.cs` | `string Name, string Address` |
| `UpdateLocationRequest.cs` | `string Name, string Address, bool IsActive` |
| `AdminLocationDto.cs` | `Guid Id, string Name, string Address, bool IsActive, string DefaultAlgorithm, int TotalSlots, int ActiveSlots` |
| `CreateSlotRequest.cs` | `Guid LocationId, string SlotNumber, string? Label` |
| `UpdateSlotRequest.cs` | `string SlotNumber, string? Label, bool IsActive` |
| `AdminSlotDto.cs` | `Guid Id, Guid LocationId, string SlotNumber, string? Label, bool IsActive` |
| `CreateBlockedDayRequest.cs` | `Guid LocationId, Guid? ParkingSlotId, DateOnly Date, string? Reason` |
| `AdminBlockedDayDto.cs` | `Guid Id, Guid LocationId, string LocationName, Guid? ParkingSlotId, string? SlotNumber, DateOnly Date, string? Reason, DateTime CreatedAt` |
| `AdminBookingDto.cs` | `Guid Id, Guid UserId, string UserEmail, string UserDisplayName, Guid LocationId, string LocationName, Guid? ParkingSlotId, string? ParkingSlotNumber, DateOnly Date, string TimeSlot, string Status, DateTime? ConfirmedAt, DateTime CreatedAt` |
| `SetAlgorithmRequest.cs` | `string Algorithm` |
| `LotteryRunDto.cs` | `Guid Id, Guid LocationId, string LocationName, DateOnly Date, string TimeSlot, string Algorithm, DateTime RanAt, int TotalBookings, int AvailableSlots` |

#### Step 2: AdminController

**New:** `Api/Controllers/AdminController.cs`

All endpoints protected by `[Authorize(Policy = "AdminOnly")]`.

**Locations:**
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/admin/locations` | All locations (including inactive), with slot counts |
| POST | `/api/admin/locations` | Create location |
| PUT | `/api/admin/locations/{id}` | Update location (name, address, isActive) |
| DELETE | `/api/admin/locations/{id}` | Deactivate location (set IsActive=false) |
| PUT | `/api/admin/locations/{id}/algorithm` | Set lottery algorithm |

**Slots:**
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/admin/slots?locationId=` | All slots for a location (including inactive) |
| POST | `/api/admin/slots` | Create slot |
| PUT | `/api/admin/slots/{id}` | Update slot (number, label, isActive) |
| DELETE | `/api/admin/slots/{id}` | Deactivate slot |

**Blocked Days:**
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/admin/blocked-days?locationId=&from=&to=` | List blocked days |
| POST | `/api/admin/blocked-days` | Block a day |
| DELETE | `/api/admin/blocked-days/{id}` | Remove block |

**Bookings:**
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/admin/bookings?locationId=&status=&from=&to=&userId=&page=&pageSize=` | All bookings with filters |

**Lottery:**
| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/admin/lottery-runs?locationId=&from=&to=&page=&pageSize=` | Lottery run history |

Implementation: Controller uses AppDbContext directly (no separate admin service — keeps it simple for CRUD operations).

### Frontend

#### Step 3: Admin API Service

**New:** `frontend/src/services/adminService.ts`

All calls to `/api/admin/*` endpoints. Methods for each CRUD operation.

#### Step 4: Admin Types

**New:** `frontend/src/types/admin.ts`

TypeScript interfaces matching all admin DTOs.

#### Step 5: Admin Nav in AppLayout

**Modify:** `frontend/src/components/AppLayout.tsx`

Add admin section to sidebar (visible only when `isAdmin`):
- Locations
- Parking Slots
- Blocked Days
- All Bookings
- Lottery History

Separated from user nav by a divider + "Admin" heading.

#### Step 6: Admin Pages

**New files in `frontend/src/pages/Admin/`:**

| Page | Features |
|------|----------|
| `LocationsPage.tsx` | Table of all locations, create/edit modal, toggle active, algorithm dropdown per row |
| `SlotsPage.tsx` | Location selector + table of slots, create/edit modal, toggle active |
| `BlockedDaysPage.tsx` | Location selector + CalendarPicker (reused) to visualize/add/remove blocks, reason input |
| `BookingsPage.tsx` | Table with filters (location, status, date range, user search), pagination |
| `LotteryHistoryPage.tsx` | Table of lottery runs with filters, showing totals and algorithm used |

#### Step 7: Update App.tsx Routes

**Modify:** `frontend/src/App.tsx`

Add admin routes wrapped in `<ProtectedRoute requireAdmin>`:
- `/admin/locations`
- `/admin/slots`
- `/admin/blocked-days`
- `/admin/bookings`
- `/admin/lottery-history`

---

## Decisions

- **Single AdminController**: One controller for all `/api/admin/*` routes (matches master plan)
- **Blocked days UI**: Calendar view reusing CalendarPicker — click dates to block/unblock
- **Edit pattern**: Modal dialogs for create/edit forms (consistent with existing ConfirmDialog pattern)
- **Save plan**: docs/plans/Phase8.md

---

## Files Summary

### Backend (12 new, 0 modified)
| Action | File |
|--------|------|
| NEW | `Api/DTOs/Admin/CreateLocationRequest.cs` |
| NEW | `Api/DTOs/Admin/UpdateLocationRequest.cs` |
| NEW | `Api/DTOs/Admin/AdminLocationDto.cs` |
| NEW | `Api/DTOs/Admin/CreateSlotRequest.cs` |
| NEW | `Api/DTOs/Admin/UpdateSlotRequest.cs` |
| NEW | `Api/DTOs/Admin/AdminSlotDto.cs` |
| NEW | `Api/DTOs/Admin/CreateBlockedDayRequest.cs` |
| NEW | `Api/DTOs/Admin/AdminBlockedDayDto.cs` |
| NEW | `Api/DTOs/Admin/AdminBookingDto.cs` |
| NEW | `Api/DTOs/Admin/SetAlgorithmRequest.cs` |
| NEW | `Api/DTOs/Admin/LotteryRunDto.cs` |
| NEW | `Api/Controllers/AdminController.cs` |

### Frontend (8 new, 2 modified)
| Action | File |
|--------|------|
| NEW | `src/services/adminService.ts` |
| NEW | `src/types/admin.ts` |
| NEW | `src/pages/Admin/LocationsPage.tsx` |
| NEW | `src/pages/Admin/SlotsPage.tsx` |
| NEW | `src/pages/Admin/BlockedDaysPage.tsx` |
| NEW | `src/pages/Admin/BookingsPage.tsx` |
| NEW | `src/pages/Admin/LotteryHistoryPage.tsx` |
| MODIFY | `src/components/AppLayout.tsx` — admin nav items |
| MODIFY | `src/App.tsx` — admin routes |

### Docs
| NEW | `docs/plans/Phase8.md` |

---

## Verification

- [ ] `dotnet build` succeeds
- [ ] `npm run build` succeeds
- [ ] Admin endpoints return 403 for non-admin users
- [ ] GET /api/admin/locations returns all locations including inactive
- [ ] POST /api/admin/locations creates a new location
- [ ] PUT /api/admin/locations/{id} updates location name/address/isActive
- [ ] DELETE /api/admin/locations/{id} sets IsActive=false
- [ ] PUT /api/admin/locations/{id}/algorithm changes lottery algorithm
- [ ] CRUD for slots works (create, update, deactivate)
- [ ] Block a day → user cannot book that date
- [ ] Unblock a day → user can book again
- [ ] GET /api/admin/bookings with filters returns correct results
- [ ] GET /api/admin/lottery-runs returns lottery history
- [ ] Admin sidebar shows only for admin users
- [ ] All admin pages render correctly with data
- [ ] Non-admin users redirected from admin routes
