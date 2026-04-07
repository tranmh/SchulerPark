namespace SchulerPark.Infrastructure.Services;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SchulerPark.Core.Settings;

public class AzureAdTokenValidator
{
    private readonly AzureAdSettings _settings;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    public AzureAdTokenValidator(IOptions<AzureAdSettings> settings)
    {
        _settings = settings.Value;

        if (_settings.IsConfigured)
        {
            var metadataUrl = $"{_settings.Instance}{_settings.TenantId}/v2.0/.well-known/openid-configuration";
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataUrl, new OpenIdConnectConfigurationRetriever());
        }
    }

    public bool IsConfigured => _settings.IsConfigured;

    public async Task<AzureAdUserInfo?> ValidateTokenAsync(string idToken)
    {
        if (!_settings.IsConfigured || _configManager == null)
            return null;

        var config = await _configManager.GetConfigurationAsync();

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{_settings.Instance}{_settings.TenantId}/v2.0",
            ValidateAudience = true,
            ValidAudience = _settings.ClientId,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = config.SigningKeys,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var result = await handler.ValidateTokenAsync(idToken, validationParams);

            if (!result.IsValid)
                return null;

            var claims = result.ClaimsIdentity;
            var oid = claims.FindFirst("oid")?.Value;
            var email = claims.FindFirst("preferred_username")?.Value
                     ?? claims.FindFirst("email")?.Value;
            var name = claims.FindFirst("name")?.Value;

            if (string.IsNullOrEmpty(oid) || string.IsNullOrEmpty(email))
                return null;

            return new AzureAdUserInfo(oid, email, name ?? email);
        }
        catch
        {
            return null;
        }
    }
}

public record AzureAdUserInfo(string ObjectId, string Email, string DisplayName);
