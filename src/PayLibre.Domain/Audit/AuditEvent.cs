using PayLibre.Domain.Common;

namespace PayLibre.Domain.Audit;

/// <summary>
/// An immutable record of a significant action taken in a school (fee created, settlement changed,
/// refund requested/approved, user invited, export run, …). Tenant-owned, so each school sees only its
/// own trail. Written alongside the action; never updated or deleted.
/// </summary>
public sealed class AuditEvent : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public Guid? ActorUserId { get; private set; }     // null = system/background action
    public string? ActorEmail { get; private set; }
    public string Action { get; private set; } = null!; // dotted verb, e.g. "fee.created"
    public string? EntityType { get; private set; }     // e.g. "Fee", "School"
    public Guid? EntityId { get; private set; }
    public string Summary { get; private set; } = null!; // human-readable one-liner

    private AuditEvent() { }

    public AuditEvent(Guid schoolId, Guid? actorUserId, string? actorEmail,
        string action, string? entityType, Guid? entityId, string summary)
    {
        SchoolId = schoolId;
        ActorUserId = actorUserId;
        ActorEmail = string.IsNullOrWhiteSpace(actorEmail) ? null : actorEmail.Trim();
        Action = DomainException.Require(action, nameof(action));
        EntityType = entityType;
        EntityId = entityId;
        Summary = DomainException.Require(summary, nameof(summary));
    }
}
