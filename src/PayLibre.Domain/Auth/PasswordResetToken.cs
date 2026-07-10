using PayLibre.Domain.Common;

namespace PayLibre.Domain.Auth;

/// <summary>
/// A single-use password-reset token. Only the SHA-256 <see cref="TokenHash"/> is stored — the opaque
/// token travels only in the emailed reset link. Consumed on use; expires after a short TTL.
/// </summary>
public sealed class PasswordResetToken : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid SchoolUserId { get; private set; }

    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool Used { get; private set; }

    private PasswordResetToken() { }

    public PasswordResetToken(Guid schoolId, Guid schoolUserId, string tokenHash, DateTimeOffset expiresAtUtc)
    {
        SchoolId = schoolId;
        SchoolUserId = schoolUserId;
        TokenHash = DomainException.Require(tokenHash, nameof(tokenHash));
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsRedeemable(DateTimeOffset now) => !Used && ExpiresAtUtc > now;
    public void Consume() => Used = true;
}
