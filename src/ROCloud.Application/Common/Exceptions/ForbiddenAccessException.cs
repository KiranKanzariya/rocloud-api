namespace ROCloud.Application.Common.Exceptions;

/// <summary>Thrown when the caller is authenticated but lacks permission for the action.</summary>
public class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException()
        : base("You do not have permission to perform this action.") { }

    public ForbiddenAccessException(string message) : base(message) { }
}
