using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Enrolment;

public sealed record EnrolContextClass(Guid Id, string Name, string Session);
public sealed record EnrolContext(string SchoolName, IReadOnlyList<EnrolContextClass> Classes);
public sealed record SelfEnrolInput(string FullName, Guid ClassId, string GuardianName, string? GuardianPhone, string? GuardianEmail);

/// <summary>
/// Public, code-based parent self-enrolment — no login. A parent uses the school's join code to add
/// their child; a dedicated virtual account is provisioned immediately. Resolves the school by code
/// (no tenant context), so it bypasses the tenant query filter deliberately.
/// </summary>
public sealed class SelfEnrolmentService(IApplicationDbContext db, IXentalClient xental, INotificationSender notifier, IClock clock)
{
    public async Task<EnrolContext> GetContextAsync(string joinCode, CancellationToken ct = default)
    {
        var school = await ResolveSchoolAsync(joinCode, ct);
        var classes = await db.Classes.IgnoreQueryFilters().Where(c => c.SchoolId == school.Id)
            .OrderBy(c => c.Session).ThenBy(c => c.Name)
            .Select(c => new EnrolContextClass(c.Id, c.Name, c.Session)).ToListAsync(ct);
        return new EnrolContext(school.Name, classes);
    }

    public async Task<Student> EnrolAsync(string joinCode, SelfEnrolInput input, CancellationToken ct = default)
    {
        var school = await ResolveSchoolAsync(joinCode, ct);
        var klass = await db.Classes.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == input.ClassId && c.SchoolId == school.Id, ct)
            ?? throw new ValidationException("The selected class does not exist for this school.");

        // Self-enrolments have no admission number from the office — generate a stable one.
        var admissionNo = $"SE-{DateTimeOffset.UtcNow:yyMM}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var student = new Student(school.Id, admissionNo, input.FullName, klass.Id, klass.Session,
            input.GuardianName, input.GuardianPhone, input.GuardianEmail, selfEnrolled: true);

        try
        {
            var va = await xental.CreateVirtualAccountAsync(
                student.BuildAccountRef(), student.FullName, school.XentalSubMerchantRef!,
                student.GuardianEmail, student.GuardianPhone, expectedAmountKobo: null, ct);
            student.AttachVirtualAccount(va.AccountRef, va.AccountNumber, va.BankName, va.AccountName);
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new UpstreamException($"Could not provision a virtual account: {ex.Message}");
        }

        db.Students.Add(student);
        await db.SaveChangesAsync(ct);

        // Best-effort: send the account details to the guardian.
        try
        {
            await notifier.SendVirtualAccountDetailsAsync(student.GuardianName, student.GuardianEmail, student.GuardianPhone,
                student.FullName, student.Nuban!, student.BankName!, student.AccountName!, ct);
        }
        catch { /* delivery is best-effort */ }

        return student;
    }

    private async Task<School> ResolveSchoolAsync(string joinCode, CancellationToken ct)
    {
        var code = (joinCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0) throw new ValidationException("A school code is required.");
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.JoinCode == code, ct)
            ?? throw new NotFoundException("No school found for that code.");
        if (string.IsNullOrWhiteSpace(school.XentalSubMerchantRef))
            throw new ConflictException("This school isn't ready to accept enrolments yet.");
        return school;
    }
}
