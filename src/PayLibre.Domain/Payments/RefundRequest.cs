using PayLibre.Domain.Common;

namespace PayLibre.Domain.Payments;

public enum RefundStatus { Requested = 1, Approved = 2, Rejected = 3, Executed = 4, Failed = 5 }

/// <summary>
/// A maker-checker refund of a received payment. One user requests it, a <b>different</b> Owner/Admin
/// approves, and only then is the refund executed against Xental. Tenant-owned; the trail of who
/// requested/decided is retained for audit.
/// </summary>
public sealed class RefundRequest : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid PaymentId { get; private set; }
    public string XentalTransactionRef { get; private set; } = null!;

    public Guid? RequestedByUserId { get; private set; }
    public string? RequestedByEmail { get; private set; }
    public string? Reason { get; private set; }

    public RefundStatus Status { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public string? DecidedByEmail { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTimeOffset? DecidedAtUtc { get; private set; }

    // Populated once executed against Xental.
    public long AmountKobo { get; private set; }
    public string? TransferRef { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? FailureReason { get; private set; }

    private RefundRequest() { }

    public RefundRequest(Guid schoolId, Guid paymentId, string xentalTransactionRef,
        Guid? requestedByUserId, string? requestedByEmail, string? reason)
    {
        SchoolId = schoolId;
        PaymentId = paymentId;
        XentalTransactionRef = DomainException.Require(xentalTransactionRef, nameof(xentalTransactionRef));
        RequestedByUserId = requestedByUserId;
        RequestedByEmail = string.IsNullOrWhiteSpace(requestedByEmail) ? null : requestedByEmail.Trim();
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Status = RefundStatus.Requested;
    }

    /// <summary>Approve the refund. The approver must be a different user from the requester (dual control).</summary>
    public void Approve(Guid? approverUserId, string? approverEmail, DateTimeOffset now)
    {
        if (Status != RefundStatus.Requested) throw new DomainException("Only a pending refund can be approved.");
        // Dual control: the approver must be a known, different user than the requester. Reject when the
        // approver identity is indeterminate, and match on email too (not just id).
        if (approverUserId is null && string.IsNullOrWhiteSpace(approverEmail))
            throw new DomainException("The approver's identity could not be determined.");
        var sameUser = approverUserId is not null && approverUserId == RequestedByUserId;
        var sameEmail = !string.IsNullOrWhiteSpace(approverEmail) && !string.IsNullOrWhiteSpace(RequestedByEmail)
            && string.Equals(approverEmail.Trim(), RequestedByEmail, StringComparison.OrdinalIgnoreCase);
        if (sameUser || sameEmail)
            throw new DomainException("A refund must be approved by a different user than the one who requested it.");
        Status = RefundStatus.Approved;
        Decide(approverUserId, approverEmail, null, now);
    }

    public void Reject(Guid? approverUserId, string? approverEmail, string? note, DateTimeOffset now)
    {
        if (Status != RefundStatus.Requested) throw new DomainException("Only a pending refund can be rejected.");
        Status = RefundStatus.Rejected;
        Decide(approverUserId, approverEmail, note, now);
    }

    public void MarkExecuted(long amountKobo, string? transferRef, string? providerReference, DateTimeOffset now)
    {
        if (Status != RefundStatus.Approved) throw new DomainException("Only an approved refund can be executed.");
        Status = RefundStatus.Executed;
        AmountKobo = amountKobo;
        TransferRef = transferRef;
        ProviderReference = providerReference;
        DecidedAtUtc = now;
    }

    public void MarkFailed(string reason, DateTimeOffset now)
    {
        Status = RefundStatus.Failed;
        FailureReason = reason;
        DecidedAtUtc = now;
    }

    private void Decide(Guid? userId, string? email, string? note, DateTimeOffset now)
    {
        DecidedByUserId = userId;
        DecidedByEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DecidedAtUtc = now;
    }
}
