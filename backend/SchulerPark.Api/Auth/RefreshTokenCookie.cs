namespace SchulerPark.Api.Auth;

using SchulerPark.Core.Settings;

/// <summary>
/// The refresh-token cookie, set and cleared with identical attributes so the delete
/// reliably matches the cookie in every browser. Shared by the auth and profile
/// controllers (change-password rotates the session too).
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "refreshToken";

    public static void Set(HttpResponse response, string token, JwtSettings jwt)
    {
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth",
            MaxAge = TimeSpan.FromDays(jwt.RefreshExpiryDays)
        });
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Name, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = "/api/auth"
        });
    }
}
