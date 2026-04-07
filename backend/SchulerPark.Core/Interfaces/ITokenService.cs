namespace SchulerPark.Core.Interfaces;

using SchulerPark.Core.Entities;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    Task<string> GenerateRefreshTokenAsync(User user, string? ipAddress);
    Task<User?> ValidateRefreshTokenAsync(string token);
    Task RevokeRefreshTokenAsync(string token);
    Task RevokeAllUserTokensAsync(Guid userId);
}
