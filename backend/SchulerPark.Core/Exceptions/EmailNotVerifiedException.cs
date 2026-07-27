namespace SchulerPark.Core.Exceptions;

/// <summary>
/// Thrown when a local login is attempted on an account whose email address
/// has not been verified yet. Mapped to 403 with a machine-readable code so
/// the frontend can offer to resend the verification email.
/// </summary>
public class EmailNotVerifiedException : Exception
{
    public EmailNotVerifiedException(string message) : base(message) { }
}
