namespace ROCloud.Application.Common.Exceptions;

/// <summary>
/// Thrown when input validation fails. Carries a field → messages map. Phase 5's
/// ValidationBehaviour converts FluentValidation failures into this exception so the
/// API layer stays decoupled from FluentValidation.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException()
        : base("One or more validation failures have occurred.")
    {
        Errors = new Dictionary<string, string[]>();
    }

    public ValidationException(IDictionary<string, string[]> errors) : this()
    {
        Errors = errors;
    }

    public IDictionary<string, string[]> Errors { get; }
}
