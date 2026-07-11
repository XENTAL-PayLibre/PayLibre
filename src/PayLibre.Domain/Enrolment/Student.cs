using PayLibre.Domain.Common;

namespace PayLibre.Domain.Enrolment;

public enum StudentStatus { Active = 1, Inactive = 2 }

/// <summary>
/// A student. On creation PayLibre provisions a Xental <b>dedicated virtual account</b> (persistent
/// NUBAN) for the student and caches the account details here for display. Parents pay fees by
/// transfer into this account; Xental reconciles and notifies PayLibre.
/// </summary>
public sealed class Student : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string AdmissionNo { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public Guid ClassId { get; private set; }
    public string Session { get; private set; } = null!;

    public string GuardianName { get; private set; } = null!;
    public string? GuardianPhone { get; private set; }
    public string? GuardianEmail { get; private set; }

    public StudentStatus Status { get; private set; }

    /// <summary>True when a parent self-enrolled the student via the school's join code.</summary>
    public bool SelfEnrolled { get; private set; }

    // Cached Xental dedicated-virtual-account details (source of truth is Xental).
    public string? XentalAccountRef { get; private set; }   // what we send to Xental as accountRef
    public string? Nuban { get; private set; }
    public string? BankName { get; private set; }
    public string? AccountName { get; private set; }

    public bool HasVirtualAccount => !string.IsNullOrWhiteSpace(Nuban);

    private Student() { }

    public Student(Guid schoolId, string admissionNo, string fullName, Guid classId, string session,
        string guardianName, string? guardianPhone, string? guardianEmail, bool selfEnrolled = false)
    {
        SelfEnrolled = selfEnrolled;
        SchoolId = schoolId;
        AdmissionNo = DomainException.Require(admissionNo, nameof(admissionNo));
        FullName = DomainException.Require(fullName, nameof(fullName));
        if (classId == Guid.Empty) throw new DomainException("A class is required.");
        ClassId = classId;
        Session = DomainException.Require(session, nameof(session));
        GuardianName = DomainException.Require(guardianName, nameof(guardianName));
        GuardianPhone = string.IsNullOrWhiteSpace(guardianPhone) ? null : guardianPhone.Trim();
        GuardianEmail = string.IsNullOrWhiteSpace(guardianEmail) ? null : guardianEmail.Trim().ToLowerInvariant();
        Status = StudentStatus.Active;
    }

    /// <summary>The reference we present to Xental for this student's virtual account.</summary>
    public string BuildAccountRef() => XentalAccountRef ??= $"stu_{Id:N}";

    /// <summary>Cache the provisioned Xental DVA details.</summary>
    public void AttachVirtualAccount(string accountRef, string nuban, string bankName, string accountName)
    {
        XentalAccountRef = DomainException.Require(accountRef, nameof(accountRef));
        Nuban = DomainException.Require(nuban, nameof(nuban));
        BankName = DomainException.Require(bankName, nameof(bankName));
        AccountName = DomainException.Require(accountName, nameof(accountName));
    }

    public void Update(string fullName, Guid classId, string session, string guardianName, string? guardianPhone, string? guardianEmail)
    {
        FullName = DomainException.Require(fullName, nameof(fullName));
        if (classId == Guid.Empty) throw new DomainException("A class is required.");
        ClassId = classId;
        Session = DomainException.Require(session, nameof(session));
        GuardianName = DomainException.Require(guardianName, nameof(guardianName));
        GuardianPhone = string.IsNullOrWhiteSpace(guardianPhone) ? null : guardianPhone.Trim();
        GuardianEmail = string.IsNullOrWhiteSpace(guardianEmail) ? null : guardianEmail.Trim().ToLowerInvariant();
    }

    public void Deactivate() => Status = StudentStatus.Inactive;
}
