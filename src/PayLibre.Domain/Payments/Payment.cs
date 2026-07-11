using PayLibre.Domain.Common;

namespace PayLibre.Domain.Payments;

/// <summary>
/// A reconciled inflow to a student's dedicated virtual account, received from Xental's
/// <c>deposit.reconciled</c> webhook. Idempotent on <see cref="XentalTransactionRef"/>. The gross
/// amount is attributed to the student's outstanding fee invoices (see <see cref="FeeAllocation"/>).
/// </summary>
public sealed class Payment : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid StudentId { get; private set; }

    public string XentalTransactionRef { get; private set; } = null!;   // globally unique — idempotency key
    public long AmountKobo { get; private set; }                        // gross (what the payer sent)
    public long NetCreditKobo { get; private set; }                     // net of provider fees
    public string? PayerName { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    private Payment() { }

    public Payment(Guid schoolId, Guid studentId, string xentalTransactionRef, long amountKobo, long netCreditKobo, string? payerName, DateTimeOffset occurredAtUtc)
    {
        SchoolId = schoolId;
        StudentId = studentId;
        XentalTransactionRef = DomainException.Require(xentalTransactionRef, nameof(xentalTransactionRef));
        AmountKobo = amountKobo;
        NetCreditKobo = netCreditKobo;
        PayerName = string.IsNullOrWhiteSpace(payerName) ? null : payerName.Trim();
        OccurredAtUtc = occurredAtUtc;
    }
}

/// <summary>How much of a <see cref="Payment"/> settled a specific student-fee invoice (audit + receipts).</summary>
public sealed class FeeAllocation : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid PaymentId { get; private set; }
    public Guid StudentFeeId { get; private set; }
    public long AmountKobo { get; private set; }

    private FeeAllocation() { }

    public FeeAllocation(Guid schoolId, Guid paymentId, Guid studentFeeId, long amountKobo)
    {
        SchoolId = schoolId;
        PaymentId = paymentId;
        StudentFeeId = studentFeeId;
        AmountKobo = amountKobo;
    }
}
