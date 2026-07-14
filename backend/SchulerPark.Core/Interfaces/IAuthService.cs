namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface IAuthService
{
    /// <summary>
    /// Creates an unverified account and sends a verification email. Deliberately
    /// silent when the email is already taken (no account enumeration): resends
    /// the verification email for unverified accounts, does nothing for verified ones.
    /// </summary>
    Task RegisterAsync(string email, string displayName, string password);
    Task<bool> VerifyEmailAsync(string token);
    Task ResendVerificationEmailAsync(string email);
    Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> LoginWithAzureAdAsync(string idToken, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> RefreshAsync(string refreshToken, string? ipAddress);
    Task<User> GetUserAsync(Guid userId);
}
