# SchulerPark - Parking Slot Booking System

## Project Overview
Multi-location parking slot booking system with fair lottery assignment for Schuler office locations (Goeppingen, Erfurt, Hessdorf, Gemmingen).

## Tech Stack
- **Backend:** .NET 10, ASP.NET Core Web API, Entity Framework Core, PostgreSQL
- **Frontend:** React 19 + Vite + TypeScript
- **Auth:** Azure AD SSO (Microsoft.Identity.Web) + local auth fallback (ASP.NET Core Identity + JWT)
- **Scheduling:** Hangfire (lottery at 10 PM daily, Europe/Berlin)
- **Email:** MailKit via SMTP
- **Deploy:** Docker Compose (app + PostgreSQL)

## Repo Structure
```
/backend          .NET solution (Api, Core, Infrastructure)
/frontend         React + Vite + TypeScript
/docs/plans       Implementation plans
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
```

### Docker (full stack)
```bash
cp .env.example .env   # Edit .env with real values
docker compose up --build
# App: http://localhost:8080
# PostgreSQL: localhost:5432
```

## Code Conventions
- **C#:** PascalCase for public members, nullable reference types enabled, implicit usings
- **TypeScript:** camelCase for variables/functions, PascalCase for components/types, strict mode
- **Database:** UTC storage, Europe/Berlin display. All dates use DateOnly or DateTime UTC
- **API routes:** `/api/{resource}` (lowercase, plural nouns)
- **Project layers:** Core has zero dependencies. Infrastructure references Core. Api references both.

## Key Endpoints
- `GET /api/health` — Health check
- `GET /swagger` — API documentation (development only)

## Environment Variables
See `.env.example` for all required/optional configuration.
