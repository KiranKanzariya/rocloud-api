namespace ROCloud.Application.Common.Exceptions;

/// <summary>Thrown when a requested entity does not exist (or is not visible to the caller).</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public NotFoundException(string name, object key)
        : base($"\"{name}\" ({key}) was not found.") { }
}
