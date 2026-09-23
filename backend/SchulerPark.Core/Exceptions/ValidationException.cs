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

    /// <summary>
    /// Optional interpolation values for the localized message (emitted as the
    /// ProblemDetails <c>params</c> extension), e.g. the other location's name for
    /// <c>booking_duplicate_other_location</c>. Values must be safe to show to the user.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; }

    public ValidationException(string message, string? code = null) : base(message)
    {
        Code = code;
    }

    public ValidationException(string message, string code, IReadOnlyDictionary<string, object?> parameters)
        : base(message)
    {
        Code = code;
        Parameters = parameters;
    }
}
