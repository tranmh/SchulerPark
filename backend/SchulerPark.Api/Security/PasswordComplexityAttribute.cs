namespace SchulerPark.Api.Security;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Password rule for the public registration endpoint. Length alone (MinLength(8))
/// admits "12345678" on an internet-facing signup form, so additionally require at
/// least 3 of 4 character classes and reject well-known passwords and site terms.
/// Length limits stay in the MinLength/MaxLength attributes on the DTO.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class PasswordComplexityAttribute : ValidationAttribute
{
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
        "Password must contain at least 3 of: lowercase letter, uppercase letter, digit, special character, " +
        "and must not be a commonly used password.";

    public override bool IsValid(object? value)
    {
        // Null/empty and length are handled by [Required]/[MinLength]/[MaxLength].
        if (value is not string password || password.Length == 0)
            return true;

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

    public override string FormatErrorMessage(string name) => RequirementText;
}
