# Phase 1: Project Scaffolding & Docker Setup

## Objective
Set up the complete monorepo structure, initialize all backend and frontend projects with their dependencies, create Docker build/deployment configuration, and verify the full stack starts end-to-end via `docker compose up --build`.

---

## Prerequisites
- .NET 10 SDK installed (or available in Docker build image)
- Node.js 22+ and npm installed (or available in Docker build image)
- Docker & Docker Compose installed on the development machine
- PostgreSQL 16 (via Docker — no local install needed)

---

## Steps

### Step 1: Create Monorepo Folder Structure

Create the following top-level directory layout:

```
/
├── backend/
├── frontend/
├── docs/plans/          (already exists)
├── docker-compose.yml
├── Dockerfile
├── .dockerignore
├── .env.example
├── CLAUDE.md
└── README.md
```

**Tasks:**
1. Create `backend/` directory at repo root.
2. Create `frontend/` directory at repo root.
3. Confirm `docs/plans/` already exists.

**Verification:** All directories exist. `ls -la` shows `backend/`, `frontend/`, `docs/`.

---

### Step 2: Initialize .NET Solution & Projects

Create a .NET 10 solution with three projects following clean-architecture layering.

**Tasks:**
1. In `/backend`, create the solution:
   ```bash
   cd backend
   dotnet new sln -n SchulerPark
   ```
2. Create the **API** project:
   ```bash
   dotnet new webapi -n SchulerPark.Api -o SchulerPark.Api --framework net10.0
   ```
3. Create the **Core** (domain) class library:
   ```bash
   dotnet new classlib -n SchulerPark.Core -o SchulerPark.Core --framework net10.0
   ```
4. Create the **Infrastructure** (data + services) class library:
   ```bash
   dotnet new classlib -n SchulerPark.Infrastructure -o SchulerPark.Infrastructure --framework net10.0
   ```
5. Add all three projects to the solution:
   ```bash
   dotnet sln add SchulerPark.Api/SchulerPark.Api.csproj
   dotnet sln add SchulerPark.Core/SchulerPark.Core.csproj
   dotnet sln add SchulerPark.Infrastructure/SchulerPark.Infrastructure.csproj
   ```
6. Set up **project references** (dependency graph: Api → Infrastructure → Core):
   ```bash
   dotnet add SchulerPark.Api reference SchulerPark.Infrastructure
   dotnet add SchulerPark.Api reference SchulerPark.Core
   dotnet add SchulerPark.Infrastructure reference SchulerPark.Core
   ```
7. Create placeholder directory structure inside each project:
   - `SchulerPark.Api/Controllers/`
   - `SchulerPark.Api/Middleware/`
   - `SchulerPark.Core/Entities/`
   - `SchulerPark.Core/Enums/`
   - `SchulerPark.Core/Interfaces/`
   - `SchulerPark.Infrastructure/Data/`
   - `SchulerPark.Infrastructure/Data/Migrations/`
   - `SchulerPark.Infrastructure/Data/Seed/`
   - `SchulerPark.Infrastructure/Repositories/`
   - `SchulerPark.Infrastructure/Services/`
   - `SchulerPark.Infrastructure/Services/Strategies/`
   - `SchulerPark.Infrastructure/Jobs/`

**Verification:**
- `dotnet build` succeeds from `/backend`.
- Solution file references all three `.csproj` files.
- Project references resolve correctly (`dotnet list reference` on each project).

---

### Step 3: Install Backend NuGet Packages

Install all required NuGet packages per project.

**SchulerPark.Api:**
| Package | Purpose |
|---|---|
| `Microsoft.Identity.Web` | Azure AD OIDC authentication |
| `Microsoft.Identity.Web.UI` | Azure AD UI helpers |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | JWT Bearer token validation |
| `Hangfire.AspNetCore` | Background job dashboard + integration |
| `Swashbuckle.AspNetCore` | Swagger / OpenAPI documentation |

```bash
cd SchulerPark.Api
dotnet add package Microsoft.Identity.Web
dotnet add package Microsoft.Identity.Web.UI
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Hangfire.AspNetCore
dotnet add package Swashbuckle.AspNetCore
```

**SchulerPark.Infrastructure:**
| Package | Purpose |
|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | EF Core PostgreSQL provider |
| `Microsoft.EntityFrameworkCore.Design` | EF Core tooling (migrations) |
| `MailKit` | SMTP email sending |
| `Hangfire.PostgreSql` | Hangfire PostgreSQL storage backend |

```bash
cd SchulerPark.Infrastructure
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package MailKit
dotnet add package Hangfire.PostgreSql
```

**SchulerPark.Core:**
- No external dependencies (pure domain layer).

**Verification:**
- `dotnet restore` succeeds from `/backend`.
- `dotnet build` succeeds from `/backend`.
- Each `.csproj` lists the expected `<PackageReference>` entries.

---

### Step 4: Initialize React + Vite + TypeScript Frontend

Scaffold the frontend application with React 19, Vite, and TypeScript.

**Tasks:**
1. From the repo root, scaffold the frontend:
   ```bash
   npm create vite@latest frontend -- --template react-ts
   ```
   > Note: If the `frontend/` directory was already created in Step 1, either scaffold into it directly or scaffold to a temp name and move contents.

2. Navigate into `frontend/` and install base dependencies:
   ```bash
   cd frontend
   npm install
   ```

3. Install additional runtime dependencies:
   ```bash
   npm install react-router-dom axios @azure/msal-browser @azure/msal-react
   ```

4. Install dev dependencies:
   ```bash
   npm install -D @types/react-router-dom
   ```

5. Create placeholder directory structure:
   - `src/components/`
   - `src/pages/Dashboard/`
   - `src/pages/Booking/`
   - `src/pages/MyBookings/`
   - `src/pages/Profile/`
   - `src/pages/Admin/`
   - `src/services/`
   - `src/hooks/`
   - `src/contexts/`
   - `src/types/`

6. Configure Vite dev server proxy for API calls in `vite.config.ts`:
   ```ts
   export default defineConfig({
     plugins: [react()],
     server: {
       proxy: {
         '/api': {
           target: 'http://localhost:5000',
           changeOrigin: true,
         },
       },
     },
   });
   ```

**Verification:**
- `npm run build` succeeds and produces a `dist/` folder.
- `npm run dev` starts the dev server on `http://localhost:5173`.
- Directory structure matches the architecture overview from the master plan.

---

### Step 5: Create Multi-Stage Dockerfile

Create a `Dockerfile` at repo root with three build stages.

**Stage 1 — Build Frontend:**
- Base image: `node:22-alpine`
- Working directory: `/app/frontend`
- Copy `frontend/package.json` and `frontend/package-lock.json`
- Run `npm ci`
- Copy remaining frontend source
- Run `npm run build`
- Output: `/app/frontend/dist`

**Stage 2 — Build Backend:**
- Base image: `mcr.microsoft.com/dotnet/sdk:10.0`
- Working directory: `/app/backend`
- Copy `backend/*.sln` and all `**/*.csproj` files
- Run `dotnet restore`
- Copy remaining backend source
- Run `dotnet publish SchulerPark.Api -c Release -o /app/publish`
- Output: `/app/publish`

**Stage 3 — Runtime:**
- Base image: `mcr.microsoft.com/dotnet/aspnet:10.0`
- Working directory: `/app`
- Copy published backend from Stage 2
- Copy frontend `dist/` to `wwwroot/` folder within published output
- Expose port `8080`
- Set `ASPNETCORE_URLS=http://+:8080`
- Entrypoint: `dotnet SchulerPark.Api.dll`

**Verification:**
- `docker build -t schulerpark .` completes successfully.
- Image size is reasonable (< 500 MB).

---

### Step 6: Create docker-compose.yml

Create `docker-compose.yml` at repo root defining two services.

**Service: `db` (PostgreSQL)**
- Image: `postgres:16-alpine`
- Container name: `schulerpark-db`
- Environment variables (from `.env`):
  - `POSTGRES_DB=schulerpark`
  - `POSTGRES_USER=schulerpark`
  - `POSTGRES_PASSWORD=${DB_PASSWORD}`
- Volumes: `postgres_data:/var/lib/postgresql/data`
- Healthcheck: `pg_isready -U schulerpark`
- Ports: `5432:5432` (for local dev access)

**Service: `app`**
- Build context: `.` (uses root Dockerfile)
- Container name: `schulerpark-app`
- Ports: `8080:8080`
- Depends on: `db` (condition: `service_healthy`)
- Environment variables:
  - `ConnectionStrings__Default=Host=db;Port=5432;Database=schulerpark;Username=schulerpark;Password=${DB_PASSWORD}`
  - `ASPNETCORE_ENVIRONMENT=Development`

**Named Volume:**
- `postgres_data` — persistent PostgreSQL data

**Verification:**
- `docker compose config` validates without errors.
- `docker compose up --build` starts both containers.
- PostgreSQL container reaches healthy state.
- App container starts and listens on port 8080.

---

### Step 7: Create .dockerignore

Create `.dockerignore` at repo root to keep Docker build context lean:

```
**/node_modules
**/bin
**/obj
**/dist
**/.git
**/.vs
**/.vscode
**/.idea
**/Thumbs.db
**/.DS_Store
*.md
!README.md
.env
.env.*
docker-compose*.yml
```

**Verification:** Docker build context is small; `docker build` does not send unnecessary files.

---

### Step 8: Create .env.example

Create `.env.example` at repo root as a template for required environment variables:

```env
# Database
DB_PASSWORD=changeme_strong_password

# Azure AD (optional — leave empty for local-auth-only mode)
AZURE_AD_TENANT_ID=
AZURE_AD_CLIENT_ID=
AZURE_AD_CLIENT_SECRET=

# SMTP (optional — leave empty to disable emails)
SMTP_HOST=
SMTP_PORT=587
SMTP_USERNAME=
SMTP_PASSWORD=
SMTP_FROM_ADDRESS=noreply@schulerpark.local

# App
ASPNETCORE_ENVIRONMENT=Development
```

**Verification:** `.env.example` exists and documents all required/optional variables.

---

### Step 9: Configure Backend Program.cs (Minimal Setup)

Update `Program.cs` in `SchulerPark.Api` to include minimal startup configuration needed for Phase 1 verification:

**Tasks:**
1. Configure Kestrel to listen on port 8080.
2. Add Swagger/OpenAPI services (development only).
3. Add a basic health-check endpoint (`GET /api/health` → 200 OK).
4. Configure static file serving from `wwwroot/` (for serving the built frontend).
5. Add SPA fallback routing (serve `index.html` for non-API routes).
6. Keep authentication and database setup as stubs/comments for Phase 2+3.

**Minimal `Program.cs` structure:**
```csharp
var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseStaticFiles();          // Serve frontend from wwwroot
app.MapControllers();

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "healthy" }));

// SPA fallback: serve index.html for non-API, non-file routes
app.MapFallbackToFile("index.html");

app.Run();
```

**Verification:**
- `dotnet run` starts the API on port 8080.
- `GET /api/health` returns `{"status":"healthy"}`.
- Swagger UI is accessible at `/swagger`.

---

### Step 10: Create CLAUDE.md

Create `CLAUDE.md` at repo root with basic project conventions for Claude Code:

**Contents:**
- Project name and one-line description
- Tech stack summary
- Repo structure overview (`/backend`, `/frontend`, `docs/plans/`)
- How to build: `dotnet build` (backend), `npm run build` (frontend), `docker compose up --build` (full stack)
- How to run locally: backend on port 5000, frontend dev server on port 5173
- Code conventions: C# — PascalCase, nullable reference types enabled; TypeScript — camelCase, strict mode
- Database: PostgreSQL via Docker, connection string in env

---

### Step 11: End-to-End Verification

Run the complete stack and verify everything works together.

**Verification Checklist:**

| # | Check | Expected Result |
|---|---|---|
| 1 | `dotnet build` in `/backend` | Build succeeds, no errors |
| 2 | `npm run build` in `/frontend` | Build succeeds, `dist/` folder created |
| 3 | `docker compose up --build` | Both containers start, no crash loops |
| 4 | PostgreSQL healthcheck | `docker compose ps` shows `db` as healthy |
| 5 | `curl http://localhost:8080/api/health` | Returns `{"status":"healthy"}` |
| 6 | Open `http://localhost:8080` in browser | React app (default Vite template) loads |
| 7 | Open `http://localhost:8080/swagger` | Swagger UI loads with health endpoint |
| 8 | `docker compose down -v` | Clean shutdown, volumes removed |

---

## Output Artifacts

After Phase 1 is complete, the repository will contain:

```
/
├── CLAUDE.md
├── README.md
├── Dockerfile
├── docker-compose.yml
├── .dockerignore
├── .env.example
├── .gitignore
├── docs/
│   └── plans/
│       ├── master-plan.md
│       └── Phase1.md
├── frontend/
│   ├── node_modules/          (gitignored)
│   ├── src/
│   │   ├── components/
│   │   ├── pages/
│   │   │   ├── Dashboard/
│   │   │   ├── Booking/
│   │   │   ├── MyBookings/
│   │   │   ├── Profile/
│   │   │   └── Admin/
│   │   ├── services/
│   │   ├── hooks/
│   │   ├── contexts/
│   │   ├── types/
│   │   ├── App.tsx
│   │   └── main.tsx
│   ├── package.json
│   ├── package-lock.json
│   ├── tsconfig.json
│   ├── vite.config.ts
│   └── index.html
├── backend/
│   ├── SchulerPark.sln
│   ├── SchulerPark.Api/
│   │   ├── SchulerPark.Api.csproj
│   │   ├── Program.cs
│   │   ├── Controllers/
│   │   └── Middleware/
│   ├── SchulerPark.Core/
│   │   ├── SchulerPark.Core.csproj
│   │   ├── Entities/
│   │   ├── Enums/
│   │   └── Interfaces/
│   └── SchulerPark.Infrastructure/
│       ├── SchulerPark.Infrastructure.csproj
│       ├── Data/
│       │   ├── Migrations/
│       │   └── Seed/
│       ├── Repositories/
│       ├── Services/
│       │   └── Strategies/
│       └── Jobs/
```

---

## Dependencies on Later Phases

| This Phase Sets Up | Used By |
|---|---|
| .NET solution structure & project references | Phase 2 (entities), Phase 3 (auth), Phase 4+ |
| EF Core + Npgsql packages | Phase 2 (migrations, DbContext) |
| Microsoft.Identity.Web + JWT packages | Phase 3 (authentication) |
| Hangfire + PostgreSql packages | Phase 5 (lottery jobs), Phase 6 (confirmation expiry) |
| MailKit package | Phase 7 (email notifications) |
| React + Router + Axios + MSAL | Phase 3 (auth UI), Phase 4 (booking UI) |
| Dockerfile & docker-compose.yml | All phases (build & deploy) |
| SPA fallback routing in Program.cs | Phase 3+ (frontend routing) |

---

## Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| .NET 10 SDK not yet available as stable Docker image | Docker build fails | Use .NET 9 preview images and upgrade later, or use `10.0-preview` tag if available |
| `npm create vite@latest` prompts interactively | CI/scripting fails | Use `--template react-ts` flag and `--yes` / non-interactive mode |
| PostgreSQL container slow to start | App container fails on startup | `depends_on` with `service_healthy` condition ensures ordering |
| Port 8080 already in use on dev machine | App won't start | Document port override via `APP_PORT` env var in `.env.example` |
| Large Docker build context | Slow builds | `.dockerignore` excludes `node_modules`, `bin`, `obj`, `.git` |
