using PayLibre.Domain.Common;

namespace PayLibre.Domain.Auth;

/// <summary>
/// A rotating dashboard refresh token. Only the SHA-256 <see cref="TokenHash"/> is stored — the
/// opaque token itself lives only in the HttpOnly cookie. Rotated (revoked + replaced) on every use.
/// </summary>
public sealed class RefreshToken : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid SchoolUserId { get; private set; }

    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool Revoked { get; private set; }

    private RefreshToken() { }

    public RefreshToken(Guid schoolId, Guid schoolUserId, string tokenHash, DateTimeOffset expiresAtUtc)
    {
        SchoolId = schoolId;
        SchoolUserId = schoolUserId;
        TokenHash = DomainException.Require(tokenHash, nameof(tokenHash));
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsActive(DateTimeOffset now) => !Revoked && ExpiresAtUtc > now;
    public void Revoke() => Revoked = true;
}
