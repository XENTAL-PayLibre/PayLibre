using PayLibre.Domain.Common;

namespace PayLibre.Domain.Payments;

public enum DisputeStatus { Open = 1, Resolved = 2, Rejected = 3 }

/// <summary>
/// A parent-raised dispute about a payment (mis-attributed, missing, wrong amount). Lands in the
/// school's queue for a staff member to resolve or reject. Tenant-owned (the payment's school).
/// </summary>
public sealed class PaymentDispute : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid PaymentId { get; private set; }

    public string RaisedByEmail { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public DisputeStatus Status { get; private set; }
    public string? Resolution { get; private set; }
    public string? ResolvedByEmail { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    private PaymentDispute() { }

    public PaymentDispute(Guid schoolId, Guid paymentId, string raisedByEmail, string reason)
    {
        SchoolId = schoolId;
        PaymentId = paymentId;
        RaisedByEmail = DomainException.Require(raisedByEmail, nameof(raisedByEmail)).Trim().ToLowerInvariant();
        Reason = DomainException.Require(reason, nameof(reason));
        Status = DisputeStatus.Open;
    }

    public void Resolve(bool accepted, string? resolution, string? resolvedByEmail, DateTimeOffset now)
    {
        if (Status != DisputeStatus.Open) throw new DomainException("Only an open dispute can be resolved.");
        Status = accepted ? DisputeStatus.Resolved : DisputeStatus.Rejected;
        Resolution = string.IsNullOrWhiteSpace(resolution) ? null : resolution.Trim();
        ResolvedByEmail = string.IsNullOrWhiteSpace(resolvedByEmail) ? null : resolvedByEmail.Trim();
        ResolvedAtUtc = now;
    }
}
