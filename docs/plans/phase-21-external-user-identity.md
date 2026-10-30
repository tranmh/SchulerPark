# Phase 21 — External users: first name, surname and company for admin approval

Date: 2026-10-30
Status: implemented 2026-10-30 (see Deviations at the end)

## Context

External (non-Schuler) people will use LouisE. Phase 18 already built the gate: an email domain that is neither auto-approved (`Registration:AutoApprovedDomains`) nor SSO-only registers as `ApprovalStatus.Pending`, admins receive a mail once the address is verified, and `/admin/approvals` offers Approve/Reject (`UserApprovalController`, `ApprovalsPage.tsx`).

What is missing is the information the admin needs to make that decision. `User` has only a free-text `DisplayName`; there is no surname and no company anywhere in the system. The approvals table and the admin mail show "Name / E-Mail" only, so an admin cannot tell whether "M. Müller" is the expected visitor from company X.

Outcome: every local registration captures **first name, surname and company**; the approvals page, the approval-request mail and the SuperAdmin users page show them; the company is read-only for the user after registration.

Decisions already taken (2026-10-30):
- Company is **required for every local registration**, including auto-approved domains. SSO users have no company (shown as "Internal / SSO").
- Users **cannot edit their company**; SuperAdmin can (Users page). First name / surname stay user-editable.
- Existing users: **no backfill**. Name fields stay null; UI falls back to `displayName`. Users fill them in the next time they save their profile (fields required there).


## Data model

`backend/SchulerPark.Core/Entities/User.cs` — add:
```csharp
public string? FirstName { get; set; }   // max 100
public string? LastName  { get; set; }   // max 100
public string? Company   { get; set; }   // max 200; null for SSO + legacy users
```
`DisplayName` stays (used by JWT `name` claim, all email greetings, Azure AD provisioning). Rule: whenever first/last name are set, `DisplayName = $"{FirstName} {LastName}".Trim()`. Put this in one helper, `User.SetName(string first, string last)` on the entity (Core has no dependencies, fine), and call it from register + profile update.

`backend/SchulerPark.Infrastructure/Data/Configurations/UserConfiguration.cs` — `HasMaxLength(100)` for the two name fields, `HasMaxLength(200)` for `Company`.

Migration (hand-written, pattern of `20260921113000_AddUserPreferredLanguage.cs`, `[DbContext]`+`[Migration]` attributes, no Designer file):
`backend/SchulerPark.Infrastructure/Data/Migrations/20261030090000_AddUserNameAndCompany.cs` — three nullable `character varying` columns on `Users`; `Down` drops them. Update `AppDbContextModelSnapshot.cs` User block (lines ~419-532) by hand, alphabetically: `Company` after `CarLicensePlate`, `FirstName` after `EmailVerified...`, `LastName` before `LockoutEnd`.

## Backend

### Registration
- `Api/DTOs/Auth/RegisterRequest.cs`: replace `DisplayName` with
  `[Required, MinLength(1), MaxLength(100)] string FirstName`, same for `LastName`, `[Required, MinLength(1), MaxLength(200)] string Company`. Trim all three.
- `AuthService.RegisterAsync` (`Infrastructure/Services/AuthService.cs:63-108`) + `IAuthService` signature: take first/last/company, set `FirstName/LastName/Company`, derive `DisplayName` via `SetName`. The response stays identical for all cases (no enumeration change). Note: when the email already exists, nothing is updated (existing behaviour).
- Azure AD path (`LoginWithAzureAdAsync`, lines 205-279): unchanged. New SSO users keep `DisplayName` from the token; `Company` stays null. If the token carries `given_name`/`family_name`, fill `FirstName/LastName` when they are null (cheap, optional; `AzureAdUserInfo` already parses claims — check the record and add the two fields only if trivial).

### Approval surface
- `Api/DTOs/Admin/PendingUserDto.cs`: add `string? FirstName, string? LastName, string? Company`.
- `UserApprovalController.ListPending` + `Decide` (lines 42, 79): project the new fields. Order unchanged.
- `Core/Interfaces/IEmailService.cs:21` + `EmailService.SendApprovalRequestToAdminAsync` (line 393): add `string? pendingUserCompany` parameter; add a `Firma / Company` row to the details table (omit the row when null). Keep "Name" row showing `DisplayName` (= "First Last").
- `AdminNotifier.PendingUserAsync` (`Infrastructure/Services/AdminNotifier.cs:37-45`): pass `pendingUser.Company`.
- `Tests/Integration/Fakes.cs:63` `CapturingEmailService`: update signature.

### SuperAdmin users list
- `Api/DTOs/Admin/AdminUserDto.cs`: add `FirstName, LastName, Company, ApprovalStatus` (string). `ApprovalStatus` lets the Users page show a "Pending/Rejected" chip — small, and it already exists on the entity.
- `UsersAdminController.GET` (lines 31-72): include new fields in the projection; extend `search` to match `Company`, `FirstName`, `LastName` (same `ToLower().Contains` pattern as lines 40-45).
- New `PUT api/admin/users/{id}/company` (SuperAdminOnly, same controller) with `UpdateUserCompanyRequest([MaxLength(200)] string? Company)` — null/blank clears. Returns `AdminUserDto`. This is the only way to change a company after registration.

### Profile
- `Api/DTOs/Profile/UpdateProfileRequest.cs`: replace `DisplayName` with required `FirstName`, `LastName` (max 100). No company (read-only for users).
- `ProfileController.UpdateProfile` (line 75): `user.SetName(request.FirstName, request.LastName)` instead of assigning `DisplayName`.
- `Api/DTOs/Auth/UserDto.cs`: add `FirstName, LastName, Company` (nullable) so the frontend can prefill and display.
- `Api/DTOs/Profile/DataExportDto.cs` + `ProfileController` export (line 158): include the three fields (DSGVO export must contain all personal data).
- `DataRetentionJob` / hard delete: nothing to do, columns are on the row.

### Seed / bootstrap
- `Infrastructure/Data/Seed/SeedData.cs`: set `FirstName/LastName` (split of the existing fixed names) and `Company = "Schuler"` for the `@schuler.de` dev users; leave admins' company null. Not required for correctness but makes the dev Users page look right. `BootstrapAdmin` unchanged.

## Frontend

### Types and services
- `src/types/auth.ts`: `User` gets `firstName: string | null; lastName: string | null; company: string | null`. `RegisterRequest` becomes `{ email, firstName, lastName, company, password, preferredLanguage? }`.
- `src/types/profile.ts` `UpdateProfileRequest`: `firstName`, `lastName` instead of `displayName`; `DataExport.profile` gains the three fields.
- `src/services/adminUsersService.ts`: `AdminUser` and `PendingUser` gain `firstName`, `lastName`, `company` (and `AdminUser.approvalStatus`); add `updateCompany(id, company)` → `PUT /admin/users/{id}/company`.
- `src/contexts/AuthContext.tsx:483-490` `register(...)`: new signature `register({ email, firstName, lastName, company, password })`.
- Add `src/utils/userName.ts`: `fullName(u: { firstName?, lastName?, displayName })` → "First Last" or fallback `displayName`. Use it in ApprovalsPage, UsersPage and the sidebar user block in `AppLayout.tsx`.

### Register page (`src/pages/Login/RegisterPage.tsx`)
- Replace the single "Display name" input with **First name** / **Surname** (side by side on ≥sm, stacked on mobile, `autoComplete="given-name"` / `"family-name"`) and **Company** (`autoComplete="organization"`), all required, trimmed, max lengths as backend.
- Move the existing `auth.registerApprovalNote` below the company field and reword it: external addresses are checked by an administrator, who sees name and company, so please use your real name and employer. New keys `auth.firstName`, `auth.lastName`, `auth.company`, `auth.companyHint`.
- No success-flow change (`verifyEmailSentTitle` card stays).

### Approvals page (`src/pages/Admin/ApprovalsPage.tsx`)
- "User" cell: bold `fullName(u)`, email below (as today).
- New column **Company** (`approvals.company`) between User and Registered; show `u.company ?? '—'`.
- Reject confirm text: include name and company (`approvals.rejectConfirm` → `{{name}} ({{company}}) — {{email}}`; keep `{{email}}` param so existing translations still work).
- Widen `min-w` to ~760px.

### Users page (`src/pages/Admin/UsersPage.tsx`, SuperAdmin)
- User cell: `fullName(u)` + email. New **Company** column: text, with a small inline pencil → prompt-style edit (reuse the `busyId` pattern; `window.prompt` is acceptable here, matching the page's `window.confirm` style) calling `adminUsersService.updateCompany`. Show "Internal (SSO)" muted text when `hasAzureAd && !company`.
- Status chip: add an amber "Pending" / rose "Rejected" variant when `approvalStatus !== 'Approved'` (same chip markup as lines 226-236).
- Search placeholder → "Search name, email, company". While touching the page, move its hardcoded strings into `admin.usersPage.*` keys (title already localised); keep this to the strings on the page, no behaviour change.

### Profile page (`src/pages/Profile/ProfilePage.tsx`)
- Replace the "Display name" input (lines 258-268) with First name / Surname inputs, prefilled from `user.firstName/lastName`; if both null, prefill first name with `displayName` split at the last space (frontend suggestion only, user can correct). Both required; Save disabled when either is empty.
- Add read-only **Company** row (disabled input like email, helper text `profile.companyHelper`: "Set at registration. Contact an administrator to change it."). Hide the row for SSO users with no company.
- `handleSave` sends `firstName`, `lastName` (no `displayName`); `updateUser(updated)` as today.

### Locale files (`src/i18n/locales/de.json`, `en.json`, keep line-aligned)
New keys: `auth.firstName/lastName/company/companyHint`, reworded `auth.registerApprovalNote`, `approvals.company`, `approvals.rejectConfirm`, `profile.labelFirstName/labelLastName/labelCompany/companyHelper`, `admin.usersPage.*` (search, role, allRoles, active, disabled, pending, rejected, enable, disable, delete, deleteConfirm, empty, company, internalSso, editCompany, companyPrompt). German first in meaning; e.g. `Vorname`, `Nachname`, `Firma`.

### Docs
- `docs/UserManual.md`: registration section — list the three fields and explain that an admin checks name and company. Regenerate `UserManual.pdf` the same way it was produced before (check git history / scripts for the command; if none, note it in the commit and leave the PDF).
- `CLAUDE.md`: one line under Key Endpoints for `PUT /api/admin/users/{id}/company`; mention name/company fields in the registration bullet.

## Tests

Backend (`backend/SchulerPark.Tests`, xUnit, in-memory DB, users created through the API):
- `AuthTestHelper.RegisterAsync` → send `firstName`, `lastName`, `company`; add overload with custom values. All existing register call sites compile against it.
- `AuthTests`: register without company / without surname → 400; register sets `DisplayName == "First Last"`; `GET /api/profile` returns the three fields.
- `UserApprovalTests`: `GET /api/admin/users/pending` returns `firstName/lastName/company`; approval-request mail receives the company (extend `CapturingEmailService.ApprovalMails` tuple with `Company` or add a separate queue, then assert).
- `UsersAdminTests`: list exposes company + approvalStatus; `search=` matches company; `PUT {id}/company` works for SuperAdmin, 403 for Admin, clears on blank.
- Profile: update with first/last recomputes `DisplayName`; company in body (if someone sends it) is ignored; data export contains the fields.

Frontend (`npm run lint && npm test`): update `RegisterPage` / `ProfilePage` / `AppLayout` unit tests that reference "Display name"; add a `fullName` util test.

E2E (`e2e/tests/auth.spec.ts`): registration via API and UI now sends/fills first name, surname, company (`getByLabel(/first name/i)` etc.). Add `e2e/tests/admin/approvals.spec.ts`: register an `@external.example` user, verify via MailHog, admin sees name + company on `/admin/approvals`, approve, user logs in. Needs `AdminApi` helper methods `pendingUsers()` / `decide()` in `e2e/helpers/api.ts`.

## Verification

1. `cd backend && dotnet build && dotnet test` — green.
2. `cd frontend && npm run lint && npm test && npm run build` — green.
3. Dev stack: `COMPOSE_FILE=docker-compose.yml:docker-compose.dev.yml docker compose up --build`. Migration applies on start (check app log for `AddUserNameAndCompany`).
4. Manual: register `visitor@external.example` with name "Erika Muster", company "Muster GmbH" → MailHog shows verification mail → verify → login shows "awaiting approval" → MailHog shows admin mail with Name / E-Mail / Firma rows → `/admin/approvals` as `admin@schulerpark.local` lists "Erika Muster · Muster GmbH" → Approve → user logs in → profile shows Vorname/Nachname editable, Firma read-only.
5. `/admin/users` as superadmin: company column, search "Muster" finds the user, edit company works, legacy users show display name and empty company.
6. `cd e2e && npx playwright test` — green.

## Suggested commit split
1. Data model + migration + snapshot + registration/profile/DTO changes + backend tests.
2. Approval mail and admin lists (backend) + frontend register/approvals/users/profile + locales + frontend tests.
3. E2E tests + docs (UserManual, CLAUDE.md, plan file).

## Deviations from the plan (as implemented)
- No `fullName()` frontend util: `displayName` is already derived server-side as "First Last" (legacy rows keep their old text), so every list simply shows `displayName`.
- Azure AD `given_name`/`family_name` were not mapped (optional item); SSO users keep `DisplayName` from the token and null name/company fields.
- `UserManual.pdf` regenerated from `UserManual.md` with Playwright Chromium (the same renderer as before: a small Markdown-to-HTML script printed via `page.pdf`, A4); no pandoc/LibreOffice on this machine.
- Users page strings now live under `admin.usersPage.*` (localised en/de) and errors go through `describeApiError`.
- **Bug fixed on the way (pre-existing, Phase 19):** `AuthProvider` wrapped the tree in `<MsalProvider>` once the auth config arrived (Azure AD enabled), remounting every page. `/verify-email` then posted its single-use token twice and showed "Verification failed" although the account was verified. Nothing consumes the MSAL React context, so the wrapper was removed; a regression test in `AuthContext.test.tsx` counts mounts.
- **E2E hygiene:** `getByLabel('Password')` matched both the input and the "Show password" toggle since that toggle was added; all password locators now use `exact: true` (helper + specs). New `e2e/tests/admin/approvals.spec.ts`; `AdminApi.pendingUsers()/decideUser()`.
- Known E2E failures unrelated to this phase, seen locally on 2026-10-30: booking wizard specs book "tomorrow at Goeppingen", which the dev seed blocks; preferred-location API specs collide with bookings left by earlier runs; locations/slots "deactivate" specs do not confirm the Phase 20 capacity dialog.
