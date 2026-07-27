namespace SchulerPark.Core.Settings;

public class RegistrationSettings
{
    /// <summary>
    /// Email domains whose registrations are approved automatically, separated by
    /// ';' or ','. Every other domain registers as Pending and needs an admin to
    /// accept the account (Phase 18). Azure AD SSO accounts are always approved.
    /// </summary>
    public string AutoApprovedDomains { get; set; } = "technikumlaubholz.de";

    /// <summary>
    /// Email domains that must use Azure AD SSO, separated by ';' or ','. Local
    /// registration with these addresses is rejected and the user is directed to
    /// the Microsoft sign-in; existing local accounts can still log in.
    /// </summary>
    public string SsoDomains { get; set; } = "andritz.com";

    public bool IsAutoApprovedDomain(string email) =>
        MatchesDomainList(email, AutoApprovedDomains);

    public bool IsSsoDomain(string email) =>
        MatchesDomainList(email, SsoDomains);

    public string[] GetSsoDomains() => SplitDomains(SsoDomains);

    private static bool MatchesDomainList(string email, string domains)
    {
        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
            return false;
        var domain = email[(at + 1)..];

        return SplitDomains(domains)
            .Any(d => domain.Equals(d, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] SplitDomains(string domains) =>
        domains.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
