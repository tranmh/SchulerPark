# Phase 7: Email Notifications

## Context
Users need email notifications for key booking lifecycle events: booking placed, lottery results (won/lost), confirmation reminders, and cancellations. Emails are sent asynchronously via Hangfire background jobs using MailKit + SMTP. A MailHog container is added for local dev testing.

---

## Key Design Decisions

- **Async delivery via Hangfire**: All emails enqueued as background jobs (`BackgroundJob.Enqueue`). No blocking the request thread. Failures are retried by Hangfire automatically.
- **Graceful degradation**: If SMTP is not configured (Host is empty), email sending is silently skipped. No crash.
- **SmtpSettings POCO**: Follows existing pattern (JwtSettings, AzureAdSettings) in Core/Settings.
- **HTML templates**: Inline C# string interpolation — simple, no template engine dependency. Each email type is a method on EmailService.
- **MailHog for dev**: Added as optional Docker service for local email testing.

---

## Implementation Plan

### Step 1: SmtpSettings POCO

**New:** `Core/Settings/SmtpSettings.cs`
```csharp
public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = "noreply@schulerpark.local";
    public string FromName { get; set; } = "SchulerPark";
    public bool IsConfigured => !string.IsNullOrEmpty(Host);
}
```

### Step 2: IEmailService Interface

**New:** `Core/Interfaces/IEmailService.cs`
```csharp
public interface IEmailService
{
    Task SendBookingCreatedAsync(Booking booking);
    Task SendBookingCancelledAsync(Booking booking);
    Task SendLotteryWonAsync(Booking booking);
    Task SendLotteryLostAsync(Booking booking);
    Task SendConfirmationReminderAsync(Booking booking);
}
```

Each method takes a fully-loaded Booking (with User, Location, ParkingSlot navigations).

### Step 3: EmailService Implementation

**New:** `Infrastructure/Services/EmailService.cs`

- Uses MailKit `SmtpClient` to send emails
- Each method builds an HTML email body with booking details
- `SendEmailAsync(string to, string subject, string htmlBody)` — shared private method
- Checks `SmtpSettings.IsConfigured` before sending; silently returns if not
- Logs email sends at Information level

**Email templates (inline HTML):**

| Method | Subject | Key Content |
|--------|---------|-------------|
| SendBookingCreatedAsync | "Booking Confirmed — {Location} on {Date}" | Location, date, time slot, status = Pending |
| SendBookingCancelledAsync | "Booking Cancelled — {Location} on {Date}" | Confirmation that booking was cancelled |
| SendLotteryWonAsync | "You Won! — {Location} on {Date}" | Assigned slot number, confirmation deadline, confirm link hint |
| SendLotteryLostAsync | "Lottery Result — {Location} on {Date}" | Sorry message, try again next time |
| SendConfirmationReminderAsync | "Reminder: Confirm Your Parking — {Location}" | Deadline approaching, confirm now |

### Step 4: Register EmailService in Program.cs

**Modify:** `Api/Program.cs`
- Bind `SmtpSettings` from configuration
- Register `IEmailService` as scoped

### Step 5: Integrate into BookingController

**Modify:** `Api/Controllers/BookingController.cs`
- After `Create()`: enqueue `SendBookingCreatedAsync` via Hangfire
- After `Cancel()`: enqueue `SendBookingCancelledAsync` via Hangfire

Need to load User navigation for email address. Modify BookingService or load in controller.

### Step 6: Integrate into LotteryService

**Modify:** `Infrastructure/Services/LotteryService.cs`
- After `SaveChangesAsync()` in `RunLotteryForSlotAsync`: for each result, enqueue Won or Lost email
- Need to eager-load User + Location + ParkingSlot for email content

### Step 7: Integrate into ConfirmationExpiryJob

**Modify:** `Infrastructure/Jobs/ConfirmationExpiryJob.cs`
- Before marking as Expired, send a confirmation reminder email
- Eager-load User + Location + ParkingSlot navigations

### Step 8: Wire SMTP env vars in docker-compose.yml

**Modify:** `docker-compose.yml`
- Add SMTP env vars to app service
- Add MailHog service for dev email testing

### Step 9: Save docs/plans/Phase7.md

---

## Decisions

- **DSGVO email**: Deferred to Phase 9 when the data export feature is built
- **Reminder timing**: At expiry time — the expiry job sends a reminder right before marking Expired (single job, simple)
- **MailHog**: Added to docker-compose.yml for dev email testing (UI on port 8025)
- **Save plan**: docs/plans/Phase7.md

---

## Files Summary

| Action | File |
|--------|------|
| NEW | `Core/Settings/SmtpSettings.cs` |
| NEW | `Core/Interfaces/IEmailService.cs` |
| NEW | `Infrastructure/Services/EmailService.cs` |
| MODIFY | `Api/Program.cs` — bind SmtpSettings, register IEmailService |
| MODIFY | `Api/Controllers/BookingController.cs` — enqueue emails on create/cancel |
| MODIFY | `Infrastructure/Services/LotteryService.cs` — enqueue won/lost emails |
| MODIFY | `Infrastructure/Jobs/ConfirmationExpiryJob.cs` — send reminder before expiry |
| MODIFY | `docker-compose.yml` — add SMTP env vars + MailHog service |
| NEW | `docs/plans/Phase7.md` |

---

## Verification

- [ ] `dotnet build` succeeds
- [ ] App starts without SMTP configured (graceful skip)
- [ ] With MailHog: booking creation sends confirmation email
- [ ] With MailHog: booking cancellation sends cancellation email
- [ ] With MailHog: lottery run sends won/lost emails to all participants
- [ ] With MailHog: expiry job sends reminder before marking expired
- [ ] MailHog UI shows all sent emails at http://localhost:8025
- [ ] Emails contain correct booking details (location, date, slot, deadline)
