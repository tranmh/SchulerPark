namespace SchulerPark.Core.Settings;

public class RegistrationSettings
{
    /// <summary>
    /// Email domains whose registrations are approved automatically, separated by
    /// ';' or ','. Every other domain registers as Pending and needs an admin to
    /// accept the account (Phase 18). Azure AD SSO accounts are always approved.
    /// </summary>
    public string AutoApprovedDomains { get; set; } = "andritz.com";

    public bool IsAutoApprovedDomain(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
            return false;
        var domain = email[(at + 1)..];

        return AutoApprovedDomains
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(d => domain.Equals(d, StringComparison.OrdinalIgnoreCase));
    }
}
