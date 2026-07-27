# Phase 18 — SSO Verification + Domain-Gated Registration with Admin Approval

Date: 2026-07-27
Status: planned

## Goals

1. **Enable SSO** — verify the already-configured Azure AD SSO works on the public hostnames.
2. **Auto-approved registration for `@andritz.com`** — corporate addresses self-register as today.
3. **Admin approval for external users** — any other email domain registers into a Pending state and needs an Admin/SuperAdmin to accept before first sign-in.

## 1. SSO (verification only — no code expected)

- Live state (2026-07-27): `/api/auth/config` returns `azureAdEnabled: true`; `AZURE_AD_TENANT_ID` + `AZURE_AD_CLIENT_ID` are set in prod `.env`; the login page shows "Continue with Microsoft". Azure tenant users are auto-created on first SSO login (`AuthService.LoginWithAzureAdAsync`, keyed on the immutable `oid` claim).
- **Risk:** the Azure app registration's redirect URIs predate the public go-live. If `https://louise.schuler.de` (and optionally `https://park.schuler.de`) are missing, Microsoft rejects login with AADSTS50011 (redirect mismatch). Fix is in the Azure portal (app registration → Authentication → SPA redirect URIs), not in this repo.
- Acceptance: a tenant user completes SSO login at `https://louise.schuler.de`.

## 2 + 3. Registration approval workflow

### Data model
- `User.ApprovalStatus` enum: `Approved = 0` (default; all existing rows), `Pending = 1`, `Rejected = 2`.
- `User.ApprovedAt` (DateTime?, UTC), `User.ApprovedByUserId` (Guid?).
- EF migration; no backfill needed beyond the default.

### Configuration
- `Registration:AutoApprovedDomains` — semicolon/comma-separated list, default `andritz.com`. Env-overridable (`Registration__AutoApprovedDomains`). Comparison case-insensitive against the part after the final `@` of the normalized email.

### Registration flow
- Allowlisted domain → `ApprovalStatus = Approved` → existing flow unchanged (verify email, then sign in).
- Other domains → `ApprovalStatus = Pending`; email verification proceeds exactly as today.
- **On successful email verification of a Pending user** (not at registration time — bots that never verify must not spam admins): send a notification email to all active Admin/SuperAdmin users ("external user awaiting approval").
- The HTTP response of `POST /api/auth/register` stays byte-identical for all cases (no account-enumeration regression).

### Login gate
- Order of checks in `LoginAsync`: password → email verified → **approval**.
- `Pending` after correct password + verified email → 403 `{ code: "pending_approval" }` (mirrors the existing `email_not_verified` pattern; only revealed to a caller holding the correct password).
- `Rejected` → generic 401, indistinguishable from wrong credentials.
- Azure AD SSO users **bypass approval** (created `Approved`): membership in the corporate tenant is the trust anchor. Also applies when SSO claims/links an existing local account.

### Admin API (policy `AdminOnly`)
- `GET  /api/admin/users/pending` — list Pending users (id, email, display name, registered/verified timestamps).
- `POST /api/admin/users/{id}/approval` — body `{ "approve": true|false }`.
  - Approve → `Approved`, stamp `ApprovedAt`/`ApprovedByUserId`, email the user ("account approved, you can sign in").
  - Reject → `Rejected`, email the user politely. Account remains (blocks silent re-registration of the same address).
- Guard: only users currently `Pending` can be transitioned; 404/validation error otherwise.

### Frontend
- Login page: handle 403 `pending_approval` with a friendly message (en + de).
- Register page: static note that non-Andritz addresses require admin approval after email verification.
- Admin users page: "Pending approval" section with Approve/Reject actions; hidden when empty.

### Emails (via existing IEmailService; fire-and-forget as elsewhere)
- To admins: new verified external registration awaiting approval (link to admin page).
- To user: approval granted / registration declined.

### Tests
- Domain gating: andritz.com → Approved; others → Pending; case-insensitivity; config override.
- Login gate: Pending → 403 `pending_approval` only with correct password; Rejected → generic 401.
- Transitions: approve/reject stamps + emails; double-approve rejected; admin-notify fires on verification, not registration.
- SSO path stays auto-approved.
- Full backend + frontend suites must pass.

### Deploy
- Standard `deploy.sh` path (migration runs on start; runtime smoke validates it against an ephemeral DB first).

## Decisions log
- Auto-approved domains: **`@andritz.com` only** (user decision, 2026-07-27). `@schuler.de` local registrations therefore need approval; Schuler staff are expected to use SSO (auto-approved via tenant).
- Approval sits **after** email verification in the funnel; admins are only notified about verified addresses.
- Rejected users are kept (status `Rejected`), not deleted, and see only generic login failures.
