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

## 9. Test DSGVO Features

- [ ] As any user, go to **Profile** (button in sidebar bottom)
- [ ] Edit display name and license plate — click Save
- [ ] Click **"Download My Data"** — should download a JSON file with all your data
- [ ] Click **"Delete My Account"** — confirm in dialog — should log you out
- [ ] Try logging in again with that user — should fail (soft-deleted)
- [ ] Visit **http://localhost:8080/privacy** (no login needed) — should show privacy notice

## 10. Test Error Handling

- [ ] Try `POST /api/bookings` with a past date — should get 400 with ProblemDetails + traceId
- [ ] Try accessing `/api/admin/locations` as a non-admin user — should get 403
- [ ] Try accessing any API endpoint without a token — should get 401

## 11. Cleanup

```bash
docker compose down -v
```
