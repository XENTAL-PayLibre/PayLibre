using PayLibre.Domain.Common;

namespace PayLibre.Domain.Parents;

/// <summary>
/// A parent/guardian account for the mobile app. Global (not owned by a school) — a parent can have
/// children in more than one school. Children are linked by matching the guardian email on students.
/// </summary>
public sealed class Parent : BaseEntity
{
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string? FullName { get; private set; }
    public string? Phone { get; private set; }

    private Parent() { }

    public Parent(string email, string passwordHash, string? fullName, string? phone)
    {
        Email = DomainException.Require(email, nameof(email)).ToLowerInvariant();
        PasswordHash = DomainException.Require(passwordHash, nameof(passwordHash));
        FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void SetPasswordHash(string passwordHash) => PasswordHash = DomainException.Require(passwordHash, nameof(passwordHash));
}
