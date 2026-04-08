# SchulerPark - Parking Slot Booking System

## Project Overview
Multi-location parking slot booking system with fair lottery assignment for Schuler office locations (Goeppingen, Erfurt, Hessdorf, Gemmingen).

## Tech Stack
- **Backend:** .NET 10, ASP.NET Core Web API, Entity Framework Core, PostgreSQL
- **Frontend:** React 19 + Vite + TypeScript + Tailwind CSS v4
- **Auth:** Azure AD SSO (Microsoft.Identity.Web) + local auth fallback (JWT)
- **Scheduling:** Hangfire (lottery at 10 PM daily, expiry hourly, retention weekly — Europe/Berlin)
- **Email:** MailKit via SMTP (MailHog for dev)
- **Deploy:** Docker Compose (app + PostgreSQL + MailHog)

## Repo Structure
```
/backend          .NET solution (Api, Core, Infrastructure, Tests)
/frontend         React + Vite + TypeScript
/docs/plans       Implementation plans (Phase1-Phase10)
```

## Build & Run

### Local Development
```bash
# Backend (from /backend)
dotnet restore
dotnet build
dotnet run --project SchulerPark.Api

# Frontend (from /frontend)
npm install
npm run dev          # Dev server on http://localhost:5173
npm run build        # Production build to dist/

# Tests (from /backend)
dotnet test          # Runs xUnit strategy tests
```

### Docker (full stack)
```bash
cp .env.example .env   # Edit .env with real values
docker compose up --build
# App: http://localhost:8080
# PostgreSQL: localhost:5432
# MailHog UI: http://localhost:8025
# Swagger: http://localhost:8080/swagger (dev only)
# Hangfire: http://localhost:8080/hangfire (dev only)
```

### Default Credentials
- Admin: `admin@schulerpark.local` / `Admin123!`

## Code Conventions
- **C#:** PascalCase for public members, nullable reference types enabled, implicit usings
- **TypeScript:** camelCase for variables/functions, PascalCase for components/types, strict mode
- **Database:** UTC storage, Europe/Berlin display. All dates use DateOnly or DateTime UTC
- **API routes:** `/api/{resource}` (lowercase, plural nouns)
- **Project layers:** Core has zero dependencies. Infrastructure references Core. Api references both.
- **Frontend styling:** Tailwind CSS utility classes. No inline styles.

## Key Endpoints
- `GET /api/health` — Health check
- `GET /swagger` — API documentation (development only)
- `POST /api/auth/login` — Local login (returns JWT)
- `POST /api/auth/register` — Local registration
- `GET /api/locations` — List active locations
- `POST /api/bookings` — Create booking
- `GET /api/bookings/my` — User's bookings
- `GET /api/profile` — User profile
- `GET /api/profile/data-export` — DSGVO data export
- `DELETE /api/profile/data` — DSGVO account deletion
- `POST /api/lottery/run?date=` — Manual lottery trigger (admin)
- `GET /api/admin/*` — Admin CRUD endpoints (admin)

## Error Handling
- Backend throws `ValidationException`, `NotFoundException`, `ForbiddenException` from Core/Exceptions
- `ExceptionHandlingMiddleware` maps these to RFC 9457 ProblemDetails with TraceId
- Frontend pages use try/catch with inline error display (red alert boxes)
- Email sending is fire-and-forget with internal error logging (never blocks the request)

## Hangfire Jobs
| Job | Schedule | Purpose |
|-----|----------|---------|
| `LotteryJob` | Daily 10 PM | Assign parking slots for next day |
| `ConfirmationExpiryJob` | Hourly | Expire unconfirmed Won bookings |
| `DataRetentionJob` | Weekly Sunday 2 AM | Delete data older than 1 year, hard-delete soft-deleted users |

## Environment Variables
See `.env.example` for all required/optional configuration.
