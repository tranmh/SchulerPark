namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface IAuthService
{
    /// <summary>
    /// Creates an unverified account and sends a verification email. Deliberately
    /// silent when the email is already taken (no account enumeration): resends
    /// the verification email for unverified accounts, does nothing for verified ones.
    /// </summary>
    /// <param name="preferredLanguage">UI language at registration ("de"/"en"); decides the language of the verification mail.</param>
    Task RegisterAsync(string email, string displayName, string password, string? preferredLanguage = null);
    Task<bool> VerifyEmailAsync(string token);
    Task ResendVerificationEmailAsync(string email);
    Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> LoginWithAzureAdAsync(string idToken, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> RefreshAsync(string refreshToken, string? ipAddress);
    Task<User> GetUserAsync(Guid userId);

    // ── Phase 20 WP2: password self-service ──

    /// <summary>
    /// Always completes silently (no enumeration). A live local account gets a reset
    /// mail; an SSO-only account gets a "sign in with Microsoft" mail; unknown → nothing.
    /// </summary>
    Task RequestPasswordResetAsync(string email);

    /// <summary>
    /// Consumes a reset token. Codes: <c>reset_token_invalid</c>, <c>password_too_weak</c>.
    /// Also marks the mailbox verified and clears any lockout; all refresh tokens are revoked.
    /// </summary>
    Task ResetPasswordAsync(string token, string newPassword);

    /// <summary>
    /// Changes the password of a signed-in user and rotates every session: all refresh
    /// tokens are revoked and a fresh pair is returned so the current session continues.
    /// Codes: <c>password_incorrect</c>, <c>password_not_set</c> (SSO-only), <c>password_too_weak</c>.
    /// </summary>
    Task<(User User, string AccessToken, string RefreshToken)> ChangePasswordAsync(
        Guid userId, string currentPassword, string newPassword, string? ipAddress);
}
