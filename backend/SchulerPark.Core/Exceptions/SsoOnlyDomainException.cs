namespace SchulerPark.Core.Exceptions;

/// <summary>
/// Thrown when a local registration is attempted with an email domain that is
/// required to use Azure AD SSO (Registration:SsoDomains). Mapped to 400 with a
/// machine-readable code. The rule depends only on the address's domain, never on
/// whether an account exists — no enumeration concern.
/// </summary>
public class SsoOnlyDomainException : Exception
{
    public SsoOnlyDomainException(string message) : base(message) { }
}
