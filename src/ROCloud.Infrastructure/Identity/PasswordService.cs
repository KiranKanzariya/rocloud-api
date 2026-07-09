using Microsoft.Extensions.Configuration;
using ROCloud.Application.Common.Interfaces;

namespace ROCloud.Infrastructure.Identity;

/// <summary>BCrypt password hashing. Work factor from Security:BcryptWorkFactor (default 12, ~250 ms/hash). Guide §10.2.</summary>
public class PasswordService : IPasswordService
{
    private const int DefaultWorkFactor = 12;
    private readonly int _workFactor;

    public PasswordService(IConfiguration config)
        => _workFactor = int.TryParse(config["Security:BcryptWorkFactor"], out var w) && w is >= 10 and <= 16
            ? w : DefaultWorkFactor;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
