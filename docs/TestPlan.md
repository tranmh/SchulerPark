# SchulerPark — End-to-End Test Plan

## 1. Start the Stack

```bash
cd SchulerPark
cp .env.example .env
docker compose up --build -d
```

Wait ~30 seconds for PostgreSQL + app startup + auto-migration.

## 2. Verify Services Are Running

```bash
docker compose ps
```

Expect 3 containers: `schulerpark-db` (healthy), `schulerpark-app` (running), `schulerpark-mailhog` (running).

```bash
curl http://localhost:8080/api/health
```

Expect: `{"status":"healthy","timestamp":"..."}`

## 3. Test Auth Flow

- [ ] Open **http://localhost:8080** — should show login page (Tailwind centered card)
- [ ] Register a new user: click "Register", fill email/name/password (min 8 chars)
- [ ] After registration, should redirect to Dashboard
- [ ] Logout, then login with `admin@schulerpark.local` / `Admin123!`
- [ ] Should see Dashboard + admin sidebar items

## 4. Test Booking Flow (as regular user)

- [ ] Login as the user you registered
- [ ] Click **"Book a Spot"** in sidebar
- [ ] Select a location (e.g., Goeppingen)
- [ ] Select a date on the calendar (tomorrow or later, within 1 month)
- [ ] Select Morning or Afternoon time slot
- [ ] Review and click **"Confirm Booking"**
- [ ] Go to **"My Bookings"** — should see booking with **Pending** status
- [ ] Try booking the same location/date/slot again — should get duplicate error

## 5. Test Lottery (as admin)

- [ ] Login as `admin@schulerpark.local` / `Admin123!`
- [ ] Open **Swagger**: http://localhost:8080/swagger
- [ ] Find `POST /api/lottery/run` and execute with `date` = tomorrow's date (e.g., `2026-04-09`)
- [ ] Or use curl:
  ```bash
  # First get a JWT token
  curl -X POST http://localhost:8080/api/auth/login \
    -H "Content-Type: application/json" \
    -d '{"email":"admin@schulerpark.local","password":"Admin123!"}'

  # Use the accessToken from response
  curl -X POST "http://localhost:8080/api/lottery/run?date=2026-04-09" \
    -H "Authorization: Bearer <YOUR_TOKEN>"
  ```
- [ ] Go to **My Bookings** (for the user who booked) — status should change to **Won** (since slots > bookings)
- [ ] The **"Confirm Usage"** button should appear with a deadline countdown

## 6. Test Confirmation & Expiry

- [ ] As the user with a Won booking, click **"Confirm Usage"**
- [ ] Status should change to **Confirmed**
- [ ] To test expiry: create another booking, run lottery, but do NOT confirm
- [ ] Check **Hangfire Dashboard**: http://localhost:8080/hangfire — verify "confirmation-expiry" job is registered

## 7. Test Email Notifications

- [ ] Open **MailHog**: http://localhost:8025
- [ ] Check inbox — should see emails for:
  - Booking created confirmation
  - Lottery result (Won/Lost)
- [ ] Create a booking and cancel it — cancellation email should appear

## 8. Test Admin Features

Login as admin, then test:

- [ ] **Locations** (`/admin/locations`): Create a new location, edit it, change algorithm, deactivate
- [ ] **Parking Slots** (`/admin/slots`): Select a location, create slots, edit, deactivate
- [ ] **Blocked Days** (`/admin/blocked-days`): Select a location, click a date to block it, add a reason
  - [ ] Then as a regular user, try to book that blocked date — should fail
  - [ ] Back as admin, click the blocked date again to unblock
- [ ] **All Bookings** (`/admin/bookings`): Filter by location, status, date range
- [ ] **Lottery History** (`/admin/lottery-history`): See past lottery runs

## 9. Test DSGVO + Profile Features

- [ ] As any user, go to **Profile** (button in sidebar bottom)
- [ ] Edit display name and license plate — click Save
- [ ] Verify **Preferred Parking Location** dropdown lists all active locations and persists after Save + reload
- [ ] Verify **Preferred Parking Slot** dropdown is **disabled** until a preferred location is chosen (detailed tests in Section 11)
- [ ] Click **"Download My Data"** — should download a JSON file with all your data
- [ ] Click **"Delete My Account"** — confirm in dialog — should log you out
- [ ] Try logging in again with that user — should fail (soft-deleted)
- [ ] Visit **http://localhost:8080/privacy** (no login needed) — should show privacy notice

## 10. Test Grid Layout (Phase 11)

Login as admin, then test:

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

## 11. Test Preferred Parking Slot (Lottery + Nearest-Fallback)

This section exercises the preferred-slot picker and the lottery placement
algorithm (exact match → Manhattan-nearest → random fallback). Assumes the
seed DB is in place (20 users, grid layouts for all 6 locations).

### 11.1 Profile UI

- [ ] Log in as `lisa.weber@schuler.de` / `Test1234!`
- [ ] Go to **Profile**. The Preferred Parking Slot dropdown should be **disabled** and show "No preference"
- [ ] Select **Preferred Parking Location** = *Weingarten*. Slot dropdown becomes enabled and lists Weingarten's active slots
- [ ] Pick any slot (e.g., `P001 — Parking Spot 1`) and click **Save Changes**. Success toast appears
- [ ] **Reload the page**. Both Preferred Location (*Weingarten*) and Preferred Slot (*P001*) should still be selected
- [ ] Change **Preferred Parking Location** to *Goeppingen*. The Slot dropdown should **reset to "No preference"** and re-populate with Goeppingen's slots
- [ ] Set **Preferred Parking Location** back to "No preference". Slot dropdown becomes **disabled** again

### 11.2 API validation (use Swagger or curl)

Get a JWT first:
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
  # Weingarten slot paired with Goeppingen as the preferred location:
  curl -i -X PUT http://localhost:8080/api/profile \
    -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
    -d "{\"displayName\":\"Lisa Weber\",\"carLicensePlate\":null,\"preferredLocationId\":\"b0000000-0000-0000-0000-000000000001\",\"preferredSlotId\":\"$SLOT\"}"
  ```

### 11.3 Lottery — exact preferred slot (happy path)

- [ ] As Lisa, set preferred location = Weingarten and preferred slot = `P001` (via UI, Section 11.1)
- [ ] Create a booking for **tomorrow, Morning** at Weingarten (UI or API)
- [ ] Log in as `admin@schulerpark.local` and run the lottery for tomorrow via Swagger or:
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

### 11.4 Lottery — nearest-slot fallback

Two users prefer the *same* slot; only one can get it, the other must receive
the Manhattan-nearest available slot.

- [ ] Set a second user's (e.g., `noah.zimmermann@schuler.de` / `Test1234!`) preferred location to Weingarten and preferred slot to **P001** (same as Lisa)
- [ ] Ensure both users have a **Pending** booking for the **same date + Morning** at Weingarten
- [ ] Run the lottery as admin (see 11.3)
- [ ] Inspect assignments:
  ```sql
  SELECT u."Email", s."SlotNumber", s."GridRow", s."GridColumn"
  FROM "Bookings" b
    JOIN "Users" u ON u."Id" = b."UserId"
    LEFT JOIN "ParkingSlots" s ON s."Id" = b."ParkingSlotId"
  WHERE b."LocationId" = 'b0000000-0000-0000-0000-000000000005'
    AND b."Date" = CURRENT_DATE + 1 AND b."TimeSlot" = 'Morning';
  ```
  Expect: the **earlier-created booking** gets `P001` (row 1, col 0); the other gets a slot one Manhattan step away — typically `P002` (row 1, col 1) or `P004` (row 2, col 0).
- [ ] Verify neither user got an unrelated far slot (e.g., `P018` at row 6)

### 11.5 Lottery — preferred slot unavailable

- [ ] In the admin **Blocked Days** UI, block Weingarten's `P001` specifically for some future date
- [ ] As Lisa (preferred = Weingarten/P001), book that date + Morning
- [ ] Run the lottery. Lisa should still **win a slot** (not Lost), and the assigned slot should be the Manhattan-nearest free neighbour (e.g., P002 or P004) — **no error** is thrown

### 11.6 Waitlist promotion respects preferred slot (optional)

- [ ] Arrange: user A wins Weingarten P001 for a date; user B (preferred slot = P001, same date) is Lost; user C (no preference, same date) is also Lost
- [ ] User A cancels the booking via **My Bookings**
- [ ] User B's booking should be promoted to **Won** ahead of user C, even if C has a higher history weight — because B explicitly preferred the freed slot
- [ ] Verify in DB:
  ```sql
  SELECT "Email", b."Status" FROM "Bookings" b
    JOIN "Users" u ON u."Id" = b."UserId"
  WHERE b."ParkingSlotId" IS NOT NULL AND u."Email" IN ('<user-B>', '<user-C>');
  ```

## 12. Test Error Handling

- [ ] Try `POST /api/bookings` with a past date — should get 400 with ProblemDetails + traceId
- [ ] Try accessing `/api/admin/locations` as a non-admin user — should get 403
- [ ] Try accessing any API endpoint without a token — should get 401

## 13. Cleanup

```bash
docker compose down -v
```
