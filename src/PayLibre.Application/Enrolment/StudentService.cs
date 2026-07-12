using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Enrolment;

public sealed record StudentInput(
    string AdmissionNo, string FullName, Guid ClassId, string? Session,
    string GuardianName, string? GuardianPhone, string? GuardianEmail);

public sealed record ImportError(int Row, string Message);
public sealed record ImportResult(int Created, int Failed, IReadOnlyList<ImportError> Errors);

/// <summary>
/// Manages students. Creating a student provisions a Xental dedicated virtual account (DVA); the
/// account details are cached on the student for display and delivery to the guardian.
/// </summary>
public sealed class StudentService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IXentalClient xental,
    INotificationSender notifier,
    IOptions<PayLibreOptions> options)
{
    private readonly PayLibreOptions _options = options.Value;

    // A class teacher only sees students in their assigned classes.
    private bool IsClassTeacher => string.Equals(tenant.Role, "ClassTeacher", StringComparison.Ordinal);

    public async Task<IReadOnlyList<Student>> ListAsync(Guid? classId, StudentStatus? status, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var q = db.Students.AsNoTracking().AsQueryable();
        if (classId is Guid cid) q = q.Where(s => s.ClassId == cid);
        if (status is StudentStatus st) q = q.Where(s => s.Status == st);
        if (IsClassTeacher) { var scope = tenant.AssignedClassIds; q = q.Where(s => scope.Contains(s.ClassId)); }
        return await q.OrderBy(s => s.FullName).ToListAsync(ct);
    }

    public async Task<Student> GetAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException($"Student '{id}' not found.");
        if (IsClassTeacher && !tenant.AssignedClassIds.Contains(student.ClassId))
            throw new NotFoundException($"Student '{id}' not found.");
        return student;
    }

    /// <summary>Create or update a student keyed by admission number (public-API sync). New students are
    /// provisioned a virtual account; existing ones are updated in place. Returns the student + whether created.</summary>
    public async Task<(Student Student, bool Created)> UpsertByAdmissionNoAsync(StudentInput input, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var admissionNo = (input.AdmissionNo ?? string.Empty).Trim();
        if (admissionNo.Length == 0) throw new ValidationException("An admission number is required.");
        var existing = await db.Students.FirstOrDefaultAsync(s => s.AdmissionNo == admissionNo, ct);
        if (existing is null) return (await CreateAsync(input, ct), true);

        var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == input.ClassId, ct)
            ?? throw new ValidationException("The selected class does not exist.");
        var session = string.IsNullOrWhiteSpace(input.Session) ? klass.Session : input.Session!.Trim();
        existing.Update(input.FullName, klass.Id, session, input.GuardianName, input.GuardianPhone, input.GuardianEmail);
        await db.SaveChangesAsync(ct);
        return (existing, false);
    }

    /// <summary>Add an additional guardian (multi-guardian). That guardian's parent account can then
    /// view + pay for the student.</summary>
    public async Task<StudentGuardian> AddGuardianAsync(Guid studentId, string email, string? name, string? phone, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var normalized = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ValidationException("A guardian email is required.");
        if (!await db.Students.AnyAsync(s => s.Id == studentId, ct)) throw new NotFoundException("Student not found.");
        if (await db.StudentGuardians.AnyAsync(g => g.StudentId == studentId && g.Email == normalized, ct))
            throw new ConflictException("This guardian is already linked to the student.");
        var guardian = new StudentGuardian(tenantId, studentId, normalized, name, phone);
        db.StudentGuardians.Add(guardian);
        await db.SaveChangesAsync(ct);
        return guardian;
    }

    public async Task<IReadOnlyList<StudentGuardian>> ListGuardiansAsync(Guid studentId, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return await db.StudentGuardians.AsNoTracking().Where(g => g.StudentId == studentId).ToListAsync(ct);
    }

    public async Task RemoveGuardianAsync(Guid guardianId, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var g = await db.StudentGuardians.FirstOrDefaultAsync(x => x.Id == guardianId, ct)
            ?? throw new NotFoundException("Guardian link not found.");
        db.StudentGuardians.Remove(g);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>A student (by admission number) plus their total outstanding fees (kobo).</summary>
    public async Task<(Student Student, long OutstandingKobo)> GetWithOutstandingAsync(string admissionNo, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        admissionNo = (admissionNo ?? string.Empty).Trim();
        var student = await db.Students.FirstOrDefaultAsync(s => s.AdmissionNo == admissionNo, ct)
            ?? throw new NotFoundException($"Student '{admissionNo}' not found.");
        var fees = await db.StudentFees.AsNoTracking().Where(f => f.StudentId == student.Id).ToListAsync(ct);
        return (student, fees.Sum(f => f.OutstandingKobo));
    }

    /// <summary>Term rollover: move the given students into a target class (+ session). Returns the count moved.</summary>
    public async Task<int> PromoteAsync(IReadOnlyList<Guid> studentIds, Guid toClassId, string? session, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        if (studentIds is null || studentIds.Count == 0) throw new ValidationException("Select at least one student.");
        var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == toClassId, ct)
            ?? throw new ValidationException("The target class does not exist.");
        var newSession = string.IsNullOrWhiteSpace(session) ? klass.Session : session!.Trim();

        var students = await db.Students.Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var s in students) s.Promote(klass.Id, newSession);
        await db.SaveChangesAsync(ct);
        return students.Count;
    }

    /// <summary>Bulk activate/deactivate students. Returns the count updated.</summary>
    public async Task<int> BulkSetStatusAsync(IReadOnlyList<Guid> studentIds, StudentStatus status, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        if (studentIds is null || studentIds.Count == 0) throw new ValidationException("Select at least one student.");
        var students = await db.Students.Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
        foreach (var s in students)
        {
            if (status == StudentStatus.Inactive) s.Deactivate(); else s.Reactivate();
        }
        await db.SaveChangesAsync(ct);
        return students.Count;
    }

    /// <summary>Export students as CSV (roster + account + guardian). Tenant-scoped.</summary>
    public async Task<string> ExportCsvAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var students = (await db.Students.AsNoTracking().ToListAsync(ct))
            .OrderBy(s => s.FullName).ToList();
        var classNames = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("AdmissionNo,FullName,Class,Session,Status,GuardianName,GuardianEmail,GuardianPhone,Nuban,BankName");
        foreach (var s in students)
        {
            var cls = classNames.TryGetValue(s.ClassId, out var n) ? n : "";
            sb.AppendLine(string.Join(",", new[]
            {
                Csv(s.AdmissionNo), Csv(s.FullName), Csv(cls), Csv(s.Session), Csv(s.Status.ToString()),
                Csv(s.GuardianName), Csv(s.GuardianEmail), Csv(s.GuardianPhone), Csv(s.Nuban), Csv(s.BankName),
            }));
        }
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        v ??= "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n')
            ? "\"" + v.Replace("\"", "\"\"") + "\""
            : v;
    }

    public async Task<Student> CreateAsync(StudentInput input, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var school = await RequireLinkedSchoolAsync(tenantId, ct);

        var admissionNo = (input.AdmissionNo ?? string.Empty).Trim();
        if (admissionNo.Length == 0) throw new ValidationException("An admission number is required.");
        if (await db.Students.AnyAsync(s => s.AdmissionNo == admissionNo, ct))
            throw new ConflictException($"A student with admission number '{admissionNo}' already exists.");

        var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == input.ClassId, ct)
            ?? throw new ValidationException("The selected class does not exist.");
        var session = string.IsNullOrWhiteSpace(input.Session) ? klass.Session : input.Session!.Trim();

        var student = new Student(tenantId, admissionNo, input.FullName, klass.Id, session,
            input.GuardianName, input.GuardianPhone, input.GuardianEmail);

        await ProvisionAccountAsync(student, school, ct); // throws on upstream failure -> nothing persisted
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return student;
    }

    public async Task<Student> UpdateAsync(Guid id, StudentInput input, CancellationToken ct = default)
    {
        var student = await GetAsync(id, ct);
        var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == input.ClassId, ct)
            ?? throw new ValidationException("The selected class does not exist.");
        var session = string.IsNullOrWhiteSpace(input.Session) ? klass.Session : input.Session!.Trim();
        student.Update(input.FullName, klass.Id, session, input.GuardianName, input.GuardianPhone, input.GuardianEmail);
        await db.SaveChangesAsync(ct);
        return student;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var student = await GetAsync(id, ct);
        db.Students.Remove(student);
        await db.SaveChangesAsync(ct);
    }

    public async Task SendAccountDetailsAsync(Guid id, CancellationToken ct = default)
    {
        var student = await GetAsync(id, ct);
        if (!student.HasVirtualAccount)
            throw new ConflictException("This student has no virtual account yet.");
        await notifier.SendVirtualAccountDetailsAsync(
            student.GuardianName, student.GuardianEmail, student.GuardianPhone,
            student.FullName, student.Nuban!, student.BankName!, student.AccountName!, ct);
    }

    /// <summary>Bulk-create students from a CSV. Header:
    /// <c>AdmissionNo,FullName,Class,Session,GuardianName,GuardianPhone,GuardianEmail</c>.
    /// Each valid row provisions a DVA; bad rows are collected and reported, good rows still commit.</summary>
    public async Task<ImportResult> ImportCsvAsync(string csv, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var school = await RequireLinkedSchoolAsync(tenantId, ct);

        var rows = CsvReader.Parse(csv);
        if (rows.Count == 0) throw new ValidationException("The CSV file is empty.");
        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(string name) => header.IndexOf(name);
        var (iAdm, iName, iClass, iSession, iGName, iGPhone, iGEmail) =
            (Col("admissionno"), Col("fullname"), Col("class"), Col("session"), Col("guardianname"), Col("guardianphone"), Col("guardianemail"));
        if (iAdm < 0 || iName < 0 || iClass < 0 || iGName < 0)
            throw new ValidationException("CSV must have at least the columns: AdmissionNo, FullName, Class, GuardianName.");

        var dataRows = rows.Skip(1).ToList();
        if (dataRows.Count > _options.MaxCsvRows)
            throw new ValidationException($"Too many rows ({dataRows.Count}); the limit is {_options.MaxCsvRows}.");

        var classes = await db.Classes.AsNoTracking().ToListAsync(ct);
        var existing = (await db.Students.AsNoTracking().Select(s => s.AdmissionNo).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? Cell(IReadOnlyList<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : null;
        var errors = new List<ImportError>();
        var created = 0;

        for (var i = 0; i < dataRows.Count; i++)
        {
            var rowNo = i + 2; // 1-based, +1 for header
            var r = dataRows[i];
            if (r.All(string.IsNullOrWhiteSpace)) continue; // skip blank lines
            try
            {
                var adm = Cell(r, iAdm) ?? "";
                var fullName = Cell(r, iName) ?? "";
                var className = Cell(r, iClass) ?? "";
                var session = Cell(r, iSession);
                var gName = Cell(r, iGName) ?? "";
                if (adm.Length == 0 || fullName.Length == 0 || className.Length == 0 || gName.Length == 0)
                { errors.Add(new ImportError(rowNo, "Missing required cell (AdmissionNo/FullName/Class/GuardianName).")); continue; }
                if (!existing.Add(adm))
                { errors.Add(new ImportError(rowNo, $"Duplicate admission number '{adm}'.")); continue; }

                var klass = classes.FirstOrDefault(c => c.Name.Equals(className, StringComparison.OrdinalIgnoreCase)
                    && (string.IsNullOrWhiteSpace(session) || c.Session.Equals(session, StringComparison.OrdinalIgnoreCase)));
                if (klass is null)
                { errors.Add(new ImportError(rowNo, $"Class '{className}' not found — add it first.")); continue; }

                var student = new Student(tenantId, adm, fullName, klass.Id,
                    string.IsNullOrWhiteSpace(session) ? klass.Session : session!, gName, Cell(r, iGPhone), Cell(r, iGEmail));
                await ProvisionAccountAsync(student, school, ct);
                db.Students.Add(student);
                created++;
            }
            catch (Exception ex)
            {
                errors.Add(new ImportError(rowNo, ex.Message));
            }
        }

        if (created > 0) await db.SaveChangesAsync(ct);
        return new ImportResult(created, errors.Count, errors);
    }

    private async Task<School> RequireLinkedSchoolAsync(Guid tenantId, CancellationToken ct)
    {
        var school = await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == tenantId, ct)
            ?? throw new NotFoundException("School not found.");
        if (string.IsNullOrWhiteSpace(school.XentalSubMerchantRef))
            throw new ConflictException("Your school's settlement setup with the payment provider is incomplete.");
        return school;
    }

    private async Task ProvisionAccountAsync(Student student, School school, CancellationToken ct)
    {
        try
        {
            var va = await xental.CreateVirtualAccountAsync(
                student.BuildAccountRef(), student.FullName, school.XentalSubMerchantRef!,
                student.GuardianEmail, student.GuardianPhone, expectedAmountKobo: null, ct);
            student.AttachVirtualAccount(va.AccountRef, va.AccountNumber, va.BankName, va.AccountName);
        }
        catch (Exception ex) when (ex is not ValidationException and not ConflictException)
        {
            throw new UpstreamException($"Could not provision a virtual account: {ex.Message}");
        }
    }
}
