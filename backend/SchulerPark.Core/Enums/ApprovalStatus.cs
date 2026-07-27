namespace SchulerPark.Core.Enums;

/// <summary>
/// Registration approval state. Accounts on an auto-approved email domain (and all
/// Azure AD SSO accounts) are Approved immediately; any other (external) address
/// registers as Pending and needs an Admin/SuperAdmin to accept it before the
/// first sign-in. Rejected accounts are kept so the address cannot silently
/// re-register, and behave like invalid credentials at login.
/// </summary>
public enum ApprovalStatus
{
    Approved = 0,
    Pending = 1,
    Rejected = 2
}
