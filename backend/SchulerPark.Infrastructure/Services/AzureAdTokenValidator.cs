namespace SchulerPark.Infrastructure.Services;

using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using SchulerPark.Core.Settings;

public class AzureAdTokenValidator
{
    private readonly AzureAdSettings _settings;
    private readonly ILogger<AzureAdTokenValidator> _logger;
    private readonly ConfigurationManager<OpenIdConnectConfiguration>? _configManager;

    public AzureAdTokenValidator(IOptions<AzureAdSettings> settings, ILogger<AzureAdTokenValidator> logger)
    {
        _settings = settings.Value;
        _logger = logger;

        if (_settings.IsConfigured)
        {
            var metadataUrl = $"{_settings.Instance}{_settings.TenantId}/v2.0/.well-known/openid-configuration";
            _configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                metadataUrl, new OpenIdConnectConfigurationRetriever());
        }
    }

    public bool IsConfigured => _settings.IsConfigured;

    // Virtual so tests can substitute a fake validator for the account-linking logic.
    public virtual async Task<AzureAdUserInfo?> ValidateTokenAsync(string idToken)
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
        catch (SecurityTokenException ex)
        {
            // A bad token is expected input — fail closed, log at debug only.
            _logger.LogDebug(ex, "Azure AD token failed validation.");
            return null;
        }
        catch (Exception ex)
        {
            // Network / key-rollover / metadata failures are operational problems,
            // not bad tokens — still fail closed, but make them visible.
            _logger.LogError(ex, "Azure AD token validation errored (metadata/key retrieval?).");
            return null;
        }
    }
}

public record AzureAdUserInfo(string ObjectId, string Email, string DisplayName);
