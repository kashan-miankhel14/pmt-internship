namespace PMT.Domain.Exceptions;

public sealed class ValidationException : Exception
{
    public ValidationException(IEnumerable<string> errors) : base("One or more validation errors occurred.")
        => Errors = errors.ToArray();

    public IReadOnlyCollection<string> Errors { get; }
}
