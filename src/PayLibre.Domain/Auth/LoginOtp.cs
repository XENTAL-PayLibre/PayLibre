using PayLibre.Domain.Common;

namespace PayLibre.Domain.Auth;

public enum OtpSubject { SchoolUser = 1, Parent = 2 }

/// <summary>
/// A one-time login code emailed as the second step of sign-in. Only the SHA-256 <see cref="CodeHash"/>
/// is stored. Global (not tenant-owned) — login happens before a tenant is known, and parents are
/// global. Limited attempts + short TTL; single-use.
/// </summary>
public sealed class LoginOtp : BaseEntity
{
    public const int MaxAttempts = 5;

    public OtpSubject Subject { get; private set; }
    public Guid SubjectId { get; private set; }
    public string Email { get; private set; } = null!;
    public string CodeHash { get; private set; } = null!;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public bool Consumed { get; private set; }
    public int Attempts { get; private set; }

    private LoginOtp() { }

    public LoginOtp(OtpSubject subject, Guid subjectId, string email, string codeHash, DateTimeOffset expiresAtUtc)
    {
        Subject = subject;
        SubjectId = subjectId;
        Email = DomainException.Require(email, nameof(email)).ToLowerInvariant();
        CodeHash = DomainException.Require(codeHash, nameof(codeHash));
        ExpiresAtUtc = expiresAtUtc;
    }

    public bool IsRedeemable(DateTimeOffset now) => !Consumed && Attempts < MaxAttempts && ExpiresAtUtc > now;
    public void RegisterFailedAttempt() => Attempts++;
    public void Consume() => Consumed = true;
}
