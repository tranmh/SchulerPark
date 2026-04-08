# Phase 10: Polish & Documentation

## Context
Final phase — polish the app for production readiness. Migrate Login/Register pages from inline styles to Tailwind, add a loading spinner component, enhance the exception middleware with TraceId, add frontend form validation, and update documentation (CLAUDE.md, README.md).

---

## Key Design Decisions

- **No FluentValidation library**: The master plan mentions it, but the app has few DTOs and simple validation needs. Adding a NuGet dependency for 5 record DTOs is overkill. Instead, add manual validation in the controller/service layer where it's missing (name length, required fields) — the exception middleware already maps ValidationException to 400.
- **No toast notification library**: Keep inline error/success messages (already working throughout the app). Adding a toast system would require a new dependency and context provider for marginal benefit.
- **No error boundaries**: React error boundaries catch render errors, which are rare in this app. The existing try/catch patterns in each page handle API errors well.
- **Focus on the gaps**: Login/Register Tailwind migration, loading spinner, and documentation are the highest-impact items.

---

## Implementation Plan

### Step 1: Loading Spinner Component

**New:** `frontend/src/components/LoadingSpinner.tsx`

A centered animated spinner with Tailwind, used by ProtectedRoute and anywhere "Loading..." appears.

### Step 2: Update ProtectedRoute

**Modify:** `frontend/src/components/ProtectedRoute.tsx`
- Replace inline styles + "Loading..." text with `<LoadingSpinner />`

### Step 3: Migrate LoginPage to Tailwind

**Modify:** `frontend/src/pages/Login/LoginPage.tsx`
- Replace all `style={{...}}` with Tailwind classes
- Match the visual style of the rest of the app (centered card, proper spacing, consistent buttons)

### Step 4: Migrate RegisterPage to Tailwind

**Modify:** `frontend/src/pages/Login/RegisterPage.tsx`
- Same treatment as LoginPage

### Step 5: Enhance ExceptionHandlingMiddleware

**Modify:** `backend/SchulerPark.Api/Middleware/ExceptionHandlingMiddleware.cs`
- Add `TraceId` from `Activity.Current?.Id ?? context.TraceIdentifier` to ProblemDetails extensions
- Add `Type` URIs for each error category

### Step 6: Add Input Validation to Backend

**Modify:** `backend/SchulerPark.Api/Controllers/AdminController.cs`
- Validate CreateLocationRequest (name/address required, max length)
- Validate CreateSlotRequest (slotNumber required)
- Validate CreateBlockedDayRequest (date not in the past)

**Modify:** `backend/SchulerPark.Api/Controllers/BookingController.cs`
- Validate CreateBookingRequest fields present

These throw `ValidationException` which the middleware already handles.

### Step 7: Update CLAUDE.md

**Modify:** `CLAUDE.md`
- Add key endpoints summary (auth, booking, admin, profile, privacy)
- Add error handling conventions
- Add testing instructions (`dotnet test`)

### Step 8: Update README.md

**Modify:** `README.md`
- Add architecture overview section
- Add default credentials (admin@schulerpark.local / Admin123!)
- Add MailHog info (port 8025)
- Add environment variables documentation
- Add Swagger/Hangfire dashboard info

### Step 9: Save docs/plans/Phase10.md

---

## Decisions

- **Validation**: Manual validation (no FluentValidation library) — simple checks with existing ValidationException pattern
- **Login style**: Centered card on plain background (no sidebar layout for auth pages)
- **Save plan**: docs/plans/Phase10.md

---

## Files Summary

### Frontend (4 new/modified)
| Action | File |
|--------|------|
| NEW | `src/components/LoadingSpinner.tsx` |
| MODIFY | `src/components/ProtectedRoute.tsx` — use LoadingSpinner |
| MODIFY | `src/pages/Login/LoginPage.tsx` — Tailwind migration |
| MODIFY | `src/pages/Login/RegisterPage.tsx` — Tailwind migration |

### Backend (3 modified)
| Action | File |
|--------|------|
| MODIFY | `Api/Middleware/ExceptionHandlingMiddleware.cs` — TraceId, Type URIs |
| MODIFY | `Api/Controllers/AdminController.cs` — input validation |
| MODIFY | `Api/Controllers/BookingController.cs` — input validation |

### Docs (3 modified/new)
| Action | File |
|--------|------|
| MODIFY | `CLAUDE.md` |
| MODIFY | `README.md` |
| NEW | `docs/plans/Phase10.md` |

---

## Verification

- [ ] `dotnet build` succeeds
- [ ] `dotnet test` — all tests pass
- [ ] `npm run build` succeeds
- [ ] Login page renders with Tailwind styling (no inline styles)
- [ ] Register page renders with Tailwind styling
- [ ] ProtectedRoute shows spinner animation while loading
- [ ] Admin create location with empty name → 400 with validation message
- [ ] ProblemDetails responses include TraceId
- [ ] CLAUDE.md has error handling + testing info
- [ ] README.md has architecture, credentials, env vars docs
