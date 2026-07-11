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

/// <summary>
/// Read API for the parent app. A parent sees only children whose guardian email matches their
/// account email. Global (children may span schools), so it bypasses the tenant filter deliberately.
/// </summary>
public sealed class ParentService(IApplicationDbContext db)
{
    public async Task<IReadOnlyList<ParentChild>> GetChildrenAsync(string parentEmail, CancellationToken ct = default)
    {
        var email = Norm(parentEmail);
        var students = await db.Students.IgnoreQueryFilters().Where(s => s.GuardianEmail == email).ToListAsync(ct);
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
        var students = await db.Students.IgnoreQueryFilters().Where(s => s.GuardianEmail == email)
            .ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        if (students.Count == 0) return Array.Empty<ParentPaymentRow>();
        var ids = students.Keys.ToList();
        var payments = await db.Payments.IgnoreQueryFilters().Where(p => ids.Contains(p.StudentId)).ToListAsync(ct);
        return payments.OrderByDescending(p => p.OccurredAtUtc)
            .Select(p => new ParentPaymentRow(p.Id, students.GetValueOrDefault(p.StudentId, "—"), p.AmountKobo, p.OccurredAtUtc)).ToList();
    }

    private async Task<Domain.Enrolment.Student> RequireOwnedStudentAsync(string parentEmail, Guid studentId, CancellationToken ct)
    {
        var email = Norm(parentEmail);
        return await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == studentId && s.GuardianEmail == email, ct)
            ?? throw new NotFoundException("Student not found.");
    }

    private static string Norm(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();
}
