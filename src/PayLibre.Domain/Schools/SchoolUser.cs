using PayLibre.Domain.Common;

namespace PayLibre.Domain.Schools;

public enum SchoolRole
{
    Owner = 1,       // full control (registered the school)
    Admin = 2,       // manage school + write
    Bursar = 3,      // day-to-day write (fees, students, reminders)
    Accountant = 4,  // read-only (finance oversight)
    Auditor = 5,     // read-only + audit trail
}

/// <summary>A dashboard user for a school. The owner is created at registration.</summary>
public sealed class SchoolUser : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string? FullName { get; private set; }
    public SchoolRole Role { get; private set; }

    private SchoolUser() { }

    public SchoolUser(Guid schoolId, string email, string passwordHash, SchoolRole role, string? fullName = null)
    {
        SchoolId = schoolId;
        Email = DomainException.Require(email, nameof(email)).ToLowerInvariant();
        PasswordHash = DomainException.Require(passwordHash, nameof(passwordHash));
        Role = role;
        FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
    }

    public void SetPasswordHash(string passwordHash) =>
        PasswordHash = DomainException.Require(passwordHash, nameof(passwordHash));
}
