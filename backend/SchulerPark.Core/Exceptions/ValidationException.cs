namespace SchulerPark.Core.Exceptions;

public class ValidationException : Exception
{
    /// <summary>
    /// Optional machine-readable code (snake_case) for messages a user can trigger
    /// through normal use. The API emits it as the ProblemDetails `code` extension so
    /// the frontend can show a localized message instead of the English
    /// <see cref="Exception.Message"/>. Admin-only or programmer-error validations leave it null.
    /// </summary>
    public string? Code { get; }

    public ValidationException(string message, string? code = null) : base(message)
    {
        Code = code;
    }
}
