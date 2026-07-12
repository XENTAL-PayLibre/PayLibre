using PayLibre.Domain.Common;

namespace PayLibre.Domain.Fees;

public enum Term { First = 1, Second = 2, Third = 3 }

/// <summary>Where a StudentFee stands. Overdue = unpaid/partly-paid past its due date.</summary>
public enum FeeStatus { Pending = 1, Partial = 2, Paid = 3, Overdue = 4 }

/// <summary>A fee category (Tuition, PTA, Uniform, …), owned by a school.</summary>
public sealed class FeeCategory : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public string Name { get; private set; } = null!;

    private FeeCategory() { }

    public FeeCategory(Guid schoolId, string name)
    {
        SchoolId = schoolId;
        Name = DomainException.Require(name, nameof(name));
    }

    public void Rename(string name) => Name = DomainException.Require(name, nameof(name));
}

/// <summary>
/// A fee definition: an amount charged to a class for a term/session. Creating it fans out one
/// <see cref="StudentFee"/> per student in that class (see FeeService). Money is integer kobo.
/// </summary>
public sealed class Fee : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string Name { get; private set; } = null!;
    public Guid FeeCategoryId { get; private set; }
    public Guid ClassId { get; private set; }
    public string Session { get; private set; } = null!;
    public Term Term { get; private set; }
    public long AmountKobo { get; private set; }
    public DateTimeOffset DueDateUtc { get; private set; }

    /// <summary>Whether an overdue late-fee surcharge applies to this fee (when the school has one configured).</summary>
    public bool AppliesLateFee { get; private set; }

    private Fee() { }

    public Fee(Guid schoolId, string name, Guid feeCategoryId, Guid classId, string session, Term term, long amountKobo, DateTimeOffset dueDateUtc, bool appliesLateFee = true)
    {
        AppliesLateFee = appliesLateFee;
        SchoolId = schoolId;
        Name = DomainException.Require(name, nameof(name));
        if (feeCategoryId == Guid.Empty) throw new DomainException("A fee category is required.");
        FeeCategoryId = feeCategoryId;
        if (classId == Guid.Empty) throw new DomainException("A class is required.");
        ClassId = classId;
        Session = DomainException.Require(session, nameof(session));
        Term = term;
        if (amountKobo <= 0) throw new DomainException("Fee amount must be positive.");
        AmountKobo = amountKobo;
        DueDateUtc = dueDateUtc;
    }
}

/// <summary>A per-student instance of a <see cref="Fee"/> — the invoice payments settle against.</summary>
public sealed class StudentFee : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid FeeId { get; private set; }
    public Guid StudentId { get; private set; }

    public long AmountKobo { get; private set; }
    public long AmountPaidKobo { get; private set; }
    public FeeStatus Status { get; private set; }
    public DateTimeOffset DueDateUtc { get; private set; }

    // Late-fee surcharge applied once when overdue (0 = none). Kept separate so the original fee amount
    // is still derivable (AmountKobo - LateFeeAppliedKobo) and the surcharge isn't re-applied.
    public long LateFeeAppliedKobo { get; private set; }
    public DateTimeOffset? LateFeeAppliedAtUtc { get; private set; }

    // Dunning: the last reminder stage sent + when, so the maintenance job never repeats a stage.
    public string? LastReminderStage { get; private set; }
    public DateTimeOffset? LastReminderAtUtc { get; private set; }

    public long OutstandingKobo => Math.Max(0, AmountKobo - AmountPaidKobo);

    private StudentFee() { }

    public StudentFee(Guid schoolId, Guid feeId, Guid studentId, long amountKobo, DateTimeOffset dueDateUtc, DateTimeOffset now)
    {
        SchoolId = schoolId;
        FeeId = feeId;
        StudentId = studentId;
        AmountKobo = amountKobo;
        DueDateUtc = dueDateUtc;
        RecomputeStatus(now);
    }

    /// <summary>Add a one-time overdue surcharge to this invoice (increases the amount owed). Idempotent:
    /// a no-op if a late fee was already applied or the surcharge is non-positive.</summary>
    public bool ApplyLateFee(long surchargeKobo, DateTimeOffset now)
    {
        if (LateFeeAppliedKobo > 0 || surchargeKobo <= 0) return false;
        LateFeeAppliedKobo = surchargeKobo;
        LateFeeAppliedAtUtc = now;
        AmountKobo += surchargeKobo;
        RecomputeStatus(now);
        return true;
    }

    /// <summary>Record that a dunning reminder for <paramref name="stage"/> was sent now.</summary>
    public void RecordReminder(string stage, DateTimeOffset now)
    {
        LastReminderStage = stage;
        LastReminderAtUtc = now;
    }

    /// <summary>Apply up to <paramref name="available"/> kobo to this invoice; returns the amount applied.</summary>
    public long Allocate(long available, DateTimeOffset now)
    {
        if (available <= 0) return 0;
        var applied = Math.Min(available, OutstandingKobo);
        AmountPaidKobo += applied;
        RecomputeStatus(now);
        return applied;
    }

    public void RecomputeStatus(DateTimeOffset now)
    {
        if (AmountPaidKobo >= AmountKobo) Status = FeeStatus.Paid;      // fully settled
        else if (DueDateUtc < now) Status = FeeStatus.Overdue;         // outstanding balance past due (incl. partial)
        else if (AmountPaidKobo > 0) Status = FeeStatus.Partial;       // part-paid, not yet due
        else Status = FeeStatus.Pending;                              // unpaid, not yet due
    }
}
