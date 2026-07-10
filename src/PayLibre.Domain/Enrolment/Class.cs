using PayLibre.Domain.Common;

namespace PayLibre.Domain.Enrolment;

/// <summary>A class/grade within a school for a given academic session (e.g. "SS1" / "2026/2027").</summary>
public sealed class Class : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string Name { get; private set; } = null!;
    public string Session { get; private set; } = null!;

    private Class() { }

    public Class(Guid schoolId, string name, string session)
    {
        SchoolId = schoolId;
        Name = DomainException.Require(name, nameof(name));
        Session = DomainException.Require(session, nameof(session));
    }

    public void Rename(string name) => Name = DomainException.Require(name, nameof(name));
    public void SetSession(string session) => Session = DomainException.Require(session, nameof(session));
}
