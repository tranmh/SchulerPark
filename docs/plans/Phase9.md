# Phase 9: DSGVO Compliance & Data Retention

## Context
German GDPR (DSGVO) requires: data minimization (delete old data), right of access (Art. 15 data export), right to erasure (Art. 17 account deletion), and transparency (privacy notice). This phase adds a weekly data retention job, user profile management, data export, account deletion, and a privacy notice page.

---

## Key Design Decisions

- **Data retention**: Weekly Hangfire job deletes Bookings and LotteryHistory older than 1 year. LotteryRun aggregates are kept (no personal data — just counts).
- **User deletion**: Soft-delete approach — set `DeletedAt` on User, anonymize their bookings (set UserId to a sentinel "deleted user" ID), revoke all refresh tokens. A separate cleanup pass in the retention job hard-deletes users that have been soft-deleted for 30+ days.
- **Data export**: JSON download containing user profile, all bookings, and lottery history. Generated on-demand, not stored.
- **Privacy notice**: Static endpoint returning structured DSGVO info. Frontend renders it as a page.
- **DB migration needed**: Add `DeletedAt` nullable field to User entity.

---

## Implementation Plan

### Backend

#### Step 1: User Entity — Add DeletedAt

**Modify:** `Core/Entities/User.cs`
- Add `public DateTime? DeletedAt { get; set; }`

**New migration:** `AddUserDeletedAt`

#### Step 2: Profile DTOs

**New directory:** `Api/DTOs/Profile/`

| File | Fields |
|------|--------|
| `UpdateProfileRequest.cs` | `string DisplayName, string? CarLicensePlate` |
| `DataExportDto.cs` | `UserProfileExport Profile, List<BookingExport> Bookings, List<LotteryHistoryExport> LotteryHistory` (nested records for each) |

#### Step 3: ProfileController

**New:** `Api/Controllers/ProfileController.cs`

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/profile` | Get current user profile (reuse existing UserDto) |
| PUT | `/api/profile` | Update display name and license plate |
| GET | `/api/profile/data-export` | Generate JSON export of all user data |
| DELETE | `/api/profile/data` | Request account deletion (soft-delete) |

#### Step 4: Privacy Page (Static Frontend)

No backend endpoint needed — privacy notice is a static React page with hardcoded text. Accessible without auth.

#### Step 5: DataRetentionJob

**New:** `Infrastructure/Jobs/DataRetentionJob.cs`

`ExecuteAsync()`:
1. Delete Bookings where `CreatedAt < 1 year ago`
2. Delete LotteryHistory where `Date < 1 year ago`
3. Hard-delete Users where `DeletedAt != null AND DeletedAt < 30 days ago` (plus their remaining data)
4. Log counts of deleted records

LotteryRun records are kept (aggregate stats, no personal data).

#### Step 6: Register DataRetentionJob in Program.cs

**Modify:** `Api/Program.cs`
- Add recurring job: weekly, Sunday 2 AM Europe/Berlin
- `"0 2 * * 0"`

#### Step 7: EF Migration

Generate migration for the `DeletedAt` column on Users table.

### Frontend

#### Step 9: Profile Service + Types

**New:** `frontend/src/services/profileService.ts`
**New:** `frontend/src/types/profile.ts`

#### Step 10: Profile Page

**New:** `frontend/src/pages/Profile/ProfilePage.tsx`
- Display name and license plate edit form
- "Download My Data" button (triggers GET /api/profile/data-export, downloads JSON)
- "Delete My Account" button with confirmation dialog (warns: irreversible, 30-day grace period)

#### Step 11: Privacy Page

**New:** `frontend/src/pages/Privacy/PrivacyPage.tsx`
- Renders privacy notice content from `/api/privacy`
- Accessible without auth (public page)

#### Step 12: Update AppLayout + Routes

**Modify:** `AppLayout.tsx` — add Profile link to user section (bottom of sidebar)
**Modify:** `App.tsx` — add `/profile` (protected) and `/privacy` (public) routes

---

## Decisions

- **Deletion grace period**: 30 days — soft-delete immediately (can't log in), hard-delete via retention job after 30 days
- **Privacy page**: Static frontend page with hardcoded text (no backend API endpoint)
- **Data export**: Direct JSON download from GET /api/profile/data-export (no email, no stored file)
- **Save plan**: docs/plans/Phase9.md

---

## Files Summary

### Backend
| Action | File |
|--------|------|
| MODIFY | `Core/Entities/User.cs` — add DeletedAt |
| NEW | `Api/DTOs/Profile/UpdateProfileRequest.cs` |
| NEW | `Api/DTOs/Profile/DataExportDto.cs` |
| NEW | `Api/Controllers/ProfileController.cs` |
| NEW | `Infrastructure/Jobs/DataRetentionJob.cs` |
| MODIFY | `Api/Program.cs` — register DataRetentionJob |
| NEW | EF migration for DeletedAt |

### Frontend
| Action | File |
|--------|------|
| NEW | `src/services/profileService.ts` |
| NEW | `src/types/profile.ts` |
| NEW | `src/pages/Profile/ProfilePage.tsx` |
| NEW | `src/pages/Privacy/PrivacyPage.tsx` |
| MODIFY | `src/components/AppLayout.tsx` — Profile link |
| MODIFY | `src/App.tsx` — profile and privacy routes |

### Docs
| NEW | `docs/plans/Phase9.md` |

---

## Verification

- [ ] `dotnet build` succeeds
- [ ] `npm run build` succeeds
- [ ] Migration applies cleanly (DeletedAt column added)
- [ ] GET /api/profile returns current user info
- [ ] PUT /api/profile updates display name and license plate
- [ ] GET /api/profile/data-export returns JSON with all user data
- [ ] DELETE /api/profile/data soft-deletes user (sets DeletedAt)
- [ ] DataRetentionJob deletes bookings/history older than 1 year
- [ ] DataRetentionJob hard-deletes soft-deleted users after 30 days
- [ ] Profile page shows edit form, export button, delete button
- [ ] Privacy page renders privacy notice
- [ ] Deleted user cannot log in
