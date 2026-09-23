namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchulerPark.Api.Auth;
using SchulerPark.Api.DTOs.Auth;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;

[ApiController]
[Route("api/auth")]
// Bug #48: the strict brute-force limiter is applied per-endpoint below (register/login/
// resend-verification) — NOT class-wide. High-frequency authed endpoints (me/config/refresh/
// logout) must fall to the 300/min global limiter, or an office behind one NAT IP trips 429s.
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly JwtSettings _jwtSettings;
    private readonly AzureAdSettings _azureAdSettings;
    private readonly RegistrationSettings _registrationSettings;

    public AuthController(
        IAuthService authService,
        ITokenService tokenService,
        IOptions<JwtSettings> jwtSettings,
        IOptions<AzureAdSettings> azureAdSettings,
        IOptions<RegistrationSettings> registrationSettings)
    {
        _authService = authService;
        _tokenService = tokenService;
        _jwtSettings = jwtSettings.Value;
        _azureAdSettings = azureAdSettings.Value;
        _registrationSettings = registrationSettings.Value;
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            // Always the same response, whether the address was new, already
            // registered, or already verified — no account enumeration.
            await _authService.RegisterAsync(request.Email, request.DisplayName, request.Password, request.PreferredLanguage);
        }
        catch (SsoOnlyDomainException ex)
        {
            // Purely domain-based (rejected before any account lookup) — not an
            // enumeration oracle.
            return BadRequest(new { error = ex.Message, code = "sso_only_domain" });
        }

        return Ok(new
        {
            message = "If the email address is available, a verification email has been sent. " +
                      "Please confirm it before signing in."
        });
    }

    // Strict limiter: unauthenticated token-guessing surface, and legit clients
    // only ever call it once per registration. azure-callback and refresh stay on
    // the global limiter for the Bug #48 reason above (login/refresh bursts from
    // one office NAT IP); azure-callback additionally requires a signed Azure AD
    // token, so it is not a guessing surface.
    [HttpPost("verify-email")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var verified = await _authService.VerifyEmailAsync(request.Token);

        if (!verified)
            return BadRequest(new { error = "Verification link is invalid or has expired.", code = "verification_link_invalid" });

        return Ok(new { message = "Email verified. You can sign in now." });
    }

    [HttpPost("resend-verification")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
    {
        await _authService.ResendVerificationEmailAsync(request.Email);

        // Same response regardless of whether the account exists or is verified.
        return Ok(new { message = "If the address has an unverified account, a new verification email has been sent." });
    }

    // ── Phase 20 WP2: password self-service ──

    /// <summary>Always 202 — whether or not the address has an account (no enumeration).</summary>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _authService.RequestPasswordResetAsync(request.Email);
        return Accepted(new { message = "If the address has an account, a password reset email has been sent." });
    }

    /// <summary>204 on success; 400 with code <c>reset_token_invalid</c> / <c>password_too_weak</c> otherwise.</summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        await _authService.ResetPasswordAsync(request.Token, request.NewPassword);
        return NoContent();
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var (user, accessToken, refreshToken) = await _authService.LoginAsync(
                request.Email, request.Password, GetIpAddress());

            RefreshTokenCookie.Set(Response, refreshToken, _jwtSettings);

            return Ok(new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                UserDto.From(user)));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid email or password.", code = "invalid_credentials" });
        }
        catch (EmailNotVerifiedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Please verify your email address before signing in.",
                code = "email_not_verified"
            });
        }
        catch (AccountPendingApprovalException)
        {
            // Only reachable with the correct password (see AuthService) — not an
            // enumeration oracle.
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Your account is awaiting approval by an administrator. You will be notified by email once it has been accepted.",
                code = "pending_approval"
            });
        }
        catch (AccountLockedException ex)
        {
            // Likewise only reachable with the correct password.
            var retryAfterSeconds = (int)Math.Ceiling(ex.RetryAfter.TotalSeconds);
            Response.Headers.RetryAfter = retryAfterSeconds.ToString();
            return StatusCode(StatusCodes.Status423Locked, new
            {
                error = "Your account is temporarily locked after too many failed sign-in attempts.",
                code = "account_locked",
                retryAfterSeconds
            });
        }
    }

    [HttpPost("azure-callback")]
    public async Task<IActionResult> AzureAdCallback([FromBody] AzureAdTokenRequest request)
    {
        if (!_azureAdSettings.IsConfigured)
            return NotFound(new { error = "Azure AD is not configured.", code = "azure_not_configured" });

        try
        {
            var (user, accessToken, refreshToken) = await _authService.LoginWithAzureAdAsync(
                request.IdToken, GetIpAddress());

            RefreshTokenCookie.Set(Response, refreshToken, _jwtSettings);

            return Ok(new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                UserDto.From(user)));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid Azure AD token.", code = "azure_token_invalid" });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var token = Request.Cookies[RefreshTokenCookie.Name];

        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "No refresh token provided." });

        try
        {
            var (user, accessToken, refreshToken) = await _authService.RefreshAsync(
                token, GetIpAddress());

            RefreshTokenCookie.Set(Response, refreshToken, _jwtSettings);

            return Ok(new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                UserDto.From(user)));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid or expired refresh token." });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var userId = GetUserId();
        if (userId == null)
            return Unauthorized();

        try
        {
            var user = await _authService.GetUserAsync(userId.Value);
            return Ok(UserDto.From(user));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var token = Request.Cookies[RefreshTokenCookie.Name];

        if (!string.IsNullOrEmpty(token))
            await _tokenService.RevokeRefreshTokenAsync(token);

        RefreshTokenCookie.Clear(Response);

        return NoContent();
    }

    [HttpGet("config")]
    public IActionResult Config()
    {
        return Ok(new
        {
            azureAdEnabled = _azureAdSettings.IsConfigured,
            azureAdClientId = _azureAdSettings.IsConfigured ? _azureAdSettings.ClientId : null,
            azureAdTenantId = _azureAdSettings.IsConfigured ? _azureAdSettings.TenantId : null,
            // Domains that must use Microsoft sign-in — lets the register page
            // steer these users to SSO before they fill in the form.
            ssoDomains = _registrationSettings.GetSsoDomains()
        });
    }

    private Guid? GetUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null && Guid.TryParse(claim.Value, out var id) ? id : null;
    }

    private string? GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString();
    }
}
