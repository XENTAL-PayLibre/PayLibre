using PayLibre.Domain.Common;

namespace PayLibre.Domain.Enrolment;

/// <summary>
/// An additional guardian for a student, beyond the primary guardian recorded on the student itself.
/// Any parent whose account email matches a primary <c>GuardianEmail</c> or a StudentGuardian email can
/// view + pay for the child. Tenant-owned.
/// </summary>
public sealed class StudentGuardian : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid StudentId { get; private set; }

    public string Email { get; private set; } = null!;
    public string? Name { get; private set; }
    public string? Phone { get; private set; }

    private StudentGuardian() { }

    public StudentGuardian(Guid schoolId, Guid studentId, string email, string? name, string? phone)
    {
        SchoolId = schoolId;
        StudentId = studentId;
        Email = DomainException.Require(email, nameof(email)).Trim().ToLowerInvariant();
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }
}
