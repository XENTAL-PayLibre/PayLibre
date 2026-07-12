using Microsoft.EntityFrameworkCore;
using PayLibre.Domain.Auth;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Fees;
using PayLibre.Domain.Parents;
using PayLibre.Domain.Payments;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Common.Interfaces;

/// <summary>The tenant-scoped persistence surface the Application layer depends on.</summary>
public interface IApplicationDbContext
{
    DbSet<School> Schools { get; }
    DbSet<SchoolUser> SchoolUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<Class> Classes { get; }
    DbSet<Student> Students { get; }
    DbSet<FeeCategory> FeeCategories { get; }
    DbSet<Fee> Fees { get; }
    DbSet<StudentFee> StudentFees { get; }
    DbSet<Payment> Payments { get; }
    DbSet<FeeAllocation> FeeAllocations { get; }
    DbSet<WebhookEvent> WebhookEvents { get; }
    DbSet<Parent> Parents { get; }
    DbSet<LoginOtp> LoginOtps { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves the current tenant (school) from the authenticated request.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
    Guid RequireTenantId();
}

/// <summary>Testable wall clock.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Password hashing (BCrypt in production).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

/// <summary>Access token issued for a dashboard session.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Issues signed access tokens for the dashboard (school users) and the parent app.</summary>
public interface ITokenService
{
    AccessToken IssueAccessToken(SchoolUser user);
    AccessToken IssueParentToken(Parent parent);
}

/// <summary>Delivers account details / notifications to guardians (SMS + email).</summary>
public interface INotificationSender
{
    Task SendVirtualAccountDetailsAsync(
        string toName, string? email, string? phone,
        string studentName, string nuban, string bankName, string accountName, CancellationToken ct = default);

    /// <summary>Email a school user a password-reset link.</summary>
    Task SendPasswordResetAsync(string toEmail, string resetUrl, CancellationToken ct = default);

    /// <summary>Welcome a newly registered user (school owner or parent).</summary>
    Task SendWelcomeAsync(string toEmail, string name, CancellationToken ct = default);

    /// <summary>Email a one-time sign-in code (2-step login).</summary>
    Task SendLoginCodeAsync(string toEmail, string code, CancellationToken ct = default);

    /// <summary>Send a payment receipt (email + SMS) to a student's guardian after a deposit is reconciled.
    /// All amounts in kobo.</summary>
    Task SendPaymentReceiptAsync(
        string toName, string? email, string? phone, string studentName,
        long amountKobo, int invoicesSettled, long outstandingKobo, DateTimeOffset occurredAtUtc,
        CancellationToken ct = default);
}
