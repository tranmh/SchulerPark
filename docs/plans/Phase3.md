# Phase 3: Authentication

## Objective
Implement dual-provider authentication (local login + Azure AD SSO) with a single app-issued JWT. Both flows produce the same JWT token. Refresh tokens use httpOnly cookies with rotation and reuse detection.

---

## Architecture: Dual-Provider, Single-Token

```
Local Login:  email/password → validate → issue app JWT + refresh cookie
Azure AD:     MSAL popup → ID token → POST to backend → validate → issue app JWT + refresh cookie
```

Both paths end with the same app JWT. The frontend never handles Azure AD tokens for API calls — only the app JWT.

---

## Key Design Decisions

### User Entity (unchanged from Phase 2)
- Standalone entity, NOT `IdentityUser`. Uses `PasswordHasher<User>` standalone for hashing.
- `UserRole` enum (User/Admin) — no Identity roles tables.

### Refresh Token Strategy
- **Separate `RefreshToken` entity** (not a field on User) — supports multiple devices/sessions.
- **Token rotation:** Each refresh invalidates the old token, issues a new one.
- **Reuse detection:** If a revoked token is presented, revoke ALL tokens for that user (indicates theft).
- **Storage:** 64 random bytes (Base64URL-encoded) sent to client; SHA-256 hash stored in DB.
- **Delivery:** httpOnly Secure SameSite=Strict cookie on path `/api/auth`.

### Access Token Storage
- **In-memory only** (React state). Never localStorage/sessionStorage. XSS cannot steal it.
- On page refresh, the refresh cookie silently re-authenticates.

### Azure AD Mode
- Works in "local-only" mode when Azure AD env vars are empty.
- Frontend fetches `/api/auth/config` to know if Azure AD button should be shown.
- MSAL handles the popup/redirect; backend validates the ID token and issues app JWT.

---

## Steps

### Step 1: Add RefreshToken Entity

**New file:** `Core/Entities/RefreshToken.cs`

```csharp
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;     // SHA-256 hash of actual token
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedByIp { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsRevoked => RevokedAt != null;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsActive => !IsRevoked && !IsExpired;

    public User User { get; set; } = null!;
}
```

**Modify:** `Core/Entities/User.cs` — add navigation:
```csharp
public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
```

---

### Step 2: RefreshToken EF Configuration + DbSet

**New file:** `Infrastructure/Data/Configurations/RefreshTokenConfiguration.cs`

- Table: `RefreshTokens`
- `Token`: required, max 128, indexed for lookup
- `UserId` FK: cascade delete
- Index on `ExpiresAt` for cleanup queries

**Modify:** `Infrastructure/Data/AppDbContext.cs` — add:
```csharp
public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
```

---

### Step 3: Generate Migration

```bash
dotnet ef migrations add AddRefreshTokens \
    --project SchulerPark.Infrastructure \
    --startup-project SchulerPark.Api \
    --output-dir Data/Migrations
```

---

### Step 4: Settings Models in Core

**New file:** `Core/Settings/JwtSettings.cs`
```csharp
public class JwtSettings
{
    public string Secret { get; set; } = string.Empty;       // min 32 bytes for HMAC-SHA256
    public string Issuer { get; set; } = "SchulerPark";
    public string Audience { get; set; } = "SchulerPark";
    public int ExpiryMinutes { get; set; } = 60;
    public int RefreshExpiryDays { get; set; } = 7;
}
```

**New file:** `Core/Settings/AzureAdSettings.cs`
```csharp
public class AzureAdSettings
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public bool IsConfigured => !string.IsNullOrEmpty(TenantId) && !string.IsNullOrEmpty(ClientId);
}
```

These are plain POCOs in Core (zero dependencies).

---

### Step 5: Auth DTOs in Api

**New directory:** `Api/DTOs/Auth/`

| File | Type |
|------|------|
| `LoginRequest.cs` | `record LoginRequest(string Email, string Password)` |
| `RegisterRequest.cs` | `record RegisterRequest(string Email, string DisplayName, string Password)` |
| `AzureAdTokenRequest.cs` | `record AzureAdTokenRequest(string IdToken)` |
| `AuthResponse.cs` | `record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User)` |
| `UserDto.cs` | `record UserDto(Guid Id, string Email, string DisplayName, string? CarLicensePlate, string Role, bool HasAzureAd)` |

Note: No `RefreshToken` in AuthResponse body — delivered via httpOnly cookie only.

---

### Step 6: Service Interfaces in Core

**New file:** `Core/Interfaces/ITokenService.cs`
```csharp
public interface ITokenService
{
    string GenerateAccessToken(User user);
    Task<string> GenerateRefreshTokenAsync(User user, string? ipAddress);
    Task<User?> ValidateRefreshTokenAsync(string token);
    Task RevokeRefreshTokenAsync(string token);
    Task RevokeAllUserTokensAsync(Guid userId);
}
```

**New file:** `Core/Interfaces/IAuthService.cs`
```csharp
public interface IAuthService
{
    Task<(User User, string AccessToken, string RefreshToken)> RegisterAsync(string email, string displayName, string password, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> LoginWithAzureAdAsync(string idToken, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> RefreshAsync(string refreshToken, string? ipAddress);
    Task<User> GetUserAsync(Guid userId);
}
```

---

### Step 7: TokenService Implementation

**New file:** `Infrastructure/Services/TokenService.cs`

**GenerateAccessToken(User user):**
- JWT claims: `sub` (User.Id), `email`, `name`, `role` (maps to ClaimTypes.Role), `jti` (unique ID)
- Signing: HMAC-SHA256 with `JwtSettings.Secret`
- Expiry: `JwtSettings.ExpiryMinutes` (default 60 min)

**GenerateRefreshTokenAsync(User user, string? ipAddress):**
- Generate 64 cryptographically random bytes → Base64URL encode
- Store SHA-256 hash in `RefreshTokens` table
- Return raw token string (for cookie)

**ValidateRefreshTokenAsync(string token):**
- Hash the input, look up in DB
- If found and active → return User (eager load)
- If found but revoked → **reuse detection**: revoke ALL tokens for that user, return null
- If not found or expired → return null

---

### Step 8: AuthService Implementation

**New file:** `Infrastructure/Services/AuthService.cs`

Dependencies: `AppDbContext`, `ITokenService`, `IPasswordHasher<User>`, `IOptions<AzureAdSettings>`

**RegisterAsync:** Validate email uniqueness → hash password → create User → generate tokens
**LoginAsync:** Find by email → verify password → generate tokens
**LoginWithAzureAdAsync:** Validate Azure AD ID token → find-or-create User by AzureAdObjectId → generate tokens
**RefreshAsync:** Validate refresh token → revoke old → generate new pair

**New file:** `Infrastructure/Services/AzureAdTokenValidator.cs`
- Fetches Azure AD OpenID Connect metadata (cached via `ConfigurationManager`)
- Validates ID token signature, issuer, audience, expiry
- Extracts claims: `oid`, `email`/`preferred_username`, `name`
- Returns null when Azure AD is not configured

**Modify:** `Infrastructure/SchulerPark.Infrastructure.csproj` — add:
```xml
<PackageReference Include="Microsoft.Extensions.Identity.Core" Version="10.*" />
```
(Provides `PasswordHasher<T>` without the full Identity framework.)

---

### Step 9: AuthController

**New file:** `Api/Controllers/AuthController.cs`

| Method | Route | Auth | Body | Returns |
|--------|-------|------|------|---------|
| POST | `/api/auth/register` | Anonymous | `RegisterRequest` | `AuthResponse` (201) + refresh cookie |
| POST | `/api/auth/login` | Anonymous | `LoginRequest` | `AuthResponse` (200) + refresh cookie |
| POST | `/api/auth/azure-callback` | Anonymous | `AzureAdTokenRequest` | `AuthResponse` (200) + refresh cookie |
| POST | `/api/auth/refresh` | Anonymous | — (cookie) | `AuthResponse` (200) + new refresh cookie |
| GET | `/api/auth/me` | `[Authorize]` | — | `UserDto` (200) |
| POST | `/api/auth/logout` | `[Authorize]` | — (cookie) | 204 + clear cookie |
| GET | `/api/auth/config` | Anonymous | — | `{ azureAdEnabled, azureAdClientId?, azureAdTenantId? }` |

**Cookie helper** (private method on controller):
```csharp
private void SetRefreshTokenCookie(string token, int expiryDays)
{
    Response.Cookies.Append("refreshToken", token, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        MaxAge = TimeSpan.FromDays(expiryDays)
    });
}
```

---

### Step 10: Configure Program.cs

**Modify:** `Api/Program.cs`

Replace `// TODO Phase 3` comments with:

```csharp
// Bind settings
builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<AzureAdSettings>(builder.Configuration.GetSection("AzureAd"));

// Auth services
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// JWT authentication
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>()!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
});
```

Middleware pipeline (order matters):
```csharp
app.UseAuthentication();
app.UseAuthorization();
```

Add Swagger JWT support (Authorize button):
```csharp
options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { ... });
options.AddSecurityRequirement(new OpenApiSecurityRequirement { ... });
```

---

### Step 11: Update Seed Data

**Modify:** `Infrastructure/Data/Seed/SeedData.cs`

- Accept `IPasswordHasher<User>` from DI
- Hash admin password properly: `passwordHasher.HashPassword(admin, "Admin123!")`
- Replace `"PLACEHOLDER_SET_IN_PHASE3"`

Dev credentials: `admin@schulerpark.local` / `Admin123!`

---

### Step 12: Update appsettings.json

**Modify:** `Api/appsettings.json`
```json
{
  "Jwt": {
    "Secret": "DEVELOPMENT_ONLY_SECRET_CHANGE_IN_PRODUCTION_MIN_32_CHARS!!",
    "Issuer": "SchulerPark",
    "Audience": "SchulerPark",
    "ExpiryMinutes": 60,
    "RefreshExpiryDays": 7
  },
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "",
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

**Modify:** `.env.example` — add `JWT_SECRET`

**Modify:** `docker-compose.yml` — add JWT/AzureAd env vars to app service

---

### Step 13: Frontend Types

**New file:** `frontend/src/types/auth.ts`

```typescript
export interface User {
  id: string;
  email: string;
  displayName: string;
  carLicensePlate: string | null;
  role: 'User' | 'Admin';
  hasAzureAd: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

export interface LoginRequest { email: string; password: string; }
export interface RegisterRequest { email: string; displayName: string; password: string; }

export interface AuthConfig {
  azureAdEnabled: boolean;
  azureAdClientId: string | null;
  azureAdTenantId: string | null;
}
```

---

### Step 14: Frontend Auth Service

**New file:** `frontend/src/services/authService.ts`

Functions calling `/api/auth/*`:
- `login(req)` → POST `/api/auth/login`
- `register(req)` → POST `/api/auth/register`
- `loginWithAzureAd(idToken)` → POST `/api/auth/azure-callback`
- `refresh()` → POST `/api/auth/refresh` (cookie sent automatically)
- `getMe()` → GET `/api/auth/me`
- `logout()` → POST `/api/auth/logout`
- `getAuthConfig()` → GET `/api/auth/config`

---

### Step 15: Axios Interceptors

**Modify:** `frontend/src/services/api.ts`

- Configure `withCredentials: true` (sends cookies)
- **Request interceptor:** Attach `Authorization: Bearer {token}` from module-level variable
- **Response interceptor:** On 401, attempt silent refresh via `/api/auth/refresh`. Queue concurrent requests. On refresh failure, dispatch `auth:logout` event.
- Export `setAccessToken(token)` for AuthContext to call

---

### Step 16: MSAL Configuration

**New file:** `frontend/src/config/msalConfig.ts`

- `createMsalConfig(clientId, tenantId)` — dynamic config (not hardcoded)
- `loginRequest = { scopes: ['openid', 'profile', 'email'] }`
- Cache in `sessionStorage` (more secure than localStorage)

MSAL is initialized after fetching `/api/auth/config` from the backend.

---

### Step 17: AuthContext / AuthProvider

**New file:** `frontend/src/contexts/AuthContext.tsx`

**State:** `user`, `accessToken`, `isAuthenticated`, `isLoading`, `isAdmin`

**Provided functions:** `login()`, `register()`, `loginWithAzureAd()`, `logout()`

**On mount:**
1. Fetch `/api/auth/config` → determine if Azure AD is enabled
2. Attempt silent refresh via `/api/auth/refresh` (cookie sent automatically)
3. If refresh succeeds → store access token in memory, set user
4. If fails → user is not authenticated, show login

**MSAL integration:**
- If Azure AD configured, create `PublicClientApplication` and wrap children in `MsalProvider`
- `loginWithAzureAd()` calls `msalInstance.acquireTokenPopup()` → gets ID token → POSTs to backend

---

### Step 18: Login & Register Pages

**New file:** `frontend/src/pages/Login/LoginPage.tsx`
- Email + password form
- "Sign in with Microsoft" button (only if `authConfig.azureAdEnabled`)
- Error display for invalid credentials
- Link to register

**New file:** `frontend/src/pages/Login/RegisterPage.tsx`
- Email, display name, password, confirm password
- Password validation (min 8 chars)
- Link back to login

---

### Step 19: ProtectedRoute Component

**New file:** `frontend/src/components/ProtectedRoute.tsx`

```typescript
interface Props { children: ReactNode; requireAdmin?: boolean; }
```
- Loading → spinner
- Not authenticated → redirect to `/login`
- `requireAdmin` + not admin → redirect to `/`
- Otherwise → render children

---

### Step 20: Update App.tsx & main.tsx

**Modify:** `frontend/src/App.tsx` — add routes with ProtectedRoute wrappers

**Modify:** `frontend/src/main.tsx` — wrap with `BrowserRouter` + `AuthProvider`

---

## Azure AD End-to-End Flow

```
1. User clicks "Sign in with Microsoft"
2. Frontend: msalInstance.acquireTokenPopup({ scopes: ['openid', 'profile', 'email'] })
3. Azure AD popup → user authenticates → returns ID token
4. Frontend: POST /api/auth/azure-callback { idToken: "eyJ..." }
5. Backend validates ID token (signature, issuer, audience, expiry)
6. Backend extracts: oid (AzureAdObjectId), email, name
7. Find user by AzureAdObjectId — if not found, create (Role=User)
8. Generate app JWT + refresh token → set cookie → return AuthResponse
9. Frontend stores access token in memory → user is authenticated
```

---

## Files Summary

### New Backend Files (15)
| # | File | Purpose |
|---|------|---------|
| 1 | `Core/Entities/RefreshToken.cs` | Refresh token entity |
| 2 | `Core/Interfaces/ITokenService.cs` | Token service interface |
| 3 | `Core/Interfaces/IAuthService.cs` | Auth service interface |
| 4 | `Core/Settings/JwtSettings.cs` | JWT config POCO |
| 5 | `Core/Settings/AzureAdSettings.cs` | Azure AD config POCO |
| 6 | `Api/DTOs/Auth/LoginRequest.cs` | Login DTO |
| 7 | `Api/DTOs/Auth/RegisterRequest.cs` | Register DTO |
| 8 | `Api/DTOs/Auth/AzureAdTokenRequest.cs` | Azure AD callback DTO |
| 9 | `Api/DTOs/Auth/AuthResponse.cs` | Auth response DTO |
| 10 | `Api/DTOs/Auth/UserDto.cs` | User DTO |
| 11 | `Api/Controllers/AuthController.cs` | Auth endpoints |
| 12 | `Infrastructure/Services/TokenService.cs` | JWT + refresh token generation |
| 13 | `Infrastructure/Services/AuthService.cs` | Auth business logic |
| 14 | `Infrastructure/Services/AzureAdTokenValidator.cs` | Azure AD ID token validation |
| 15 | `Infrastructure/Data/Configurations/RefreshTokenConfiguration.cs` | EF config |

### Modified Backend Files (5)
| File | Change |
|------|--------|
| `Core/Entities/User.cs` | Add RefreshTokens navigation |
| `Infrastructure/Data/AppDbContext.cs` | Add RefreshTokens DbSet |
| `Infrastructure/Data/Seed/SeedData.cs` | Real password hash for admin |
| `Infrastructure/SchulerPark.Infrastructure.csproj` | Add Microsoft.Extensions.Identity.Core |
| `Api/Program.cs` | JWT auth, DI, middleware, Swagger auth |

### New Frontend Files (7)
| # | File | Purpose |
|---|------|---------|
| 1 | `src/types/auth.ts` | TypeScript types |
| 2 | `src/services/authService.ts` | API calls for auth |
| 3 | `src/config/msalConfig.ts` | Dynamic MSAL configuration |
| 4 | `src/contexts/AuthContext.tsx` | Auth state + provider |
| 5 | `src/components/ProtectedRoute.tsx` | Route guard |
| 6 | `src/pages/Login/LoginPage.tsx` | Login page |
| 7 | `src/pages/Login/RegisterPage.tsx` | Register page |

### Modified Frontend Files (3)
| File | Change |
|------|--------|
| `src/services/api.ts` | Axios interceptors, withCredentials |
| `src/App.tsx` | Routes with auth |
| `src/main.tsx` | AuthProvider wrapper |

### Modified Config Files (3)
| File | Change |
|------|--------|
| `Api/appsettings.json` | JWT + AzureAd sections |
| `.env.example` | Add JWT_SECRET |
| `docker-compose.yml` | Add JWT/AzureAd env vars |

### Auto-Generated (1 migration)
| File | Purpose |
|------|---------|
| `Infrastructure/Data/Migrations/*_AddRefreshTokens.cs` | RefreshTokens table |

---

## JWT Token Claims

| Claim | Value | Purpose |
|-------|-------|---------|
| `sub` | User.Id (Guid) | User identification |
| `email` | User.Email | Display/lookup |
| `name` | User.DisplayName | Display |
| `role` | "User" or "Admin" | [Authorize] policies |
| `iss` | "SchulerPark" | Validation |
| `aud` | "SchulerPark" | Validation |
| `jti` | new Guid | Unique token ID |
| `exp` | iat + 60 min | Expiry |

---

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| XSS stealing tokens | Access token in memory only, refresh in httpOnly cookie |
| CSRF | SameSite=Strict cookie + Bearer header (double-submit) |
| Token theft | Refresh token rotation + reuse detection (revoke all on reuse) |
| Weak JWT secret | Validate min 32 bytes at startup, fail fast |
| Password security | `PasswordHasher<User>` uses PBKDF2 with 100K+ iterations |
| Azure AD token forgery | Full validation: signature, issuer, audience, expiry via published keys |
| Brute force | Rate limiting on login/register (can add in later phase) |
| Expired token accumulation | Cleanup on each refresh call + periodic job (Phase 5 Hangfire) |

---

## Verification Checklist

### Backend
- [ ] `dotnet build` succeeds
- [ ] Migration applies cleanly
- [ ] Seed creates admin with hashed password (not placeholder)
- [ ] `POST /api/auth/register` → 201 + JWT + refresh cookie
- [ ] `POST /api/auth/login` (correct creds) → 200 + JWT + refresh cookie
- [ ] `POST /api/auth/login` (wrong creds) → 401
- [ ] `GET /api/auth/me` (valid token) → 200 + UserDto
- [ ] `GET /api/auth/me` (no token) → 401
- [ ] `POST /api/auth/refresh` (valid cookie) → 200 + new tokens
- [ ] `POST /api/auth/refresh` (revoked cookie) → 401
- [ ] Token rotation: old refresh token no longer works
- [ ] Admin can access `[Authorize(Policy = "AdminOnly")]` endpoints
- [ ] Regular user gets 403 on admin endpoints
- [ ] `GET /api/auth/config` → returns Azure AD status

### Frontend
- [ ] `npm run build` succeeds
- [ ] Redirects to `/login` when not authenticated
- [ ] Local login flow works end-to-end
- [ ] Auto-refreshes token on 401
- [ ] Azure AD button shown only when configured
- [ ] Protected routes block unauthenticated access
- [ ] Admin routes block non-admin users

---

## Implementation Order

1. RefreshToken entity + config + migration (Steps 1-3)
2. Settings models (Step 4)
3. DTOs (Step 5)
4. Service interfaces (Step 6)
5. TokenService + AuthService + AzureAdTokenValidator (Steps 7-8)
6. AuthController (Step 9)
7. Program.cs configuration (Step 10)
8. Seed data update (Step 11)
9. appsettings + env config (Step 12)
10. **Backend verification with curl/Postman**
11. Frontend types + auth service (Steps 13-14)
12. Axios interceptors (Step 15)
13. MSAL config (Step 16)
14. AuthContext/AuthProvider (Step 17)
15. Login/Register pages (Step 18)
16. ProtectedRoute (Step 19)
17. App.tsx + main.tsx (Step 20)
18. **Full end-to-end verification**

---

## Dependencies on Later Phases

| Set Up Here | Used By |
|-------------|---------|
| JWT auth + [Authorize] | Phase 4 (BookingController), Phase 8 (AdminController) |
| User.Id from ClaimsPrincipal | Phase 4 (booking ownership), Phase 9 (DSGVO export) |
| AdminOnly policy | Phase 8 (admin features) |
| Refresh token cleanup | Phase 5 (Hangfire periodic job) |
| Auth context / ProtectedRoute | Phase 4+ (all frontend pages) |
