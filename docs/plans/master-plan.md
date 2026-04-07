# SchulerPark - Parking Slot Booking System

## Context
Build a greenfield parking slot booking system for multiple office locations (Goeppingen, Erfurt, Hessdorf, Gemmingen). Employees book morning or afternoon parking slots, a fair lottery assigns slots when demand exceeds supply, and users confirm actual usage. The app must support Azure AD SSO with local auth fallback. DSGVO-compliant (German GDPR).

---

## Tech Stack
- **Backend:** .NET 10, ASP.NET Core Web API, Entity Framework Core, PostgreSQL
- **Frontend:** React 19 + Vite + TypeScript
- **Auth:** Microsoft.Identity.Web (Azure AD) + ASP.NET Core Identity (local fallback)
- **Email:** SMTP via MailKit
- **Scheduling:** Hangfire (lottery at 10 PM daily + data retention cleanup)
- **Containerization:** Docker Compose (2 containers: app + PostgreSQL)
- **Target OS:** Linux (Ubuntu 24.04 LTS)
- **Repo structure:** Monorepo — `/backend` and `/frontend`

---

## Architecture Overview

```
/
├── docker-compose.yml
├── Dockerfile                    # Multi-stage: build backend + frontend, serve as one
├── docs/plans/                   # Detailed implementation plans
├── CLAUDE.md
├── README.md
│
├── frontend/ (React + Vite + TypeScript)
│   ├── src/
│   │   ├── components/           # Shared UI components
│   │   ├── pages/                # Route pages
│   │   │   ├── Dashboard/
│   │   │   ├── Booking/
│   │   │   ├── MyBookings/
│   │   │   ├── Profile/
│   │   │   └── Admin/
│   │   ├── services/             # API client (axios/fetch)
│   │   ├── hooks/                # Custom React hooks
│   │   ├── contexts/             # Auth context
│   │   └── types/                # TypeScript interfaces
│   ├── package.json
│   └── vite.config.ts
│
├── backend/
│   ├── SchulerPark.Api/          # API host
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs
│   │   │   ├── BookingController.cs
│   │   │   ├── LocationController.cs
│   │   │   ├── ProfileController.cs
│   │   │   └── AdminController.cs
│   │   ├── Middleware/
│   │   └── Program.cs
│   ├── SchulerPark.Core/         # Domain layer
│   │   ├── Entities/
│   │   ├── Enums/
│   │   └── Interfaces/
│   ├── SchulerPark.Infrastructure/ # Data + services
│   │   ├── Data/
│   │   │   ├── AppDbContext.cs
│   │   │   ├── Migrations/
│   │   │   └── Seed/
│   │   ├── Repositories/
│   │   ├── Services/
│   │   │   ├── LotteryService.cs
│   │   │   ├── EmailService.cs
│   │   │   ├── DataRetentionService.cs
│   │   │   └── Strategies/
│   │   │       ├── ILotteryStrategy.cs
│   │   │       ├── PureRandomStrategy.cs
│   │   │       ├── WeightedHistoryStrategy.cs
│   │   │       └── RoundRobinStrategy.cs
│   │   └── Jobs/
│   │       ├── LotteryJob.cs
│   │       ├── ConfirmationExpiryJob.cs
│   │       └── DataRetentionJob.cs
│   └── SchulerPark.sln
```

---

## Docker Compose Setup

```yaml
# 2 containers: app (backend serves frontend as static files) + PostgreSQL
services:
  db:
    image: postgres:16-alpine
    volumes: [postgres_data:/var/lib/postgresql/data]
    environment: [POSTGRES_DB, POSTGRES_USER, POSTGRES_PASSWORD]
    healthcheck: pg_isready

  app:
    build: .
    ports: ["8080:8080"]
    depends_on: [db]
    environment: [ConnectionStrings__Default, Auth__*, Smtp__*, etc.]
```

**Dockerfile** (multi-stage):
1. Stage 1: Build frontend (`node:22-alpine`, `npm ci && npm run build`)
2. Stage 2: Build backend (`mcr.microsoft.com/dotnet/sdk:10.0`, `dotnet publish`)
3. Stage 3: Runtime (`mcr.microsoft.com/dotnet/aspnet:10.0`), copy frontend dist to wwwroot, copy published backend

---

## Database Schema (PostgreSQL)

### Core Entities

**Users** — Id (GUID), Email, DisplayName, CarLicensePlate, AzureAdObjectId?, PasswordHash?, Role (User/Admin), CreatedAt, UpdatedAt

**Locations** — Id, Name, Address, IsActive, DefaultAlgorithm (enum)

**ParkingSlots** — Id, LocationId (FK), SlotNumber, Label?, IsActive

**Bookings** — Id, UserId (FK), ParkingSlotId? (assigned after lottery), LocationId (FK), Date, TimeSlot (Morning/Afternoon), Status (Pending/Won/Lost/Confirmed/Cancelled/Expired), ConfirmedAt?, CreatedAt

**LotteryRuns** — Id, LocationId (FK), Date, TimeSlot, Algorithm, RanAt, TotalBookings, AvailableSlots

**LotteryHistory** — Id, UserId (FK), LocationId (FK), Date, TimeSlot, Won (bool)

**BlockedDays** — Id, LocationId (FK), ParkingSlotId? (nullable = whole location), Date, Reason?, BlockedByUserId (FK), CreatedAt

### DSGVO / Data Retention
- **DataRetentionJob** runs weekly via Hangfire
- Deletes Bookings, LotteryHistory entries older than 1 year
- Anonymizes related LotteryRun statistics (keeps aggregates, removes user references)
- Users can request data export (Art. 15 DSGVO) and deletion (Art. 17 DSGVO)
- Privacy notice endpoint / page

---

## Fair Booking Algorithm (3 strategies)

All implement `ILotteryStrategy` interface. Default: **WeightedHistory**.

1. **PureRandom** — Equal chance. `Random.Shared.Shuffle(candidates)`, pick top N.
2. **WeightedHistory** (default) — Weight = `1.0 + (consecutiveLosses * 0.5)`. Weighted random selection. Resets on win.
3. **RoundRobin** — Sort by last win date ascending. Longest-without-win gets priority. Ties broken randomly.

Configurable per location by admin.

### Lottery Flow (daily at 10 PM via Hangfire)
1. For each location, for each time slot (morning + afternoon):
   - Get Pending bookings for next day
   - Get available slots (active, not blocked)
   - If demand <= supply: all win, assign slots randomly
   - If demand > supply: run configured lottery, assign winners, mark losers
2. Send emails to all participants
3. Log run in LotteryRuns + LotteryHistory

---

## API Endpoints

```
# Auth
POST   /api/auth/login                    # Local login → JWT
POST   /api/auth/register                 # Local registration
GET    /api/auth/azure-login              # Azure AD OIDC redirect
POST   /api/auth/azure-callback           # Azure AD callback → JWT
POST   /api/auth/refresh                  # Refresh token
GET    /api/auth/me                       # Current user info

# Locations & Slots (public, authenticated)
GET    /api/locations                      # List active locations
GET    /api/locations/{id}/slots           # Slots for a location
GET    /api/locations/{id}/blocked-days    # Blocked days for a location
GET    /api/locations/{id}/availability    # Available dates + slot counts

# Bookings (user)
POST   /api/bookings                      # Create booking (date, location, timeSlot)
GET    /api/bookings/my                   # My bookings (filterable)
POST   /api/bookings/{id}/confirm         # Confirm slot usage
DELETE /api/bookings/{id}                 # Cancel booking

# Profile
GET    /api/profile                       # Get my profile
PUT    /api/profile                       # Update (license plate, display name)
GET    /api/profile/data-export           # DSGVO Art. 15 data export
DELETE /api/profile/data                  # DSGVO Art. 17 data deletion request

# Admin
GET    /api/admin/locations               # All locations (incl. inactive)
POST   /api/admin/locations               # Create location
PUT    /api/admin/locations/{id}          # Update location
DELETE /api/admin/locations/{id}          # Deactivate location

GET    /api/admin/slots?locationId=       # All slots
POST   /api/admin/slots                   # Create slot
PUT    /api/admin/slots/{id}             # Update slot
DELETE /api/admin/slots/{id}             # Deactivate slot

POST   /api/admin/blocked-days            # Block a day (location or specific slot)
DELETE /api/admin/blocked-days/{id}       # Unblock

GET    /api/admin/bookings                # All bookings (filterable)
PUT    /api/admin/locations/{id}/algorithm # Set lottery algorithm

GET    /api/admin/lottery-runs            # Lottery run history
POST   /api/admin/lottery/trigger         # Manually trigger lottery (optional)

# Privacy
GET    /api/privacy                       # Privacy notice / DSGVO info
```

---

## Detailed Implementation Phases

### Phase 1: Project Scaffolding & Docker Setup
1. Create monorepo folder structure
2. Initialize .NET 10 solution with 3 projects:
   - `dotnet new sln` in `/backend`
   - `dotnet new webapi` for SchulerPark.Api
   - `dotnet new classlib` for SchulerPark.Core
   - `dotnet new classlib` for SchulerPark.Infrastructure
   - Add project references (Api → Infrastructure → Core)
3. Install NuGet packages:
   - Api: `Microsoft.Identity.Web`, `Hangfire`, `Swashbuckle`
   - Infrastructure: `Npgsql.EntityFrameworkCore.PostgreSQL`, `MailKit`, `Hangfire.PostgreSql`
   - Core: (no dependencies)
4. Create React + Vite + TypeScript frontend:
   - `npm create vite@latest frontend -- --template react-ts`
   - Install: `react-router-dom`, `axios`, `@msal/browser` (for Azure AD)
5. Create `Dockerfile` (multi-stage build)
6. Create `docker-compose.yml` (app + postgres)
7. Create `.dockerignore`, `.env.example`
8. Verify: `docker compose up --build` starts both containers

### Phase 2: Domain Models & Database
1. Define all enums in Core:
   - `TimeSlot` (Morning, Afternoon)
   - `BookingStatus` (Pending, Won, Lost, Confirmed, Cancelled, Expired)
   - `LotteryAlgorithm` (PureRandom, WeightedHistory, RoundRobin)
   - `UserRole` (User, Admin)
2. Define all entity classes in Core/Entities
3. Create `AppDbContext` with entity configurations (Fluent API)
4. Configure indexes: (UserId+Date+TimeSlot+LocationId unique on Bookings), (LocationId+Date on BlockedDays)
5. Create initial EF Core migration
6. Create seed data:
   - 4 locations (Goeppingen, Erfurt, Hessdorf, Gemmingen)
   - Sample parking slots per location (10-20 each)
   - 1 admin user (local auth)
7. Verify: migration applies, seed data populates

### Phase 3: Authentication
1. Backend:
   - Configure ASP.NET Core Identity for local users (User entity as IdentityUser)
   - `AuthController`: Register, Login (returns JWT), Refresh
   - JWT token generation service (access + refresh tokens)
   - Configure Microsoft.Identity.Web for Azure AD OIDC
   - Azure AD callback: find-or-create user, issue JWT
   - Auth middleware: validate JWT, populate ClaimsPrincipal
   - Role-based `[Authorize]` policies (User, Admin)
2. Frontend:
   - Auth context/provider (stores JWT, user info)
   - Login page with local form + "Sign in with Microsoft" button
   - MSAL browser integration for Azure AD flow
   - Axios interceptor for JWT in Authorization header
   - Protected route wrapper component
   - Auto-refresh token logic
3. Verify: register local user, login, access protected endpoint; Azure AD flow (if tenant configured)

### Phase 4: Core Booking Flow
1. Backend:
   - `LocationController`: GET /locations, GET /locations/{id}/slots, GET /locations/{id}/blocked-days, GET /locations/{id}/availability
   - `BookingController`: POST /bookings (validate: not blocked, not duplicate, within 1 month), GET /bookings/my (with pagination + filters), DELETE /bookings/{id} (only own, only Pending/Won)
   - Booking validation service (check blocked days, duplicate bookings, date range)
   - Repository pattern for data access
2. Frontend:
   - Dashboard page: overview of upcoming bookings + quick-book
   - Booking page: location selector → calendar (blocked days greyed out) → time slot picker → confirm
   - My Bookings page: list with status badges, cancel button, confirm button
3. Verify: create booking, see it in my bookings, cancel it, verify blocked days are not bookable

### Phase 5: Lottery System
1. Implement `ILotteryStrategy` interface:
   ```csharp
   interface ILotteryStrategy {
       List<LotteryResult> Execute(List<Booking> candidates, List<ParkingSlot> availableSlots);
   }
   ```
2. Implement `PureRandomStrategy`
3. Implement `WeightedHistoryStrategy` (query LotteryHistory for consecutive losses)
4. Implement `RoundRobinStrategy` (query LotteryHistory for last win date)
5. `LotteryService`: orchestrates the lottery for a location+date+timeSlot
   - Fetches pending bookings
   - Fetches available slots (active, not blocked)
   - Resolves strategy from location config
   - Executes strategy
   - Updates booking statuses (Won with assigned slot / Lost)
   - Writes LotteryHistory entries
   - Writes LotteryRun record
6. Hangfire setup:
   - Configure Hangfire with PostgreSQL storage
   - Register `LotteryJob` as recurring job at 10 PM daily (Europe/Berlin timezone)
   - LotteryJob iterates all active locations, both time slots, for next day
7. Unit tests for all 3 strategies with deterministic seeds
8. Verify: create multiple bookings for same slot/date, trigger lottery manually, verify winners/losers

### Phase 6: Confirmation & Expiry
1. Backend:
   - POST /bookings/{id}/confirm — only for Won bookings, sets ConfirmedAt
   - `ConfirmationExpiryJob` (Hangfire recurring):
     - Runs hourly
     - Finds Won bookings past confirmation deadline (1h after slot start)
     - Marks them as Expired
   - Confirmation deadline logic: Morning slots → must confirm by 7:00 AM, Afternoon → by 14:00
2. Frontend:
   - Confirm button on Won bookings (prominent, with deadline countdown)
   - Visual indicator of confirmation status
3. Verify: win lottery, don't confirm, verify expiry after deadline

### Phase 7: Email Notifications
1. `EmailService` using MailKit + SMTP configuration from appsettings
2. Email templates (HTML):
   - Booking placed confirmation
   - Lottery result: Won (with slot number + location + confirm deadline)
   - Lottery result: Lost
   - Confirmation reminder (sent at slot start time)
   - Cancellation confirmation
   - DSGVO: data export ready
3. Integrate email sending into:
   - BookingController (on create, on cancel)
   - LotteryService (after lottery run)
   - ConfirmationExpiryJob (reminder before expiry)
4. Make emails async (fire-and-forget or via Hangfire background job)
5. Verify: check emails with local SMTP tool (Papercut/MailHog in Docker for dev)

### Phase 8: Admin Features
1. Backend:
   - `AdminController`: full CRUD for locations, slots, blocked days
   - GET /admin/bookings with filters (date range, location, status, user)
   - PUT /admin/locations/{id}/algorithm
   - GET /admin/lottery-runs (history)
   - POST /admin/lottery/trigger (manual lottery run)
2. Frontend:
   - Admin layout with sidebar navigation
   - Locations management page (list, create, edit, toggle active)
   - Slots management page per location (list, create, edit, toggle active)
   - Blocked days management (calendar view, add/remove blocks with reason)
   - Bookings overview (table with filters, search)
   - Lottery configuration per location (algorithm dropdown)
   - Lottery run history
3. Verify: CRUD all admin entities, block a day, verify user can't book it

### Phase 9: DSGVO Compliance & Data Retention
1. `DataRetentionJob` (Hangfire, weekly):
   - Delete Bookings older than 1 year
   - Delete LotteryHistory older than 1 year
   - Keep anonymized LotteryRun aggregates
   - Log retention actions
2. User data rights:
   - GET /profile/data-export: generate JSON export of all user data (bookings, lottery history, profile)
   - DELETE /profile/data: soft-delete user, anonymize bookings, schedule hard delete
3. Privacy notice page (frontend) with:
   - What data is collected and why
   - Retention period (1 year)
   - User rights (access, deletion, export)
   - Contact information for data protection officer
4. Verify: create old test data, run retention job, verify deletion

### Phase 10: Polish & Documentation
1. Error handling:
   - Global exception handler middleware
   - Problem Details responses (RFC 9457)
   - Frontend error boundaries + toast notifications
2. Validation:
   - FluentValidation on all request DTOs
   - Frontend form validation
3. UI polish:
   - Responsive design (mobile-friendly for parking lot use)
   - Loading skeletons
   - Empty states
4. Date/timezone:
   - All dates in Europe/Berlin timezone
   - UTC storage in DB, local display in frontend
5. Write `CLAUDE.md` with project conventions and development instructions
6. Write `README.md` with setup guide, architecture overview, deployment instructions
7. Save detailed plans to `docs/plans/`

---

## Verification (End-to-End)
1. `docker compose up --build` — both containers start healthy
2. Open `http://localhost:8080` — frontend loads
3. Register local user → login → see dashboard
4. Book a parking slot for tomorrow morning → status = Pending
5. Trigger lottery (or wait for 10 PM job) → status = Won/Lost
6. If Won: confirm usage → status = Confirmed
7. Test cancellation flow
8. Admin: create location, add slots, block a day, verify blocked in user view
9. Check emails in MailHog (add as 3rd Docker container for dev)
10. Verify data retention: insert old records, trigger job, verify cleanup
11. DSGVO: export user data, request deletion
