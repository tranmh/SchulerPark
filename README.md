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

## Tech Stack

| Layer | Technology |
|---|---|
| Backend | .NET 10, ASP.NET Core Web API, Entity Framework Core |
| Frontend | React 19, Vite, TypeScript |
| Database | PostgreSQL 16 |
| Auth | Azure AD (OIDC) + local JWT |
| Jobs | Hangfire |
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

Open [http://localhost:8080](http://localhost:8080) in your browser.

### Local Development

**Backend:**
```bash
cd backend
dotnet restore
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

## Project Structure

```
SchulerPark/
├── backend/
│   ├── SchulerPark.Api/            # ASP.NET Core Web API host
│   ├── SchulerPark.Core/           # Domain entities, enums, interfaces
│   └── SchulerPark.Infrastructure/ # EF Core, repositories, services, jobs
├── frontend/
│   └── src/
│       ├── components/             # Shared UI components
│       ├── pages/                  # Route pages
│       ├── services/               # API client
│       ├── hooks/                  # Custom React hooks
│       ├── contexts/               # Auth context
│       └── types/                  # TypeScript interfaces
├── docs/plans/                     # Implementation plans
├── Dockerfile                      # Multi-stage build
├── docker-compose.yml              # App + PostgreSQL
└── .env.example                    # Environment variable template
```

## License

Private - Schuler Group internal use.
