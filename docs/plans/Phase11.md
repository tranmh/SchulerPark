# Phase 11: 2D Grid-Based Parking Slot Layout

## Context
Add a 2D grid-based visual layout for parking lots per location. Admins configure the grid layout (dimensions, slot placement, obstacle/road cells via drag-and-drop), and users see a read-only color-coded grid showing real-time slot availability when booking. Each of the 4 locations has its own independent grid.

---

## Key Design Decisions

- **Grid position stored on ParkingSlot**: Add nullable `GridRow` and `GridColumn` to the existing `ParkingSlot` entity. Nullable because existing slots without a grid position should still work (backward-compatible).
- **Grid dimensions on Location**: Add `GridRows` and `GridColumns` (nullable int) to `Location`. Null means "no grid configured yet."
- **New entity for non-slot grid cells**: `GridCell` entity for obstacle, road, entrance, and label cells. Each belongs to a Location with row, column, and `CellType` enum.
- **No drag-and-drop library**: Use native HTML5 drag-and-drop API. Click-to-place is the primary interaction, drag-and-drop as enhancement.
- **Grid availability endpoint**: `GET /api/locations/{id}/grid-availability?date=&timeSlot=` returns full grid layout plus per-slot booking status.
- **Admin grid config endpoint**: `PUT /api/admin/locations/{id}/grid` saves entire grid configuration transactionally.
- **Admin page**: New `/admin/grid-layout` page; existing SlotsPage table CRUD stays intact.
- **User view**: Informational grid on BookingPage between TimeSlot and Confirm steps (lottery still assigns slots).

---

## Implementation Plan

### Database Changes

#### Step 1: GridCellType Enum
**New:** `backend/SchulerPark.Core/Enums/GridCellType.cs`

Values: Empty, Obstacle, Road, Entrance, Label

#### Step 2: GridCell Entity
**New:** `backend/SchulerPark.Core/Entities/GridCell.cs`

Fields: Id, LocationId (FK), Row, Column, CellType, Label (optional)

#### Step 3: Modify ParkingSlot Entity
**Modify:** `backend/SchulerPark.Core/Entities/ParkingSlot.cs`

Add: `int? GridRow`, `int? GridColumn`

#### Step 4: Modify Location Entity
**Modify:** `backend/SchulerPark.Core/Entities/Location.cs`

Add: `int? GridRows`, `int? GridColumns`, `ICollection<GridCell> GridCells`

#### Step 5: GridCell EF Configuration
**New:** `backend/SchulerPark.Infrastructure/Data/Configurations/GridCellConfiguration.cs`

Unique index on (LocationId, Row, Column).

#### Step 6: Modify ParkingSlotConfiguration
Add filtered unique index on (LocationId, GridRow, GridColumn) where both not null.

#### Step 7: Modify LocationConfiguration
Add GridRows and GridColumns property config.

#### Step 8: Update AppDbContext
Add `DbSet<GridCell> GridCells`.

#### Step 9: EF Core Migration
`dotnet ef migrations add AddGridLayout`

---

### Backend API

#### Step 10: Grid DTOs
**New:** `backend/SchulerPark.Api/DTOs/Grid/`

- `GridConfigurationDto` — response with grid dimensions, placed slots, cells
- `SaveGridConfigurationRequest` — request with dimensions, slot positions, cells
- `GridAvailabilityDto` — response with slot statuses (Free/Booked/Blocked/Own/Inactive)

#### Step 11: Admin Grid Endpoints
**Modify:** `backend/SchulerPark.Api/Controllers/AdminController.cs`

- `GET /api/admin/locations/{id}/grid` — load grid config
- `PUT /api/admin/locations/{id}/grid` — save grid config (transactional full-replace)

#### Step 12: User Grid Availability Endpoint
**Modify:** `backend/SchulerPark.Api/Controllers/LocationController.cs`

- `GET /api/locations/{id}/grid-availability?date=&timeSlot=` — slot statuses for user view

---

### Frontend

#### Step 13: Grid Types
**New:** `frontend/src/types/grid.ts`

#### Step 14: Service Methods
**Modify:** `frontend/src/services/adminService.ts` — add grid config methods
**Modify:** `frontend/src/services/locationService.ts` — add grid availability method

#### Step 15: Admin Grid Editor Page
**New:** `frontend/src/pages/Admin/GridLayoutPage.tsx`

Location selector, grid dimension inputs, grid canvas, slot palette sidebar, cell type picker toolbar, save button.

#### Step 16: Grid Editor Components
**New:**
- `frontend/src/components/grid/GridEditor.tsx` — CSS Grid canvas with drag-and-drop
- `frontend/src/components/grid/SlotPalette.tsx` — draggable unplaced slots list
- `frontend/src/components/grid/CellTypePicker.tsx` — toolbar for cell type painting

#### Step 17: User Grid View
**New:** `frontend/src/components/grid/ParkingGridView.tsx` — read-only color-coded grid

Color coding: Free=green, Booked=red, Blocked=gray, Own=blue, Inactive=faded

#### Step 18: Integration
**Modify:** `frontend/src/pages/Booking/BookingPage.tsx` — show grid after timeslot selection
**Modify:** `frontend/src/App.tsx` — add `/admin/grid-layout` route
**Modify:** `frontend/src/components/AppLayout.tsx` — add Grid Layout nav item

---

## Files Summary

### Backend (New: 5, Modified: 5)
| Action | File |
|--------|------|
| NEW | `Core/Enums/GridCellType.cs` |
| NEW | `Core/Entities/GridCell.cs` |
| NEW | `Infrastructure/Data/Configurations/GridCellConfiguration.cs` |
| NEW | `Api/DTOs/Grid/GridConfigurationDto.cs` |
| NEW | `Api/DTOs/Grid/GridAvailabilityDto.cs` |
| MODIFY | `Core/Entities/ParkingSlot.cs` |
| MODIFY | `Core/Entities/Location.cs` |
| MODIFY | `Infrastructure/Data/AppDbContext.cs` |
| MODIFY | `Api/Controllers/AdminController.cs` |
| MODIFY | `Api/Controllers/LocationController.cs` |

### Frontend (New: 6, Modified: 5)
| Action | File |
|--------|------|
| NEW | `src/types/grid.ts` |
| NEW | `src/pages/Admin/GridLayoutPage.tsx` |
| NEW | `src/components/grid/GridEditor.tsx` |
| NEW | `src/components/grid/SlotPalette.tsx` |
| NEW | `src/components/grid/CellTypePicker.tsx` |
| NEW | `src/components/grid/ParkingGridView.tsx` |
| MODIFY | `src/services/adminService.ts` |
| MODIFY | `src/services/locationService.ts` |
| MODIFY | `src/pages/Booking/BookingPage.tsx` |
| MODIFY | `src/App.tsx` |
| MODIFY | `src/components/AppLayout.tsx` |

---

## Verification
- [ ] `dotnet build` succeeds
- [ ] `npm run build` succeeds
- [ ] Admin grid editor loads, saves, and reloads grid config
- [ ] User booking page shows color-coded grid when location has grid configured
- [ ] Locations without grid configured work exactly as before
- [ ] Existing SlotsPage table CRUD unchanged
