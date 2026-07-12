using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Application.Maintenance;

/// <summary>
/// Applies overdue late-fee surcharges across all schools. A one-time percentage of the outstanding
/// balance is added to each unpaid invoice once it is past its due date + the school's grace period,
/// but only for schools that have late fees configured and fees that opt in. Idempotent: a surcharge
/// is applied at most once per invoice (tracked by <c>LateFeeAppliedKobo</c>). Platform-global — runs
/// with no tenant context, so every query bypasses the tenant filter.
/// </summary>
public sealed class LateFeeService(IApplicationDbContext db, IClock clock)
{
    public async Task<int> ApplyDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        // Schools with late fees on → their rate + grace.
        var schools = await db.Schools.IgnoreQueryFilters()
            .Where(s => s.LateFeeBps > 0)
            .Select(s => new { s.Id, s.LateFeeBps, s.LateFeeGraceDays })
            .ToListAsync(ct);
        if (schools.Count == 0) return 0;
        var policy = schools.ToDictionary(s => s.Id, s => (bps: s.LateFeeBps, grace: s.LateFeeGraceDays));
        var schoolIds = policy.Keys.ToHashSet();

        // Unpaid, past-due invoices in those schools that haven't been surcharged yet.
        var candidates = (await db.StudentFees.IgnoreQueryFilters()
                .Where(sf => sf.LateFeeAppliedKobo == 0
                    && sf.AmountPaidKobo < sf.AmountKobo
                    && schoolIds.Contains(sf.SchoolId))
                .ToListAsync(ct))
            .Where(sf => sf.DueDateUtc < now)
            .ToList();
        if (candidates.Count == 0) return 0;

        // Which of those fees opt in to late fees.
        var feeIds = candidates.Select(sf => sf.FeeId).Distinct().ToList();
        var appliesByFee = (await db.Fees.IgnoreQueryFilters()
                .Where(f => feeIds.Contains(f.Id))
                .Select(f => new { f.Id, f.AppliesLateFee })
                .ToListAsync(ct))
            .ToDictionary(f => f.Id, f => f.AppliesLateFee);

        var applied = 0;
        foreach (var sf in candidates)
        {
            if (!appliesByFee.TryGetValue(sf.FeeId, out var opt) || !opt) continue;
            var (bps, grace) = policy[sf.SchoolId];
            if (now < sf.DueDateUtc.AddDays(grace)) continue;                 // still within grace
            var surcharge = (long)Math.Round(sf.OutstandingKobo * (bps / 10_000.0), MidpointRounding.AwayFromZero);
            if (sf.ApplyLateFee(surcharge, now)) applied++;
        }

        if (applied > 0) await db.SaveChangesAsync(ct);
        return applied;
    }
}
