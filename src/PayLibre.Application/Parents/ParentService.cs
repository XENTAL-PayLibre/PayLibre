using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Fees;

namespace PayLibre.Application.Parents;

public sealed record ParentChild(
    Guid StudentId, string FullName, string AdmissionNo, string SchoolName, string ClassName,
    string? Nuban, string? BankName, string? AccountName, long OutstandingKobo);

public sealed record ParentFeeRow(
    Guid StudentFeeId, string FeeName, long AmountKobo, long AmountPaidKobo, long OutstandingKobo, string Status, DateTimeOffset DueDateUtc);

public sealed record ParentPaymentRow(Guid Id, string StudentName, long AmountKobo, DateTimeOffset OccurredAtUtc);

public sealed record ParentPaymentDetails(string StudentName, string Nuban, string BankName, string AccountName, long OutstandingKobo);

public sealed record ReceiptData(
    string SchoolName, string StudentName, string AdmissionNo, long AmountKobo, long NetCreditKobo,
    string? PayerName, string Reference, DateTimeOffset OccurredAtUtc);

/// <summary>
/// Read API for the parent app. A parent sees only children whose guardian email matches their
/// account email. Global (children may span schools), so it bypasses the tenant filter deliberately.
/// </summary>
public sealed class ParentService(IApplicationDbContext db)
{
    /// <summary>Student ids linked to a parent via an additional-guardian record (multi-guardian).</summary>
    private async Task<HashSet<Guid>> ExtraGuardianStudentIdsAsync(string email, CancellationToken ct) =>
        (await db.StudentGuardians.IgnoreQueryFilters().Where(g => g.Email == email).Select(g => g.StudentId).ToListAsync(ct)).ToHashSet();

    public async Task<IReadOnlyList<ParentChild>> GetChildrenAsync(string parentEmail, CancellationToken ct = default)
    {
        var email = Norm(parentEmail);
        var extra = await ExtraGuardianStudentIdsAsync(email, ct);
        var students = await db.Students.IgnoreQueryFilters()
            .Where(s => s.GuardianEmail == email || extra.Contains(s.Id)).ToListAsync(ct);
        if (students.Count == 0) return Array.Empty<ParentChild>();

        var studentIds = students.Select(s => s.Id).ToList();
        var schoolIds = students.Select(s => s.SchoolId).Distinct().ToList();
        var classIds = students.Select(s => s.ClassId).Distinct().ToList();
        var schools = await db.Schools.IgnoreQueryFilters().Where(s => schoolIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var classes = await db.Classes.IgnoreQueryFilters().Where(c => classIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var outstanding = (await db.StudentFees.IgnoreQueryFilters().Where(sf => studentIds.Contains(sf.StudentId) && sf.AmountPaidKobo < sf.AmountKobo).ToListAsync(ct))
            .GroupBy(sf => sf.StudentId).ToDictionary(g => g.Key, g => g.Sum(x => x.AmountKobo - x.AmountPaidKobo));

        return students.Select(s => new ParentChild(
            s.Id, s.FullName, s.AdmissionNo, schools.GetValueOrDefault(s.SchoolId, "—"), classes.GetValueOrDefault(s.ClassId, "—"),
            s.Nuban, s.BankName, s.AccountName, outstanding.GetValueOrDefault(s.Id))).ToList();
    }

    public async Task<IReadOnlyList<ParentFeeRow>> GetChildFeesAsync(string parentEmail, Guid studentId, bool openOnly, CancellationToken ct = default)
    {
        await RequireOwnedStudentAsync(parentEmail, studentId, ct);
        var invoices = await db.StudentFees.IgnoreQueryFilters().Where(sf => sf.StudentId == studentId).ToListAsync(ct);
        if (openOnly) invoices = invoices.Where(sf => sf.AmountPaidKobo < sf.AmountKobo).ToList();
        var feeIds = invoices.Select(i => i.FeeId).Distinct().ToList();
        var feeNames = await db.Fees.IgnoreQueryFilters().Where(f => feeIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        return invoices.OrderBy(i => i.DueDateUtc)
            .Select(i => new ParentFeeRow(i.Id, feeNames.GetValueOrDefault(i.FeeId, "Fee"),
                i.AmountKobo, i.AmountPaidKobo, i.OutstandingKobo, i.Status.ToString(), i.DueDateUtc)).ToList();
    }

    /// <summary>The account to transfer to for a child, plus the total outstanding across their fees.</summary>
    public async Task<ParentPaymentDetails> GetPaymentDetailsAsync(string parentEmail, Guid studentId, CancellationToken ct = default)
    {
        var student = await RequireOwnedStudentAsync(parentEmail, studentId, ct);
        if (string.IsNullOrWhiteSpace(student.Nuban))
            throw new ConflictException("No virtual account for this student yet.");
        var outstanding = (await db.StudentFees.IgnoreQueryFilters()
            .Where(sf => sf.StudentId == studentId && sf.AmountPaidKobo < sf.AmountKobo).ToListAsync(ct))
            .Sum(sf => sf.AmountKobo - sf.AmountPaidKobo);
        return new ParentPaymentDetails(student.FullName, student.Nuban!, student.BankName!, student.AccountName!, outstanding);
    }

    public async Task<IReadOnlyList<ParentPaymentRow>> GetPaymentsAsync(string parentEmail, CancellationToken ct = default)
    {
        var email = Norm(parentEmail);
        var extra = await ExtraGuardianStudentIdsAsync(email, ct);
        var students = await db.Students.IgnoreQueryFilters().Where(s => s.GuardianEmail == email || extra.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        if (students.Count == 0) return Array.Empty<ParentPaymentRow>();
        var ids = students.Keys.ToList();
        var payments = await db.Payments.IgnoreQueryFilters().Where(p => ids.Contains(p.StudentId)).ToListAsync(ct);
        return payments.OrderByDescending(p => p.OccurredAtUtc)
            .Select(p => new ParentPaymentRow(p.Id, students.GetValueOrDefault(p.StudentId, "—"), p.AmountKobo, p.OccurredAtUtc)).ToList();
    }

    /// <summary>A receipt for one of the parent's children's payments (parent must own the student).</summary>
    public async Task<ReceiptData> GetReceiptAsync(string parentEmail, Guid paymentId, CancellationToken ct = default)
    {
        var email = Norm(parentEmail);
        var payment = await db.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException("Payment not found.");
        var student = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == payment.StudentId, ct);
        if (student is null || !await IsGuardianAsync(email, student, ct))
            throw new NotFoundException("Payment not found.");
        var schoolName = await db.Schools.IgnoreQueryFilters()
            .Where(s => s.Id == payment.SchoolId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "—";
        return new ReceiptData(schoolName, student.FullName, student.AdmissionNo, payment.AmountKobo,
            payment.NetCreditKobo, payment.PayerName, payment.XentalTransactionRef, payment.OccurredAtUtc);
    }

    private async Task<Domain.Enrolment.Student> RequireOwnedStudentAsync(string parentEmail, Guid studentId, CancellationToken ct)
    {
        var email = Norm(parentEmail);
        var student = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null || !await IsGuardianAsync(email, student, ct)) throw new NotFoundException("Student not found.");
        return student;
    }

    /// <summary>True if the parent email is the student's primary guardian or an additional guardian.</summary>
    private async Task<bool> IsGuardianAsync(string email, Domain.Enrolment.Student student, CancellationToken ct) =>
        student.GuardianEmail == email
        || await db.StudentGuardians.IgnoreQueryFilters().AnyAsync(g => g.StudentId == student.Id && g.Email == email, ct);

    private static string Norm(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
