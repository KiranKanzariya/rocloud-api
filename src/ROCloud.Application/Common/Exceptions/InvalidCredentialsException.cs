namespace ROCloud.Application.Common.Exceptions;

/// <summary>Thrown on a failed login. Deliberately generic so it never reveals whether the email exists.</summary>
public class InvalidCredentialsException : Exception
{
    public InvalidCredentialsException() : base("Invalid email or password.") { }
}
