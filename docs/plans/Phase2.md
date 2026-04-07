# Phase 2: Domain Models & Database

## Objective
Define all domain entities and enums, create the EF Core DbContext with Fluent API configurations, generate the initial migration, and seed the database with locations, parking slots, and an admin user.

---

## Key Design Decision: User Entity vs ASP.NET Core Identity

**Decision: Keep `User` as a standalone entity. Do NOT extend `IdentityUser`.**

**Rationale:**
1. Core must have zero dependencies — `IdentityUser` would pull `Microsoft.AspNetCore.Identity.EntityFrameworkCore` into Core.
2. The master plan only needs two roles (User/Admin) — no need for Identity's full role/claims tables.
3. `PasswordHash` will be populated using `PasswordHasher<User>` standalone in Phase 3 — works without inheriting `IdentityUser`.
4. In Phase 3, auth will be a custom `AuthService` using `PasswordHasher<User>` for local login and `Microsoft.Identity.Web` for Azure AD.

---

## Steps

### Step 1: Define Enums in Core/Enums

Create four files with explicit integer values for stable DB storage. All enums stored as **strings** in PostgreSQL via Fluent API for readability.

| File | Enum | Values |
|------|------|--------|
| `Core/Enums/TimeSlot.cs` | TimeSlot | Morning = 0, Afternoon = 1 |
| `Core/Enums/BookingStatus.cs` | BookingStatus | Pending = 0, Won = 1, Lost = 2, Confirmed = 3, Cancelled = 4, Expired = 5 |
| `Core/Enums/LotteryAlgorithm.cs` | LotteryAlgorithm | PureRandom = 0, WeightedHistory = 1, RoundRobin = 2 |
| `Core/Enums/UserRole.cs` | UserRole | User = 0, Admin = 1 |

---

### Step 2: Define Entity Classes in Core/Entities

Seven entities, all using `Guid` primary keys and following the master plan schema.

**User** — Id, Email, DisplayName, CarLicensePlate?, AzureAdObjectId?, PasswordHash?, Role, CreatedAt, UpdatedAt
- Navigation: Bookings, LotteryHistories, BlockedDays

**Location** — Id, Name, Address, IsActive, DefaultAlgorithm
- Navigation: ParkingSlots, Bookings, LotteryRuns, LotteryHistories, BlockedDays

**ParkingSlot** — Id, LocationId (FK), SlotNumber (string, for alphanumeric like "A1"), Label?, IsActive
- Navigation: Location, Bookings, BlockedDays

**Booking** — Id, UserId (FK), ParkingSlotId? (FK, assigned after lottery), LocationId (FK), Date (DateOnly), TimeSlot, Status, ConfirmedAt?, CreatedAt
- Navigation: User, ParkingSlot?, Location

**LotteryRun** — Id, LocationId (FK), Date (DateOnly), TimeSlot, Algorithm, RanAt, TotalBookings, AvailableSlots
- Navigation: Location

**LotteryHistory** — Id, UserId (FK), LocationId (FK), Date (DateOnly), TimeSlot, Won (bool)
- Navigation: User, Location

**BlockedDay** — Id, LocationId (FK), ParkingSlotId? (nullable = whole location), Date (DateOnly), Reason?, BlockedByUserId (FK), CreatedAt
- Navigation: Location, ParkingSlot?, BlockedByUser

**Design Notes:**
- `DateOnly` maps natively to PostgreSQL `date` via Npgsql.
- `DateTime` fields store UTC. Display in Europe/Berlin is frontend concern.
- Collection nav props use `= []`. Required nav props use `= null!`.

---

### Step 3: Create AppDbContext

**File:** `Infrastructure/Data/AppDbContext.cs`

- Constructor takes `DbContextOptions<AppDbContext>`.
- Seven `DbSet<T>` properties (expression-bodied `=> Set<T>()` to avoid nullable warnings).
- `OnModelCreating` calls `ApplyConfigurationsFromAssembly` to auto-discover all `IEntityTypeConfiguration<T>`.

---

### Step 4: Create Entity Configurations (Fluent API)

Seven configuration files in `Infrastructure/Data/Configurations/`.

**Key configurations:**

| Entity | Table | Indexes | Delete Behavior |
|--------|-------|---------|-----------------|
| User | Users | Unique: Email, AzureAdObjectId (filtered, non-null only) | — |
| Location | Locations | Unique: Name | — |
| ParkingSlot | ParkingSlots | Unique: (LocationId, SlotNumber) | Cascade from Location |
| Booking | Bookings | **Unique: (UserId, Date, TimeSlot, LocationId) filtered: Status != Cancelled** | Cascade from User/Location, SetNull from ParkingSlot |
| LotteryRun | LotteryRuns | Unique: (LocationId, Date, TimeSlot) | Cascade from Location |
| LotteryHistory | LotteryHistories | (UserId, LocationId, Date) for WeightedHistory queries | Cascade from User/Location |
| BlockedDay | BlockedDays | (LocationId, Date) per spec | Cascade from Location, SetNull from ParkingSlot, **Restrict** from User |

**Notes:**
- All IDs use `HasDefaultValueSql("gen_random_uuid()")` (PG 16 built-in).
- All enums stored as strings with `HasConversion<string>()`.
- Booking unique index has `HasFilter("\"Status\" != 'Cancelled'")` so users can cancel and re-book.
- BlockedDay uses `Restrict` on BlockedByUser to prevent cascading user deletion.
- Timestamps use `HasDefaultValueSql("now() at time zone 'utc'")`.

---

### Step 5: Register DbContext in Program.cs

Replace the `// TODO Phase 2` comment with:

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
```

Add auto-migration + seed on startup (development only):

```csharp
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    await SeedData.SeedAsync(app.Services);
}
```

Change `app.Run()` to `await app.RunAsync()` for async support.

---

### Step 6: Create Seed Data

**File:** `Infrastructure/Data/Seed/SeedData.cs`

Uses **fixed GUIDs** for idempotent seeding (checks existence before inserting).

| Data | Details |
|------|---------|
| Admin user | admin@schulerpark.local, Role=Admin, placeholder PasswordHash (replaced in Phase 3) |
| 4 Locations | Goeppingen (20 slots), Erfurt (15 slots), Hessdorf (10 slots), Gemmingen (12 slots) |
| 57 Parking slots | Numbered P001-P0XX per location |

---

### Step 7: Generate Initial EF Core Migration

```bash
cd backend
dotnet ef migrations add InitialCreate \
    --project SchulerPark.Infrastructure \
    --startup-project SchulerPark.Api \
    --output-dir Data/Migrations
```

Pre-requisite: `dotnet tool install --global dotnet-ef` if not installed.

---

### Step 8: Verification

| # | Check | Expected |
|---|-------|----------|
| 1 | `dotnet build` in `/backend` | 0 errors |
| 2 | Migration files generated | 3 files in `Data/Migrations/` |
| 3 | `docker compose up --build` | App starts, DB migrated, data seeded |
| 4 | Query Users table | 1 admin user |
| 5 | Query Locations table | 4 locations |
| 6 | Query ParkingSlots count | 57 total (20+15+10+12) |
| 7 | Check indexes exist | All unique/composite indexes present |
| 8 | `GET /api/health` | Still returns healthy |

---

## Files Summary

**New files (20+3 generated):**

| # | File | Purpose |
|---|------|---------|
| 1 | `Core/Enums/TimeSlot.cs` | Enum |
| 2 | `Core/Enums/BookingStatus.cs` | Enum |
| 3 | `Core/Enums/LotteryAlgorithm.cs` | Enum |
| 4 | `Core/Enums/UserRole.cs` | Enum |
| 5 | `Core/Entities/User.cs` | Entity |
| 6 | `Core/Entities/Location.cs` | Entity |
| 7 | `Core/Entities/ParkingSlot.cs` | Entity |
| 8 | `Core/Entities/Booking.cs` | Entity |
| 9 | `Core/Entities/LotteryRun.cs` | Entity |
| 10 | `Core/Entities/LotteryHistory.cs` | Entity |
| 11 | `Core/Entities/BlockedDay.cs` | Entity |
| 12 | `Infrastructure/Data/AppDbContext.cs` | DbContext |
| 13-19 | `Infrastructure/Data/Configurations/*.cs` | 7 Fluent API configs |
| 20 | `Infrastructure/Data/Seed/SeedData.cs` | Seed data |
| 21-23 | `Infrastructure/Data/Migrations/*` | Auto-generated migration |

**Modified files (1):**

| File | Change |
|------|--------|
| `Api/Program.cs` | Add DbContext, auto-migrate, seed |

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `dotnet-ef` tool not installed | Install globally before migration step |
| Partial unique index syntax | Npgsql/EF Core handles `HasFilter()` correctly for PostgreSQL |
| Multiple cascade paths on BlockedDay | PostgreSQL allows this (unlike SQL Server). BlockedByUser uses Restrict. |
| Seed PasswordHash is placeholder | Acceptable for Phase 2. Phase 3 replaces with proper hash. |
| `app.Run()` → `await app.RunAsync()` | Required for async seeding. .NET 10 supports this natively. |

---

## Dependencies on Later Phases

| Set Up Here | Used By |
|-------------|---------|
| User entity + PasswordHash field | Phase 3 (auth with PasswordHasher) |
| Booking entity + Status enum | Phase 4 (booking flow), Phase 5 (lottery) |
| LotteryRun + LotteryHistory entities | Phase 5 (lottery system) |
| BlockedDay entity | Phase 4 (booking validation), Phase 8 (admin) |
| Location.DefaultAlgorithm | Phase 5 (strategy selection per location) |
| Seed admin user | Phase 3 (admin login) |
