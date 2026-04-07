namespace SchulerPark.Api.DTOs.Auth;

public record AuthResponse(string AccessToken, DateTime ExpiresAt, UserDto User);
