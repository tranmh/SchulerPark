# SchulerPark

Parking slot booking system for multiple Schuler office locations. Employees book morning or afternoon parking slots, a fair lottery assigns slots when demand exceeds supply, and users confirm actual usage.

## Features

- Book parking slots at 4 locations (Goeppingen, Erfurt, Hessdorf, Gemmingen)
- Morning / Afternoon time slot selection
- Fair lottery system with 3 configurable algorithms (PureRandom, WeightedHistory, RoundRobin)
- Azure AD SSO with local auth fallback
- Email notifications for booking confirmations, lottery results, and reminders
- Admin panel for location/slot/blocked-day management
- DSGVO-compliant with data retention and user data export/deletion
- Confirmation deadlines with automatic expiry

## Architecture

```
┌─────────────┐     ┌──────────────────────────────────────┐
│  React SPA  │────>│  ASP.NET Core Web API                │
│  (Tailwind) │     │  ├── Controllers (Auth, Booking,     │
│             │     │  │   Location, Admin, Profile,       │
│             │     │  │   Lottery, Privacy)                │
│             │     │  ├── Middleware (ExceptionHandling)   │
│             │     │  └── Hangfire (Lottery, Expiry,       │
│             │     │      DataRetention)                   │
└─────────────┘     └──────────┬───────────────────────────┘
                               │
                    ┌──────────▼───────────┐
                    │  PostgreSQL 16       │
                    │  (EF Core + Hangfire)│
                    └──────────────────────┘
```

**Layered backend:**
- **SchulerPark.Core** — Domain entities, enums, interfaces, helpers (zero dependencies)
- **SchulerPark.Infrastructure** — EF Core DbContext, services, Hangfire jobs, email
- **SchulerPark.Api** — Controllers, DTOs, middleware, Hangfire configuration
- **SchulerPark.Tests** — xUnit tests for lottery strategies

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, Entity Framework Core |
| Frontend | React 19, Vite, TypeScript, Tailwind CSS v4 |
| Database | PostgreSQL 16 |
| Auth | Azure AD (OIDC) + local JWT |
| Jobs | Hangfire (PostgreSQL storage) |
| Email | MailKit (SMTP) |
| Deploy | Docker Compose |

## Quick Start

### Prerequisites

- Docker & Docker Compose

### Run

```bash
git clone https://github.com/tranmh/SchulerPark.git
cd SchulerPark
cp .env.example .env
# Edit .env with your database password and optional Azure AD / SMTP settings
docker compose up --build
```

| URL | Description |
|-----|-------------|
| http://localhost:8080 | Application |
| http://localhost:8080/swagger | API docs (dev only) |
| http://localhost:8080/hangfire | Job dashboard (dev only) |
| http://localhost:8025 | MailHog email UI |

### Default Credentials

- **Admin:** `admin@schulerpark.local` / `Admin123!`

### Local Development

**Backend:**
```bash
cd backend
dotnet restore
dotnet build
dotnet run --project SchulerPark.Api
# API runs on http://localhost:5000
# Swagger: http://localhost:5000/swagger
```

**Frontend:**
```bash
cd frontend
npm install
npm run dev
# Dev server on http://localhost:5173 (proxies /api to backend)
```

**Tests:**
```bash
cd backend
dotnet test
# Runs 14 xUnit tests for lottery strategies
```

## Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `DB_PASSWORD` | Yes | `changeme` | PostgreSQL password |
| `JWT_SECRET` | Yes | — | JWT signing key (min 32 chars) |
| `AZURE_AD_TENANT_ID` | No | — | Azure AD tenant (empty = local-only mode) |
| `AZURE_AD_CLIENT_ID` | No | — | Azure AD application ID |
| `AZURE_AD_CLIENT_SECRET` | No | — | Azure AD client secret |
| `SMTP_HOST` | No | `mailhog` | SMTP server (empty = emails disabled) |
| `SMTP_PORT` | No | `1025` | SMTP port |
| `SMTP_USERNAME` | No | — | SMTP username |
| `SMTP_PASSWORD` | No | — | SMTP password |
| `SMTP_FROM_ADDRESS` | No | `noreply@schulerpark.local` | Sender email |

## Project Structure

```
SchulerPark/
├── backend/
│   ├── SchulerPark.Api/            # ASP.NET Core Web API host
│   │   ├── Controllers/            # Auth, Booking, Location, Admin, Profile, Lottery
│   │   ├── DTOs/                   # Request/response records
│   │   └── Middleware/             # Exception handling
│   ├── SchulerPark.Core/           # Domain entities, enums, interfaces
│   ├── SchulerPark.Infrastructure/ # EF Core, services, jobs, email
│   └── SchulerPark.Tests/          # xUnit strategy tests
├── frontend/
│   └── src/
│       ├── components/             # Shared UI (AppLayout, Calendar, StatusBadge, etc.)
│       ├── pages/                  # Dashboard, Booking, MyBookings, Admin/*, Profile, Privacy
│       ├── services/               # API clients (auth, booking, location, admin, profile)
│       ├── contexts/               # Auth context with MSAL
│       └── types/                  # TypeScript interfaces
├── docs/plans/                     # Phase 1-10 implementation plans
├── Dockerfile                      # Multi-stage build (Node + .NET)
├── docker-compose.yml              # App + PostgreSQL + MailHog
└── .env.example                    # Environment variable template
```

## License

Private - Schuler Group internal use.
