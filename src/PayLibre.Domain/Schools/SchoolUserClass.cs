using PayLibre.Domain.Common;

namespace PayLibre.Domain.Schools;

/// <summary>
/// Assigns a <see cref="SchoolUser"/> (a ClassTeacher) to a class. A class teacher's data access is
/// restricted to the classes they are assigned to. Tenant-owned.
/// </summary>
public sealed class SchoolUserClass : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid SchoolUserId { get; private set; }
    public Guid ClassId { get; private set; }

    private SchoolUserClass() { }

    public SchoolUserClass(Guid schoolId, Guid schoolUserId, Guid classId)
    {
        SchoolId = schoolId;
        SchoolUserId = schoolUserId;
        ClassId = classId;
    }
}
