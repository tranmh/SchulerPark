namespace SchulerPark.Core.Exceptions;

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
