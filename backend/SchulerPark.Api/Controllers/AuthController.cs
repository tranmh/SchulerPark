namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SchulerPark.Api.DTOs.Auth;
using SchulerPark.Core.Exceptions;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;

[ApiController]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ITokenService _tokenService;
    private readonly JwtSettings _jwtSettings;
    private readonly AzureAdSettings _azureAdSettings;

    public AuthController(
        IAuthService authService,
        ITokenService tokenService,
        IOptions<JwtSettings> jwtSettings,
        IOptions<AzureAdSettings> azureAdSettings)
    {
        _authService = authService;
        _tokenService = tokenService;
        _jwtSettings = jwtSettings.Value;
        _azureAdSettings = azureAdSettings.Value;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        // Always the same response, whether the address was new, already
        // registered, or already verified — no account enumeration.
        await _authService.RegisterAsync(request.Email, request.DisplayName, request.Password);

        return Ok(new
        {
            message = "If the email address is available, a verification email has been sent. " +
                      "Please confirm it before signing in."
        });
    }

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        var verified = await _authService.VerifyEmailAsync(request.Token);

        if (!verified)
            return BadRequest(new { error = "Verification link is invalid or has expired." });

        return Ok(new { message = "Email verified. You can sign in now." });
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request)
    {
        await _authService.ResendVerificationEmailAsync(request.Email);

        // Same response regardless of whether the account exists or is verified.
        return Ok(new { message = "If the address has an unverified account, a new verification email has been sent." });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var (user, accessToken, refreshToken) = await _authService.LoginAsync(
                request.Email, request.Password, GetIpAddress());

            SetRefreshTokenCookie(refreshToken);

            return Ok(new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                ToUserDto(user)));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid email or password." });
        }
        catch (EmailNotVerifiedException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "Please verify your email address before signing in.",
                code = "email_not_verified"
            });
        }
    }

    [HttpPost("azure-callback")]
    public async Task<IActionResult> AzureAdCallback([FromBody] AzureAdTokenRequest request)
    {
        if (!_azureAdSettings.IsConfigured)
            return NotFound(new { error = "Azure AD is not configured." });

        try
        {
            var (user, accessToken, refreshToken) = await _authService.LoginWithAzureAdAsync(
                request.IdToken, GetIpAddress());

            SetRefreshTokenCookie(refreshToken);

            return Ok(new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                ToUserDto(user)));
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { error = "Invalid Azure AD token." });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        var token = Request.Cookies["refreshToken"];

        if (string.IsNullOrEmpty(token))
            return Unauthorized(new { error = "No refresh token provided." });

        try
        {
            var (user, accessToken, refreshToken) = await _authService.RefreshAsync(
                token, GetIpAddress());

            SetRefreshTokenCookie(refreshToken);

            return Ok(new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                ToUserDto(user)));
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
            return Ok(ToUserDto(user));
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
        var token = Request.Cookies["refreshToken"];

        if (!string.IsNullOrEmpty(token))
            await _tokenService.RevokeRefreshTokenAsync(token);

        // Mirror the attributes used when setting the cookie so the delete
        // reliably matches it in all browsers.
        Response.Cookies.Delete("refreshToken", new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth"
        });

        return NoContent();
    }

    [HttpGet("config")]
    public IActionResult Config()
    {
        return Ok(new
        {
            azureAdEnabled = _azureAdSettings.IsConfigured,
            azureAdClientId = _azureAdSettings.IsConfigured ? _azureAdSettings.ClientId : null,
            azureAdTenantId = _azureAdSettings.IsConfigured ? _azureAdSettings.TenantId : null
        });
    }

    private void SetRefreshTokenCookie(string token)
    {
        Response.Cookies.Append("refreshToken", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(_jwtSettings.RefreshExpiryDays)
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

    private static UserDto ToUserDto(Core.Entities.User user)
    {
        return new UserDto(
            user.Id,
            user.Email,
            user.DisplayName,
            user.CarLicensePlate,
            user.Role.ToString(),
            !string.IsNullOrEmpty(user.AzureAdObjectId),
            user.PreferredLocationId,
            user.PreferredSlotId);
    }
}
