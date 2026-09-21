# Phase 19 — Mobile optimisation of the LouisE frontend

## Context

LouisE is meant to be used from the parking lot on a phone (it is an installable PWA and the
master plan lists "mobile-friendly for parking lot use" as a requirement), but the UI is a
desktop-only layout. Exploration found:

- `AppLayout` renders a permanent 256px sidebar plus `px-10` content padding. On a 360px phone
  that leaves ~24px for page content. Every protected page is unusable until this is fixed.
- Only 16 real Tailwind responsive utilities exist in ~5,750 lines of TSX. `AppLayout`,
  `MyBookingsPage`, `CalendarPicker`, `ConfirmDialog` and all six table-based admin pages have none.
- `error.png` (iPhone screenshot of `/login`) shows the header under the status bar and inputs
  running edge to edge. `index.html` lacks `viewport-fit=cover`, `index.css` has no safe-area
  handling, and every input is `text-[14px]`, which makes iOS Safari zoom on focus.
- Playwright only runs at the default 1280×720 viewport, so none of this is caught in CI.

Decisions already taken with the user:

- **Navigation:** top bar with hamburger below `lg`, the existing sidebar becomes a slide-in
  overlay drawer. Desktop (`lg` and up) stays exactly as today.
- **Admin scope:** tables become horizontally scrollable, fixed widths and clipped header rows
  are fixed. No card-list redesign, no i18n sweep of the hardcoded admin strings.
  `GridLayoutPage` (drag-and-drop editor) stays desktop-only with a notice.
- **E2E:** add a Pixel 7 Playwright project with a small mobile smoke spec.

Constraints:

- Do not touch the auth logic in the uncommitted diff (`AuthContext.tsx`, `msalConfig.ts`,
  `LoginPage.tsx` login handlers, `LoginPage.test.tsx`). Login changes here are class-only.
- This checkout is the prod deploy checkout (symlinked). Don't run `docker compose build`
  against it as part of verification; use the throwaway `node:22-alpine` recipes below.
- Tailwind CSS v4 (`@import "tailwindcss"` + `@theme` in `src/index.css`); no config file.
- All new UI text goes through `src/i18n/locales/de.json` **and** `en.json`.

Breakpoint convention: `lg` (1024px) is the sidebar/drawer switch, matching the existing
`lg:grid-cols-[...]` two-column page layouts. `sm` (640px) is the phone/tablet switch for
padding, stacking and stepper density.

---

## 1. Foundation (index.html, index.css, i18n)

**`frontend/index.html`**
- `<meta name="viewport" content="width=device-width, initial-scale=1.0, viewport-fit=cover" />`
  so `env(safe-area-inset-*)` becomes available on notched iPhones.

**`frontend/src/index.css`**
- `body { min-height: 100dvh; }` (was `100vh`), add `overscroll-behavior-y: none`,
  `-webkit-tap-highlight-color: transparent`, `-webkit-text-size-adjust: 100%`.
- Add a coarse-pointer rule so inputs never trigger iOS zoom without changing the desktop look:
  ```css
  @media (pointer: coarse) {
    :where(input, select, textarea) { font-size: max(16px, 1em); }
  }
  ```
- Add safe-area utilities used by the top bar, drawer footer and bottom-sheet dialogs:
  `.pt-safe`, `.pb-safe`, `.pl-safe`, `.pr-safe` (`padding-*: env(safe-area-inset-*)`).
- Add `.no-scrollbar` (hide scrollbar on the horizontally scrolling filter strip).

**`frontend/src/i18n/locales/{de,en}.json`** – new keys:
`nav.openMenu` ("Menü öffnen" / "Open menu"), `nav.closeMenu`, `common.scrollHint`
("Zum Scrollen wischen" / "Swipe to scroll", optional), `admin.desktopOnlyHint`
("Der Layout-Editor ist für Desktop-Bildschirme ausgelegt." / "The layout editor is designed for
desktop screens."), `booking.stepOf` ("Schritt {{current}} von {{total}}" / "Step {{current}} of
{{total}}").

**`frontend/vite.config.ts`** – no manifest change. Do not lock `orientation` (tablets).

## 2. AppLayout: top bar + drawer (`frontend/src/components/AppLayout.tsx`)

Keep the single `<aside>` (E2E `auth.spec.ts:86` selects `aside span:has-text("Admin")`) and make
it responsive instead of rendering two navs:

- State `const [open, setOpen] = useState(false)`. Close on route change
  (`useLocation()` effect), on Escape, on backdrop tap, and on any `NavLink` click.
- Body scroll lock while open (`document.body.style.overflow = 'hidden'` in an effect).
- Outer shell: `flex h-dvh` (was `h-screen`). Add `flex-col lg:flex-row`.
- **Top bar** (new, `lg:hidden`): `sticky top-0 z-30 flex h-14 items-center gap-3 bg-ink-900
  px-3 pt-safe text-white`. Contents: hamburger `<button aria-label={t('nav.openMenu')}
  aria-expanded={open} aria-controls="app-sidebar">` with `h-11 w-11` hit area; the existing
  LE brand block; `LanguageToggle variant="dark"` pushed right with `ml-auto`.
- **Aside**: `id="app-sidebar"` and
  `fixed inset-y-0 left-0 z-50 w-64 max-w-[85vw] -translate-x-full transition-transform
  duration-200 lg:static lg:translate-x-0 lg:z-auto` plus `translate-x-0` when `open`.
  `aria-hidden={!open}` below `lg` only (use a `hidden lg:block` sibling approach if
  `aria-hidden` toggling is awkward; simplest is `inert` on the aside when closed on mobile).
  Add `pb-safe` to the user section so "Abmelden" clears the home indicator.
  Add a close button (X, `h-11 w-11`, `lg:hidden`) in the brand row; hide the in-aside
  `LanguageToggle` below `lg` (`hidden lg:flex`) since the top bar has it.
- **Backdrop**: `{open && <div className="fixed inset-0 z-40 bg-ink-900/55 lg:hidden"
  onClick={() => setOpen(false)} />}`.
- Nav links: `py-2` → `py-2.5 min-h-11` (44px touch target). Profile/Sign-out buttons:
  `py-1.5` → `py-2.5`.
- Main: `<main className="flex-1 overflow-auto scroll-thin min-w-0">` and inner
  `px-4 py-5 sm:px-6 sm:py-7 lg:px-10 lg:py-9 max-w-[1400px] mx-auto`.
- Focus: on open, focus the first link in the drawer; on close, return focus to the hamburger.

`frontend/src/components/LanguageToggle.tsx`: bump buttons to `min-h-9 px-2.5 text-[12px]`
(both variants) so the toggle is tappable; visually still compact.

## 3. Shared components

**`CalendarPicker.tsx`** (used by BookingPage and BlockedDaysPage)
- Card: `w-full max-w-sm ... p-5` → `w-full sm:max-w-sm p-3 sm:p-5`.
- Day cells: `h-10` → `h-11`, keep `grid-cols-7 gap-1`; availability dot `h-1 w-1` → `h-1.5 w-1.5`.
- Legend row: drop `ml-auto` on the last item below `sm` (`sm:ml-auto`) so wrapping is predictable.

**Dialogs** – `ConfirmDialog.tsx`, the local `Modal` in `Admin/LocationsPage.tsx:~320` and
`Admin/SlotsPage.tsx:~289`, and the inline modal in `Admin/BlockedDaysPage.tsx:~167`.
Extract the duplicated `Modal` into `frontend/src/components/Modal.tsx` (same props: `title`,
`onClose`, `children`) and use it from LocationsPage, SlotsPage and BlockedDaysPage; have
`ConfirmDialog` share the same wrapper classes:
- Overlay: `fixed inset-0 z-50 flex items-end sm:items-center justify-center p-0 sm:p-4`.
- Panel: `w-full sm:max-w-md rounded-t-card sm:rounded-card max-h-[90dvh] overflow-y-auto pb-safe`.
- Footer button row: `flex flex-col-reverse gap-2 sm:flex-row sm:justify-end`; buttons
  `w-full sm:w-auto min-h-11`.
- Close on Escape (`keydown` listener) and lock body scroll while mounted (same helper as the
  drawer; put it in `frontend/src/hooks/useBodyScrollLock.ts`).

**`LocationSelector.tsx`, `TimeSlotSelector.tsx`**: verify option cards have `min-h-11` and the
grid stacks to one column below `sm` (they already carry one responsive prefix each; adjust only
if a fixed column count remains).

## 4. User-facing pages

**`pages/Dashboard/DashboardPage.tsx`**
- Booking row (`flex items-center gap-4 p-4`, ~line 170): `gap-3 p-3 sm:gap-4 sm:p-4`, text
  block `min-w-0`, location/status row `flex-wrap`.
- Deadline pill (~line 194, currently `hidden sm:block`): render it on mobile too, as a second
  line under the location (`sm:hidden` copy inside the text block) so "confirm by HH:MM" is
  visible on phones. Same pattern for the `hidden sm:block` divider if it crowds.

**`pages/Booking/BookingPage.tsx`**
- Stepper (~line 341): below `sm` show a compact bar: `t('booking.stepOf', {current, total})`
  plus the current step label and a 4-segment progress track; keep the pill row `hidden sm:flex`.
- Result cards (~lines 196/250): `p-4 sm:p-6`.
- `SummaryRow` (~line 563): `flex flex-col gap-0.5 sm:flex-row sm:items-center
  sm:justify-between`, value `break-words`.
- Week-mode toggle row (~line 413): ensure `flex-wrap gap-2` and the label has `min-h-11`.
- Step navigation buttons (back/next/confirm): `w-full sm:w-auto min-h-11` and stack
  `flex-col-reverse sm:flex-row` below `sm`.
- `ParkingGridView` already scrolls; add a small `common.scrollHint` caption `sm:hidden` above it.

**`pages/MyBookings/MyBookingsPage.tsx`**
- Filter chips (~line 151): make the container a horizontal scroll strip on mobile:
  `flex flex-nowrap overflow-x-auto no-scrollbar snap-x -mx-4 px-4 sm:mx-0 sm:px-1 sm:flex-wrap
  sm:overflow-visible sm:inline-flex`; chips `shrink-0 snap-start min-h-9`.
- Booking row (~line 189): `p-4 sm:p-5`, text block `min-w-0`.
- Action cluster (~line 225): `flex flex-wrap items-center gap-2 w-full sm:w-auto`; buttons
  `min-h-10 flex-1 sm:flex-none`.
- Pagination (~line 265): `flex-wrap`, buttons `min-h-10`.

**`pages/Profile/ProfilePage.tsx`**
- Spacer `<div />` (~line 229): `hidden sm:block`.
- Push section (~line 292): buttons cluster `w-full sm:w-auto sm:ml-auto`, buttons `min-h-10`.
- Danger zone (~line 350): `flex flex-col gap-4 sm:flex-row sm:items-start`; delete button
  `w-full sm:w-auto min-h-11`.
- Save button row: `w-full sm:w-auto`.

**`pages/Login/LoginPage.tsx`, `RegisterPage.tsx`, `VerifyEmailPage.tsx`** (class-only edits)
- The coarse-pointer 16px rule from section 1 fixes iOS zoom; additionally change inputs from
  `text-[14px]` to `text-base sm:text-[14px]` for consistency.
- Login right panel: `px-6` → `px-5 sm:px-8 lg:px-12`, add `pt-safe` to the container;
  `LanguageToggle` wrapper `absolute right-4 top-4 sm:right-6 sm:top-6` with the bigger hit area.
- Primary buttons `min-h-11`.

**`pages/Privacy/PrivacyPage.tsx`**: `px-6 py-12` → `px-4 py-8 sm:px-6 sm:py-12`.

## 5. Admin pages (usable, not redesigned)

Apply one pattern to `Admin/{BookingsPage,LocationsPage,SlotsPage,LotteryHistoryPage,UsersPage,
ApprovalsPage}.tsx`:
- Table card wrapper `mt-6 overflow-hidden rounded-card ...` → `mt-6 overflow-x-auto rounded-card ...`
  (keep the rounded clipping via `rounded-card` on the wrapper; `overflow-x-auto` still clips
  the corners). Add `min-w-[640px]` on the `<table>` so columns don't crush; whitespace-nowrap
  on the status/actions cells.
- Fixed-width controls `w-72` / `w-64` (SlotsPage ~124, BlockedDaysPage ~111, LotteryHistoryPage
  ~87, UsersPage ~137, GridLayoutPage ~258) → `w-full sm:w-72` (resp. `sm:w-64`).
- Header rows `flex items-start justify-between` (LocationsPage ~97 and equivalents) →
  `flex flex-wrap items-start justify-between gap-3`; primary action button `w-full sm:w-auto`.
- Filter bar `BookingsPage.tsx:~72`: controls `w-full sm:w-auto` inside the existing `flex-wrap`.
- `BlockedDaysPage.tsx:~129` list `max-h-[28rem]` → `max-h-[60dvh] lg:max-h-[28rem]`.
- `GridLayoutPage.tsx`: add a `lg:hidden` info banner with `t('admin.desktopOnlyHint')` above
  the editor; leave the editor rendered (it already scrolls). Replace the `prompt()` call only
  if trivially possible; otherwise out of scope.

## 6. Tests

**Vitest** – new `frontend/src/components/AppLayout.test.tsx` using `renderWithRouter` and
`createMockAuth` from `src/test/helpers.tsx`:
- hamburger has the `nav.openMenu` label and `aria-expanded=false`; clicking sets `true` and
  makes the aside `translate-x-0`;
- clicking a nav link closes the drawer (assert `aria-expanded` back to `false`);
- Escape closes it;
- `document.body.style.overflow` is `hidden` while open and restored after.
Also a `Modal.test.tsx` for Escape-to-close on the extracted modal.

**Playwright** – `e2e/playwright.config.ts`:
```ts
projects: [
  { name: 'chromium', use: { browserName: 'chromium' }, testIgnore: /mobile\// },
  { name: 'mobile-chrome', use: { ...devices['Pixel 7'] }, testMatch: /mobile\// },
],
```
Pixel 7 emulation runs on Chromium, so the existing `npx playwright install --with-deps chromium`
in `.github/workflows/ci.yml` (job `e2e`) needs no change. Existing desktop specs are untouched
because the desktop project ignores the new folder and the sidebar is unchanged at 1280px.

New `e2e/tests/mobile/smoke.spec.ts` (reuse the login helper pattern from `tests/auth.spec.ts`):
1. Login page renders with no horizontal overflow
   (`document.documentElement.scrollWidth <= window.innerWidth`).
2. After login the sidebar links are not visible, the hamburger is; tapping it shows
   "Meine Buchungen"/"My bookings"; tapping the link navigates and closes the drawer.
3. `/booking` step 1 and `/my-bookings` have no horizontal overflow; the filter strip is
   scrollable rather than wrapped (`scrollWidth > clientWidth` on the strip).
4. Admin `/admin/bookings`: the table wrapper scrolls horizontally, the page does not.

## 7. Verification

No node on the host; run everything through Docker from the repo root:

```bash
# lint, unit tests, production build
docker run --rm -v "$PWD/frontend:/app" -w /app node:22-alpine \
  sh -c "npm run lint && npx vitest run && npm run build"
```

Visual check at 360×780 and 390×844: run the dev stack
(`COMPOSE_FILE=docker-compose.yml:docker-compose.dev.yml docker compose up --build` is **not**
safe on this box because it is the prod checkout; instead use the `run` skill or Playwright
headed mode with `devices['Pixel 7']` and take screenshots of `/login`, `/`, `/booking`,
`/my-bookings`, `/profile`, `/admin/bookings`). Checklist per screen:
- no horizontal page scroll; 16px side gutter; header not under the status bar;
- drawer opens/closes, closes on navigation, focus returns to the hamburger;
- calendar cells ≥ 44px; every button/link ≥ 44px tall;
- input focus does not zoom on iOS Safari (check on a real iPhone or Safari responsive mode);
- desktop at 1280px looks identical to before (sidebar visible, no top bar).

E2E: `cd e2e && npx playwright test --project=mobile-chrome` against a running stack, then the
full suite to confirm the desktop specs still pass. CI on push to master runs both projects.

## Suggested commit split

1. Foundation + AppLayout drawer + LanguageToggle + i18n keys + AppLayout test.
2. Shared components (CalendarPicker, Modal extraction, ConfirmDialog, scroll-lock hook).
3. User-facing pages (Dashboard, Booking, MyBookings, Profile, Login/Register/Privacy classes).
4. Admin scroll/width fixes + GridLayout notice.
5. Playwright mobile project + smoke spec.

---

## Status (2026-09-21)

Implemented in the working tree, all sections 1–6. Verified on the prod box via
`node:22-alpine`: `npm run lint` (0 errors), `vitest run` (11 files, 51 tests incl. the new
`AppLayout.test.tsx` and `Modal.test.tsx`), `npm run build` (compiled CSS contains the drawer,
safe-area, bottom-sheet and `pointer: coarse` rules).

Not verified here: browser rendering and the new `mobile-chrome` Playwright project. The box has
~2 GB free on `/`, so the Playwright image cannot be pulled, and starting a local compose stack is
blocked on the production host. Both run in GitHub CI on push (`e2e` job runs all projects).
Also still worth a manual pass on a real iPhone: input focus must not zoom, the top bar must sit
below the status bar in standalone (PWA) mode.

Deviations from the plan: dialog helpers live in `components/modalChrome.ts` (kept out of
`Modal.tsx` to satisfy the react-refresh lint rule); a `useMediaQuery` hook was added so the
off-canvas drawer can be `inert` only below `lg`.
