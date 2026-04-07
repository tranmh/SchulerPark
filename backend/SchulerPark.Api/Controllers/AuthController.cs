namespace SchulerPark.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SchulerPark.Api.DTOs.Auth;
using SchulerPark.Core.Interfaces;
using SchulerPark.Core.Settings;
using SchulerPark.Infrastructure.Services;

[ApiController]
[Route("api/auth")]
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
        try
        {
            var (user, accessToken, refreshToken) = await _authService.RegisterAsync(
                request.Email, request.DisplayName, request.Password, GetIpAddress());

            SetRefreshTokenCookie(refreshToken);

            return Created("", new AuthResponse(
                accessToken,
                DateTime.UtcNow.AddMinutes(_jwtSettings.ExpiryMinutes),
                ToUserDto(user)));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
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

        Response.Cookies.Delete("refreshToken", new CookieOptions { Path = "/api/auth" });

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
            !string.IsNullOrEmpty(user.AzureAdObjectId));
    }
}
