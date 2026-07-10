using Microsoft.EntityFrameworkCore;
using PayLibre.Domain.Auth;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Common.Interfaces;

/// <summary>The tenant-scoped persistence surface the Application layer depends on.</summary>
public interface IApplicationDbContext
{
    DbSet<School> Schools { get; }
    DbSet<SchoolUser> SchoolUsers { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Class> Classes { get; }
    DbSet<Student> Students { get; }

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

/// <summary>Issues signed dashboard access tokens.</summary>
public interface ITokenService
{
    AccessToken IssueAccessToken(SchoolUser user);
}

/// <summary>Delivers account details / notifications to guardians (SMS/email).</summary>
public interface INotificationSender
{
    Task SendVirtualAccountDetailsAsync(
        string toName, string? email, string? phone,
        string studentName, string nuban, string bankName, string accountName, CancellationToken ct = default);
}
