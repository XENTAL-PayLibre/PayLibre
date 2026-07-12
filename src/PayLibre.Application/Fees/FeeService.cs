using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Fees;

namespace PayLibre.Application.Fees;

public sealed record FeeSpec(string Name, Guid FeeCategoryId, Guid ClassId, string? Session, string Term, long AmountKobo, DateTimeOffset DueDateUtc, bool AppliesLateFee = true);

/// <summary>A fee plus its rolled-up collection figures.</summary>
public sealed record FeeStats(Fee Fee, string CategoryName, string ClassName, int Students, long InvoicedKobo, long CollectedKobo, long OutstandingKobo);

/// <summary>Headline totals across all fees (Fees page cards).</summary>
public sealed record FeeSummary(long TotalInvoicedKobo, long CollectedKobo, long OutstandingKobo, int Fees, int StudentFees);

/// <summary>One student's invoice for a fee, with the student's display fields.</summary>
public sealed record StudentFeeRow(StudentFee Invoice, string StudentName, string AdmissionNo, string ClassName);

/// <summary>
/// Fee definitions and their per-student invoices. Creating a fee <b>fans out</b> one
/// <see cref="StudentFee"/> per active student in the class. Tenant-scoped; money in kobo.
/// </summary>
public sealed class FeeService(IApplicationDbContext db, ITenantContext tenant, IClock clock)
{
    public async Task<(Fee Fee, int InvoicesCreated)> CreateAsync(FeeSpec spec, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var name = (spec.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ValidationException("A fee name is required.");
        if (spec.AmountKobo <= 0) throw new ValidationException("Amount must be greater than zero.");
        if (!Enum.TryParse<Term>(spec.Term, ignoreCase: true, out var term))
            throw new ValidationException($"Unknown term '{spec.Term}'. Use First, Second, or Third.");

        if (!await db.FeeCategories.AnyAsync(c => c.Id == spec.FeeCategoryId, ct))
            throw new ValidationException("The selected fee category does not exist.");
        var klass = await db.Classes.FirstOrDefaultAsync(c => c.Id == spec.ClassId, ct)
            ?? throw new ValidationException("The selected class does not exist.");
        var session = string.IsNullOrWhiteSpace(spec.Session) ? klass.Session : spec.Session!.Trim();

        var now = clock.UtcNow;
        var fee = new Fee(tenantId, name, spec.FeeCategoryId, klass.Id, session, term, spec.AmountKobo, spec.DueDateUtc, spec.AppliesLateFee);
        db.Fees.Add(fee);

        // Fan out one invoice per active student in the class.
        var students = await db.Students
            .Where(s => s.ClassId == klass.Id && s.Status == StudentStatus.Active)
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var studentId in students)
            db.StudentFees.Add(new StudentFee(tenantId, fee.Id, studentId, fee.AmountKobo, fee.DueDateUtc, now));

        await db.SaveChangesAsync(ct);
        return (fee, students.Count);
    }

    public async Task<IReadOnlyList<FeeStats>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var fees = (await db.Fees.AsNoTracking().ToListAsync(ct)).OrderByDescending(f => f.CreatedAtUtc).ToList();
        var categories = await db.FeeCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var classes = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var agg = await db.StudentFees.AsNoTracking()
            .GroupBy(sf => sf.FeeId)
            .Select(g => new { FeeId = g.Key, Students = g.Count(), Invoiced = g.Sum(x => x.AmountKobo), Collected = g.Sum(x => x.AmountPaidKobo) })
            .ToDictionaryAsync(x => x.FeeId, ct);

        return fees.Select(f =>
        {
            agg.TryGetValue(f.Id, out var a);
            var invoiced = a?.Invoiced ?? 0;
            var collected = a?.Collected ?? 0;
            return new FeeStats(f, categories.GetValueOrDefault(f.FeeCategoryId, "—"), classes.GetValueOrDefault(f.ClassId, "—"),
                a?.Students ?? 0, invoiced, collected, Math.Max(0, invoiced - collected));
        }).ToList();
    }

    public async Task<(Fee Fee, IReadOnlyList<StudentFeeRow> Invoices)> GetAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var fee = await db.Fees.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException($"Fee '{id}' not found.");
        var invoices = await db.StudentFees.AsNoTracking().Where(sf => sf.FeeId == id).ToListAsync(ct);
        var studentIds = invoices.Select(i => i.StudentId).ToList();
        var students = await db.Students.AsNoTracking().Where(s => studentIds.Contains(s.Id)).ToListAsync(ct);
        var classes = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var rows = invoices
            .Select(sf =>
            {
                var s = students.FirstOrDefault(x => x.Id == sf.StudentId);
                return new StudentFeeRow(sf, s?.FullName ?? "—", s?.AdmissionNo ?? "—",
                    s is null ? "—" : classes.GetValueOrDefault(s.ClassId, "—"));
            })
            .OrderBy(r => r.StudentName)
            .ToList();
        return (fee, (IReadOnlyList<StudentFeeRow>)rows);
    }

    public async Task<FeeSummary> SummaryAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var fees = await db.Fees.CountAsync(ct);
        var invoiced = await db.StudentFees.SumAsync(x => (long?)x.AmountKobo, ct) ?? 0;
        var collected = await db.StudentFees.SumAsync(x => (long?)x.AmountPaidKobo, ct) ?? 0;
        var count = await db.StudentFees.CountAsync(ct);
        return new FeeSummary(invoiced, collected, Math.Max(0, invoiced - collected), fees, count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var fee = await db.Fees.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new NotFoundException($"Fee '{id}' not found.");
        if (await db.StudentFees.AnyAsync(sf => sf.FeeId == id && sf.AmountPaidKobo > 0, ct))
            throw new ConflictException("Cannot delete a fee that already has payments against it.");
        var invoices = await db.StudentFees.Where(sf => sf.FeeId == id).ToListAsync(ct);
        db.StudentFees.RemoveRange(invoices);
        db.Fees.Remove(fee);
        await db.SaveChangesAsync(ct);
    }
}
