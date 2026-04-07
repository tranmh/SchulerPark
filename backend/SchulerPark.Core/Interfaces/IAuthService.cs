namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface IAuthService
{
    Task<(User User, string AccessToken, string RefreshToken)> RegisterAsync(string email, string displayName, string password, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> LoginAsync(string email, string password, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> LoginWithAzureAdAsync(string idToken, string? ipAddress);
    Task<(User User, string AccessToken, string RefreshToken)> RefreshAsync(string refreshToken, string? ipAddress);
    Task<User> GetUserAsync(Guid userId);
}
