using PayLibre.Domain.Common;

namespace PayLibre.Domain.Schools;

/// <summary>
/// An invitation for a new staff member to join a school with a given role. The raw token is emailed
/// (never stored); only its hash is kept. Single-use and time-limited. On acceptance a
/// <see cref="SchoolUser"/> is created and the invite is consumed.
/// </summary>
public sealed class Invite : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string Email { get; private set; } = null!;
    public SchoolRole Role { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public string? InvitedByEmail { get; private set; }
    public DateTimeOffset? AcceptedAtUtc { get; private set; }

    private Invite() { }

    public Invite(Guid schoolId, string email, SchoolRole role, string tokenHash, DateTimeOffset expiresAtUtc, string? invitedByEmail)
    {
        SchoolId = schoolId;
        Email = DomainException.Require(email, nameof(email)).Trim().ToLowerInvariant();
        Role = role;
        TokenHash = DomainException.Require(tokenHash, nameof(tokenHash));
        ExpiresAtUtc = expiresAtUtc;
        InvitedByEmail = string.IsNullOrWhiteSpace(invitedByEmail) ? null : invitedByEmail.Trim();
    }

    public bool IsRedeemable(DateTimeOffset now) => AcceptedAtUtc is null && ExpiresAtUtc > now;

    public void Accept(DateTimeOffset now) => AcceptedAtUtc = now;
}
