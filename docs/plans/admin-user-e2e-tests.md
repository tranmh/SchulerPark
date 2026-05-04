# E2E Test Suite — Admin (Arbiter) + User Workflows

## Context

The user calls the operational role "arbiter" — in this codebase that is the **Admin** role (the role that arbitrates lottery, slots, and bookings). The user wants comprehensive end-to-end Playwright coverage of:

- **Admin** workflow: every screen and action under `/admin/*`
- **User** workflow: the full booking/profile journey a regular employee performs (which the Admin then supervises)

The goal is to land an executable, green test suite that exercises every feature, fixing real bugs (test or product) discovered along the way. Existing coverage in `e2e/tests/admin.spec.ts` is shallow (route-loads only, no CRUD, no negative paths) and `booking.spec.ts` covers only the happy path. This plan extends both.

The work happens in the `arbiter-e2e-tests` worktree. Approved decisions:

- **Scope**: Admin + User (skip SuperAdmin-only screens — already covered by SuperAdmin bootstrap commit; not in scope here).
- **Runtime**: Local dev servers (`dotnet run` + `npm run dev`), not docker compose. Playwright `BASE_URL=http://localhost:5173` (Vite proxies `/api` → `http://localhost:5000`).
- **Mock strategy**: **Hybrid** — real backend for happy paths; Playwright `page.route()` mocks only for error/empty/timing-sensitive cases.
- **Code edits**: Allowed — add `data-testid` attributes for stable selectors, fix real bugs.

## Test Strategy

### What runs against the real stack

The dev seed (`backend/SchulerPark.Infrastructure/Data/Seed/SeedData.cs`) provides everything we need:
- Admin user `admin@schulerpark.local` / `Admin123!`
- 6 locations (Goeppingen, Erfurt, Hessdorf, Gemmingen, Weingarten, Netphen)
- ~20 test users (`anna.mueller@schuler.de` / `Test1234!`, `finn.werner@schuler.de` / `Test1234!`, etc.)
- Slots, blocked days, grid layouts, sample bookings

Real-backend tests cover: navigation, RBAC redirects, location CRUD, slot CRUD, blocked-day CRUD, viewing bookings with filters, viewing lottery history, manual lottery trigger, grid layout read+save, user booking flow, profile updates, preferred location/slot, week-booking.

### Where we mock with `page.route()`

Targeted, surgical mocks only:
- **Lottery error states** — POST `/api/lottery/run` returning 400 ("already run for date") / 500
- **Empty list states** — GET `/api/admin/bookings` returning `{ items: [], total: 0 }` to assert empty UI
- **Server unreachable** — assert error banner / retry behavior on a known endpoint
- **403 from API** — admin endpoint returning 403 to confirm UI handles it

This way fidelity stays high for happy paths and tricky-to-reproduce edges still get coverage.

### Test isolation

- Each create-test prefixes generated names with `E2E-${testInfo.title.slug}-${Date.now()}` so parallel/repeat runs do not collide.
- Each spec teardowns its own data via `afterAll` calling the admin API directly (using a stored JWT) to deactivate any entities it created.
- Sequential execution (already configured: `workers: 1`, `fullyParallel: false`).

## Critical Files

### New

```
e2e/
├── helpers/
│   ├── auth.ts          # loginAsAdmin / loginAsUser / apiLogin (returns JWT)
│   ├── api.ts           # admin API helpers for setup + teardown
│   └── data.ts          # uniqueName(), todayBerlin(), tomorrowBerlin()
└── tests/
    ├── admin/
    │   ├── locations.spec.ts        # CRUD + algorithm change
    │   ├── slots.spec.ts            # CRUD scoped to location
    │   ├── blocked-days.spec.ts     # add/remove location-wide + slot-specific
    │   ├── bookings-view.spec.ts    # filters, pagination, status badges
    │   ├── lottery-history.spec.ts  # filter + pagination
    │   ├── lottery-trigger.spec.ts  # manual run + mocked error states
    │   ├── grid-layout.spec.ts      # load + smoke save
    │   └── permissions.spec.ts      # negative + 403/empty mocks
    └── user/
        ├── booking-full.spec.ts     # extends booking.spec.ts: cancel, conflicts
        ├── week-booking.spec.ts     # POST /api/bookings/week happy path
        ├── profile-full.spec.ts     # extends profile: data export, deletion (mocked)
        └── waitlist.spec.ts         # join waitlist + auto-promote (mocked promote)
```

### Edited (small, targeted)

- `frontend/src/pages/admin/LocationsPage.tsx` — add `data-testid` to: new-location button, name/address inputs, algorithm select, save/cancel buttons, row containers (`location-row-{id}`), row edit/deactivate buttons.
- `frontend/src/pages/admin/SlotsPage.tsx` — same pattern: new-slot, location-select, slot-number/label inputs, save, deactivate.
- `frontend/src/pages/admin/BlockedDaysPage.tsx` — location-select, date input, reason input, slot-select (optional), block button, row remove buttons.
- `frontend/src/pages/admin/BookingsPage.tsx` — filter selects (location/status), date inputs, pagination next/prev, results-table.
- `frontend/src/pages/admin/LotteryHistoryPage.tsx` — location filter, results-table, pagination.
- `frontend/src/pages/admin/GridLayoutPage.tsx` — location-select, save button, grid container.
- `frontend/src/pages/LotteryRunPage.tsx` (or wherever the "Run lottery now" UI lives — must verify; if not surfaced in UI, test the API call directly via `request.post` with admin JWT).

Edits are additive (`data-testid="..."`) and behavior-preserving. No restructuring.

### Touched only on bug discovery

Backend controllers / services or frontend service code — only if a test surfaces a real defect. Each fix gets a one-sentence reason in the commit, never a refactor.

## Execution Plan

1. **Stack up**
   - Terminal A: `cd backend && dotnet run --project SchulerPark.Api` (binds `:5000`)
   - Terminal B: `cd frontend && npm install && npm run dev` (Vite at `:5173`, proxies `/api` → `:5000`)
   - Wait until `GET http://localhost:5173/api/health` returns 200.
2. **Add Playwright env override**
   - Run tests with `BASE_URL=http://localhost:5173 npx playwright test` (no config change needed — already env-driven).
3. **Add data-testids** to the 6 admin pages + any user pages whose selectors are too brittle today.
4. **Write helpers** (`auth.ts`, `api.ts`, `data.ts`).
5. **Write tests file-by-file**, running each spec immediately after authoring (`npx playwright test admin/locations.spec.ts`). Iterate to green.
6. **Mock-based specs last** — once happy paths are stable.
7. **Final full run**: `npx playwright test` → all green.
8. **Bug log**: keep a running list of any product bugs discovered + fix commits, surface in final summary to the user.

## Verification

- `cd e2e && BASE_URL=http://localhost:5173 npx playwright test` — entire suite passes (chromium only, sequential).
- `npx playwright test --headed` for spot-checking a flaky spec during iteration.
- Final report includes: spec count, runtime, list of any product bugs fixed and a one-line description of each.
- Plan copied to `docs/plans/admin-user-e2e-tests.md` (project convention).

## Out of Scope

- SuperAdmin user-management UI (separate concern, recently shipped).
- Performance / load testing.
- Cross-browser (only chromium, matches existing config).
- Hangfire dashboard tests.
- PWA install / service-worker tests.
