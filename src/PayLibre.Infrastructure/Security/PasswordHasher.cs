using Microsoft.Extensions.Options;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Security;

/// <summary>BCrypt password hashing.</summary>
public sealed class PasswordHasher(IOptions<AuthOptions> options) : IPasswordHasher
{
    private readonly int _workFactor = Math.Max(12, options.Value.BcryptWorkFactor);

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, _workFactor);

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}
