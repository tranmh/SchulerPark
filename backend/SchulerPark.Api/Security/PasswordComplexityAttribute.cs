namespace SchulerPark.Api.Security;

using System.ComponentModel.DataAnnotations;
using SchulerPark.Core.Helpers;

/// <summary>
/// Password rule for the public registration endpoint. Length alone (MinLength(8))
/// admits "12345678" on an internet-facing signup form, so additionally require at
/// least 3 of 4 character classes and reject well-known passwords and site terms.
/// Length limits stay in the MinLength/MaxLength attributes on the DTO. The rule
/// itself lives in <see cref="PasswordPolicy"/> so reset/change share it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public class PasswordComplexityAttribute : ValidationAttribute
{
    public const string RequirementText = PasswordPolicy.RequirementText;

    public override bool IsValid(object? value)
    {
        // Null/empty and length are handled by [Required]/[MinLength]/[MaxLength].
        if (value is not string password || password.Length == 0)
            return true;

        return PasswordPolicy.MeetsComplexity(password);
    }

    public override string FormatErrorMessage(string name) => RequirementText;
}
