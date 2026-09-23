namespace SchulerPark.Core.Helpers;

/// <summary>
/// The one password rule for every place a password is set (registration, reset,
/// change). Length alone admits "12345678", so at least 3 of 4 character classes are
/// required and well-known passwords and site terms are rejected. The API's
/// <c>PasswordComplexityAttribute</c> and the auth service both delegate here so the
/// rule cannot drift between endpoints.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password123", "passw0rd", "p@ssw0rd", "p@ssword1",
        "12345678", "123456789", "1234567890", "qwertyuiop", "qwerty123", "1q2w3e4r",
        "iloveyou", "sunshine", "princess", "football", "baseball", "superman",
        "welcome1", "admin123", "letmein1", "trustno1", "abcd1234", "asdf1234",
        "11111111", "00000000", "aa123456", "1qaz2wsx"
    };

    // Site-specific words an attacker will try first; matched as substrings.
    private static readonly string[] SiteTerms = ["schuler", "louise"];

    public const string RequirementText =
        "Password must be 8–128 characters and contain at least 3 of: lowercase letter, uppercase letter, " +
        "digit, special character, and must not be a commonly used password.";

    /// <summary>Complexity only (no length check) — used by the DTO attribute, which owns the length rule.</summary>
    public static bool MeetsComplexity(string password)
    {
        if (CommonPasswords.Contains(password))
            return false;

        if (SiteTerms.Any(term => password.Contains(term, StringComparison.OrdinalIgnoreCase)))
            return false;

        var classes = 0;
        if (password.Any(char.IsLower)) classes++;
        if (password.Any(char.IsUpper)) classes++;
        if (password.Any(char.IsDigit)) classes++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) classes++;

        return classes >= 3;
    }

    /// <summary>Full rule: length and complexity.</summary>
    public static bool IsAcceptable(string? password) =>
        password is not null
        && password.Length >= MinLength
        && password.Length <= MaxLength
        && MeetsComplexity(password);
}
