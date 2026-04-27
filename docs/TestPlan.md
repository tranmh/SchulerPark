# SchulerPark — End-to-End Test Plan

Covers all phases through the 3-tier role system (SuperAdmin / Admin / User) added in 2026-04.

## 1. Start the Stack

```bash
cd SchulerPark
cp .env.example .env
docker compose up --build -d
```

Wait ~30 seconds for PostgreSQL + app startup + auto-migration + first-run bootstrap.

## 2. Verify Services Are Running

```bash
docker compose ps
```

Expect 3 containers: `schulerpark-db` (healthy), `schulerpark-app` (running), `schulerpark-mailhog` (running).

```bash
curl http://localhost:8080/api/health
```

Expect: `{"status":"healthy","timestamp":"..."}`

### 2.1 Verify First-Run Bootstrap (clean DB only)

If you started against an empty database, `BootstrapAdmin` should have created a SuperAdmin and written its credentials to disk.

- [ ] Inside the app container, check for the credentials file:
  ```bash
  docker compose exec app cat /app/admin.yml
  ```
  Expect a YAML file with `email: superadmin@schulerpark.local` (or the value of `BOOTSTRAP_SUPERADMIN_EMAIL`) and a generated `password`.
- [ ] Restart the app container and confirm the file is **not** rewritten — bootstrap is idempotent once any user exists.

> **Dev seed override:** when running with the development seed, both `superadmin@schulerpark.local` and `admin@schulerpark.local` are created with password `Admin123!`. The bootstrap is a no-op in that case.

## 3. Test Auth Flow

- [ ] Open **http://localhost:8080** — should show login page (Tailwind centered card)
- [ ] Register a new user: click "Register", fill email/name/password (min 8 chars)
- [ ] After registration, should redirect to Dashboard
- [ ] Logout, then login with `admin@schulerpark.local` / `Admin123!`
  - Sidebar shows the **Admin** section (Locations, Parking Slots, Grid Layout, Blocked Days, All Bookings, Lottery History) and an **amber "Admin"** badge near your name.
- [ ] Logout, then login with `superadmin@schulerpark.local` / `Admin123!`
  - Sidebar shows the same Admin section **plus** a **"Super Admin"** section containing **Users**, and a **violet "SuperAdmin"** badge near your name.

## 4. Test Booking Flow (as regular user)

- [ ] Login as the user you registered
- [ ] Click **"Book a Spot"** in sidebar
- [ ] Select a location (e.g., Goeppingen)
- [ ] Select a date on the calendar (tomorrow or later, within 1 month)
- [ ] Select Morning or Afternoon time slot
- [ ] Review and click **"Confirm Booking"**
- [ ] Go to **"My Bookings"** — should see booking with **Pending** status
- [ ] Try booking the same location/date/slot again — should get duplicate error

### 4.1 Week Booking

- [ ] On the Booking page, switch to **Week** mode
- [ ] Pick a Monday in the upcoming week and a time slot
- [ ] Submit — should create five Pending bookings (Mon–Fri) in **My Bookings**
- [ ] Any day already booked should be skipped without erroring out the whole week

## 5. Test Lottery (as admin)

- [ ] Login as `admin@schulerpark.local` / `Admin123!`
- [ ] Open **Swagger**: http://localhost:8080/swagger
- [ ] Find `POST /api/lottery/run` and execute with `date` = tomorrow's date (e.g., `2026-04-28`)
- [ ] Or use curl:
  ```bash
  TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@schulerpark.local","password":"Admin123!"}' | jq -r .accessToken)

  curl -X POST "http://localhost:8080/api/lottery/run?date=$(date -d '+1 day' +%Y-%m-%d)" \
    -H "Authorization: Bearer $TOKEN"
  ```
- [ ] Go to **My Bookings** (for the user who booked) — status should change to **Won** (since slots > bookings)
- [ ] The **"Confirm Usage"** button should appear with a deadline countdown

## 6. Test Confirmation & Expiry

- [ ] As the user with a Won booking, click **"Confirm Usage"** — status should change to **Confirmed**
- [ ] To test expiry: create another booking, run lottery, but do NOT confirm
- [ ] Open **Hangfire Dashboard** http://localhost:8080/hangfire and verify three recurring jobs are registered: `daily-lottery`, `confirmation-expiry`, `data-retention`
- [ ] Trigger `confirmation-expiry` manually from the Hangfire UI to simulate an expired Won booking — status should flip to **Expired** and any waitlisted user should be promoted (see §7)

## 7. Test Waitlist Auto-Promotion

- [ ] Set up: user A and user B both have Pending bookings for the same `(location, date, timeSlot)`. Run the lottery so A is **Won** and B is **Lost** (manipulate slot count or use a small location like Netphen)
- [ ] As user A, cancel the Won booking from **My Bookings**
- [ ] User B's booking should automatically flip to **Won** with the freed slot assigned. Email + push notification should fire (see MailHog at http://localhost:8025)
- [ ] If a user's `PreferredSlotId` matches the freed slot, they should be promoted ahead of users with no preference (verify in DB if needed)

## 8. Test Email Notifications

- [ ] Open **MailHog**: http://localhost:8025
- [ ] Check inbox — should see emails for:
  - Booking created confirmation
  - Lottery result (Won/Lost)
  - Waitlist promotion (from §7)
- [ ] Create a booking and cancel it — cancellation email should appear

## 9. Test Push Notifications

- [ ] On HTTPS or `localhost`, log in as a regular user. Open browser DevTools → Application → Service Workers — confirm the SW is active.
- [ ] Visit **Profile** and enable push notifications. Browser should prompt for permission; allow.
- [ ] Behind the scenes the frontend calls `GET /api/push/vapid-public-key` and `POST /api/push/subscribe`. Verify with:
  ```bash
  USER=$(curl -s -X POST http://localhost:8080/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"<your-user>","password":"<pwd>"}' | jq -r .accessToken)
  curl -s -H "Authorization: Bearer $USER" http://localhost:8080/api/push/vapid-public-key
  ```
- [ ] Trigger a waitlist promotion (§7) — the OS-level push notification should fire even if the tab is closed.
- [ ] Disable push from Profile (calls `DELETE /api/push/subscribe`) — subsequent triggers should not deliver notifications.

## 10. Test Admin Features (regular Admin)

Login as `admin@schulerpark.local` / `Admin123!`. The Admin role can manage day-to-day operations but cannot manage users.

- [ ] **Locations** (`/admin/locations`): Create a new location, edit it, change algorithm, deactivate
- [ ] **Parking Slots** (`/admin/slots`): Select a location, create slots, edit, deactivate
- [ ] **Blocked Days** (`/admin/blocked-days`): Select a location, click a date to block it, add a reason
  - [ ] Then as a regular user, try to book that blocked date — should fail
  - [ ] Back as admin, click the blocked date again to unblock
- [ ] **All Bookings** (`/admin/bookings`): Filter by location, status, date range
- [ ] **Lottery History** (`/admin/lottery-history`): See past lottery runs
- [ ] Visit `/admin/users` directly — should redirect to `/` (Admin lacks SuperAdmin)
- [ ] Curl `/api/admin/users` with the Admin's JWT — should return **403 Forbidden**

## 11. Test SuperAdmin Role Management

Login as `superadmin@schulerpark.local` / `Admin123!`. The SuperAdmin role inherits everything Admin can do, plus user management at `/admin/users`.

### 11.1 Inclusive admin policy

- [ ] As SuperAdmin, open `/admin/locations`, `/admin/slots`, `/admin/bookings` etc. — all should load (the `AdminOnly` policy accepts SuperAdmin).
- [ ] As SuperAdmin, run the lottery from Swagger (`POST /api/lottery/run`) — should succeed.

### 11.2 Users page (UI)

- [ ] Click **Users** in the Super Admin section. Page lists all users with columns: User, Role, Status, Auth, Actions.
- [ ] Use the search box to find a specific user by email or name.
- [ ] Filter by Role — verify counts change.
- [ ] **Promote** a regular user to Admin via the role dropdown. The next time **that user** logs in, the JWT carries the new role and they see the Admin sidebar.
- [ ] **Demote** them back to User. Same JWT round-trip caveat (the page footer reminds you of this).
- [ ] **Disable** a user — Status badge flips to "Disabled" and any active refresh tokens are revoked. The disabled user can no longer log in.
- [ ] **Enable** the user again — they can log in.
- [ ] **Delete** a user (with confirm dialog) — they're hard-removed and disappear from the list.

### 11.3 Self-protection guards

Each guard returns **400 Bad Request** with a problem-details message; the UI surfaces it in the red error banner.

- [ ] Try changing your own role — the role dropdown is disabled in the UI. Bypass via curl:
  ```bash
  SU=$(curl -s -X POST http://localhost:8080/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"superadmin@schulerpark.local","password":"Admin123!"}' | jq -r .accessToken)
  MYID=$(curl -s -H "Authorization: Bearer $SU" http://localhost:8080/api/auth/me | jq -r .id)

  curl -i -X PUT "http://localhost:8080/api/admin/users/$MYID/role" \
    -H "Authorization: Bearer $SU" -H "Content-Type: application/json" \
    -d '{"role":"Admin"}'
  ```
  Expect **400** ("You cannot change your own role.").
- [ ] Try disabling yourself: `PUT /api/admin/users/$MYID/disable` → 400.
- [ ] Try deleting yourself: `DELETE /api/admin/users/$MYID` → 400.

### 11.4 Last-SuperAdmin guard

- [ ] In a fresh dev DB there is exactly one SuperAdmin (`superadmin@schulerpark.local`). Promote a second user to SuperAdmin via the Users page.
- [ ] Demote the second SuperAdmin back to Admin — succeeds.
- [ ] Now there is one SuperAdmin again. From the **second user's** session (still Admin if you didn't refresh), confirm they cannot reach `/admin/users`.
- [ ] Promote a different user to SuperAdmin, then from that new SuperAdmin try to delete the original `superadmin@schulerpark.local`. After deletion, only one SuperAdmin remains.
- [ ] Try deleting the last remaining SuperAdmin from another SuperAdmin's session (you'll need a third one to do this) — expect **400** ("Cannot delete the last remaining SuperAdmin."). Same guard applies to demote and disable.

### 11.5 Negative — non-SuperAdmin access

- [ ] Login as a regular User → `/admin/users` redirects home; `GET /api/admin/users` → 403.
- [ ] Login as Admin → same behaviour (Admin is not SuperAdmin).

## 12. Test DSGVO + Profile Features

- [ ] As any user, go to **Profile** (button in sidebar bottom)
- [ ] Edit display name and license plate — click Save
- [ ] Verify **Preferred Parking Location** dropdown lists all active locations and persists after Save + reload
- [ ] Verify **Preferred Parking Slot** dropdown is **disabled** until a preferred location is chosen (detailed tests in §14)
- [ ] Click **"Download My Data"** — should download a JSON file with all your data (includes Role)
- [ ] Click **"Delete My Account"** — confirm in dialog — should log you out
- [ ] Try logging in again with that user — should fail (soft-deleted, awaiting retention job)
- [ ] Visit **http://localhost:8080/privacy** (no login needed) — should show privacy notice

## 13. Test Grid Layout

Login as admin (or superadmin), then test:

- [ ] **Grid Layout** (`/admin/grid-layout`): Should appear in admin sidebar
- [ ] Select a location (e.g., Goeppingen)
- [ ] Set grid dimensions (e.g., 5 rows x 8 columns) — grid should render
- [ ] **Place slots**: Click a slot in the palette, then click an empty grid cell — slot appears on the grid
- [ ] **Drag-and-drop**: Drag a slot from the palette onto an empty cell
- [ ] **Paint cell types**: Select "Road" tool, click cells to mark as road (dark gray). Try Obstacle, Entrance, Label.
- [ ] **Remove items**: Right-click a placed slot or cell — should remove it
- [ ] **Eraser tool**: Select Eraser, click cells to clear them
- [ ] Click **"Save Layout"** — should show success message
- [ ] Refresh the page, re-select the same location — layout should reload correctly
- [ ] **Clear Grid**: Click "Clear Grid" — all placements should be removed

Test user grid view:

- [ ] Login as a regular user
- [ ] Go to **"Book a Spot"**, select the location that has a grid configured
- [ ] Select a date and time slot, proceed to the Review step
- [ ] A **"Parking Layout"** section should appear showing the color-coded grid
  - [ ] Free slots in **green**, booked slots in **red**, blocked in **gray**
  - [ ] If the user has a booking for a slot, it should show in **blue**
  - [ ] Legend should appear below the grid
- [ ] Select a location without a grid configured — no grid should appear (backwards compatible)

## 14. Test Preferred Parking Slot (Lottery + Nearest-Fallback)

This section exercises the preferred-slot picker and the lottery placement
algorithm (exact match → Manhattan-nearest → random fallback). Assumes the
seed DB is in place (20 users, grid layouts for all 6 locations).

### 14.1 Profile UI

- [ ] Log in as `lisa.weber@schuler.de` / `Test1234!`
- [ ] Go to **Profile**. The Preferred Parking Slot dropdown should be **disabled** and show "No preference"
- [ ] Select **Preferred Parking Location** = *Weingarten*. Slot dropdown becomes enabled and lists Weingarten's active slots
- [ ] Pick any slot (e.g., `P001 — Parking Spot 1`) and click **Save Changes**. Success toast appears
- [ ] **Reload the page**. Both Preferred Location (*Weingarten*) and Preferred Slot (*P001*) should still be selected
- [ ] Change **Preferred Parking Location** to *Goeppingen*. The Slot dropdown should **reset to "No preference"** and re-populate with Goeppingen's slots
- [ ] Set **Preferred Parking Location** back to "No preference". Slot dropdown becomes **disabled** again

### 14.2 API validation (use Swagger or curl)

```bash
TOKEN=$(curl -s -X POST http://localhost:8080/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"lisa.weber@schuler.de","password":"Test1234!"}' | jq -r .accessToken)
SLOT=$(curl -s -H "Authorization: Bearer $TOKEN" \
  http://localhost:8080/api/locations/b0000000-0000-0000-0000-000000000005/slots \
  | jq -r '.[0].id')
```

- [ ] `PUT /api/profile` with a `preferredSlotId` but `preferredLocationId: null` → **400 Bad Request** ("Preferred slot requires a preferred location.")
  ```bash
  curl -i -X PUT http://localhost:8080/api/profile \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"displayName\":\"Lisa Weber\",\"carLicensePlate\":null,\"preferredLocationId\":null,\"preferredSlotId\":\"$SLOT\"}"
  ```
- [ ] `PUT /api/profile` with a slot belonging to a different location → **400 Bad Request** ("Preferred slot does not belong to the preferred location.")
  ```bash
  curl -i -X PUT http://localhost:8080/api/profile \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"displayName\":\"Lisa Weber\",\"carLicensePlate\":null,\"preferredLocationId\":\"b0000000-0000-0000-0000-000000000001\",\"preferredSlotId\":\"$SLOT\"}"
  ```

### 14.3 Lottery — exact preferred slot (happy path)

- [ ] As Lisa, set preferred location = Weingarten and preferred slot = `P001`
- [ ] Create a booking for **tomorrow, Morning** at Weingarten
- [ ] As admin, run the lottery for tomorrow:
  ```bash
  ADMIN=$(curl -s -X POST http://localhost:8080/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@schulerpark.local","password":"Admin123!"}' | jq -r .accessToken)
  curl -i -X POST "http://localhost:8080/api/lottery/run?date=$(date -d '+1 day' +%Y-%m-%d)" \
    -H "Authorization: Bearer $ADMIN"
  ```
- [ ] As Lisa, open **My Bookings**. The booking should be **Won** and the assigned slot should be **P001**
- [ ] Confirm in DB:
  ```sql
  SELECT b."Status", s."SlotNumber"
  FROM "Bookings" b LEFT JOIN "ParkingSlots" s ON s."Id" = b."ParkingSlotId"
  WHERE b."UserId" = (SELECT "Id" FROM "Users" WHERE "Email" = 'lisa.weber@schuler.de')
    AND b."Date" = CURRENT_DATE + 1;
  ```
  Expect: `Won | P001`.

### 14.4 Lottery — nearest-slot fallback

- [ ] Set `noah.zimmermann@schuler.de`'s preferred location to Weingarten and preferred slot to **P001** (same as Lisa)
- [ ] Ensure both have a Pending booking for the same date + Morning at Weingarten
- [ ] Run the lottery as admin (see §14.3)
- [ ] Inspect assignments:
  ```sql
  SELECT u."Email", s."SlotNumber", s."GridRow", s."GridColumn"
  FROM "Bookings" b
    JOIN "Users" u ON u."Id" = b."UserId"
    LEFT JOIN "ParkingSlots" s ON s."Id" = b."ParkingSlotId"
  WHERE b."LocationId" = 'b0000000-0000-0000-0000-000000000005'
    AND b."Date" = CURRENT_DATE + 1 AND b."TimeSlot" = 'Morning';
  ```
  Expect: the **earlier-created booking** gets `P001`; the other gets a slot one Manhattan step away (typically `P002` or `P004`).
- [ ] Verify neither user got an unrelated far slot (e.g., `P018`)

### 14.5 Lottery — preferred slot blocked

- [ ] Block Weingarten's `P001` for some future date (Blocked Days UI)
- [ ] As Lisa (preferred = Weingarten/P001), book that date + Morning
- [ ] Run the lottery — Lisa should still **win a slot** (not Lost), assigned to the Manhattan-nearest free neighbour. No error thrown.

## 15. Test Error Handling

- [ ] `POST /api/bookings` with a past date → 400 with ProblemDetails + traceId
- [ ] `/api/admin/locations` as a non-admin User → 403
- [ ] `/api/admin/users` as Admin (not SuperAdmin) → 403
- [ ] Any `/api/*` endpoint without a token → 401
- [ ] Disabled user attempts to log in → 401

## 16. Run Automated Tests

```bash
# Backend
cd backend && dotnet test

# Frontend
cd ../frontend && npm run lint && npm run build
```

Expect 54 backend tests passing, lint clean (or only pre-existing warnings), Vite build successful.

## 17. Cleanup

```bash
docker compose down -v   # -v also wipes the postgres volume so bootstrap re-runs next time
```
