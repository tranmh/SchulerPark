namespace SchulerPark.Core.Exceptions;

/// <summary>
/// Thrown when a local login presents the CORRECT password for an account that is
/// currently locked out after repeated failures. Mapped to 423 with a machine-readable
/// code and the remaining lockout. Only ever thrown after the password verified — a
/// wrong password on a locked account stays a generic 401, so the lockout is not an
/// account-enumeration oracle (same trust rule as <see cref="AccountPendingApprovalException"/>).
/// </summary>
public class AccountLockedException : Exception
{
    public TimeSpan RetryAfter { get; }

    public AccountLockedException(TimeSpan retryAfter)
        : base("Account is temporarily locked after too many failed sign-in attempts.")
    {
        RetryAfter = retryAfter < TimeSpan.Zero ? TimeSpan.Zero : retryAfter;
    }
}
