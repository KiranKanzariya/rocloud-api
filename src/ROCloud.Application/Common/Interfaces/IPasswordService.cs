namespace ROCloud.Application.Common.Interfaces;

/// <summary>Password hashing/verification (BCrypt). Guide §10.2.</summary>
public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string password, string hash);
}
