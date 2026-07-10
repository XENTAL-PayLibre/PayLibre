using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Payments;

namespace PayLibre.Application.Payments;

public sealed record PaymentRow(Payment Payment, string StudentName, string AdmissionNo);

/// <summary>Reads a school's received payments (tenant-scoped).</summary>
public sealed class PaymentService(IApplicationDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<PaymentRow>> ListAsync(int take = 100, Guid? studentId = null, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var q = db.Payments.AsNoTracking().AsQueryable();
        if (studentId is Guid sid) q = q.Where(p => p.StudentId == sid);
        var payments = (await q.ToListAsync(ct))
            .OrderByDescending(p => p.OccurredAtUtc)
            .Take(Math.Clamp(take, 1, 500))
            .ToList();

        var ids = payments.Select(p => p.StudentId).Distinct().ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.FullName, s.AdmissionNo }, ct);

        return payments.Select(p =>
        {
            students.TryGetValue(p.StudentId, out var s);
            return new PaymentRow(p, s?.FullName ?? "—", s?.AdmissionNo ?? "—");
        }).ToList();
    }
}
