namespace SIGER.Application.Exceptions;

public class ValidationException : Exception
{
    public ValidationException(string message)
        : this([message])
    {
    }

    public ValidationException(IEnumerable<string> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors.Where(error => !string.IsNullOrWhiteSpace(error)).ToArray();
    }

    public ValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
        Errors = [message];
    }

    public IReadOnlyCollection<string> Errors { get; }
}
