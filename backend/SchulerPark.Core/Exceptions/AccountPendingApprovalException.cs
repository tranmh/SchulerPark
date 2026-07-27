namespace SchulerPark.Core.Exceptions;

/// <summary>
/// Thrown when a local login is attempted on an account that is verified but still
/// awaiting admin approval (external email domain). Mapped to 403 with a
/// machine-readable code. Only ever thrown AFTER the password verified — it must
/// not become an account-enumeration oracle.
/// </summary>
public class AccountPendingApprovalException : Exception
{
    public AccountPendingApprovalException(string message) : base(message) { }
}
