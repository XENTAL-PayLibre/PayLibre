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

        // Discover candidates WITHOUT tracking (projection), so each per-student locked load below is a
        // fresh read — this serializes correctly against concurrent reconciliation on the same student.
        var candidates = (await db.StudentFees.IgnoreQueryFilters().AsNoTracking()
                .Where(sf => sf.LateFeeAppliedKobo == 0
                    && sf.AmountPaidKobo < sf.AmountKobo
                    && schoolIds.Contains(sf.SchoolId))
                .Select(sf => new { sf.Id, sf.SchoolId, sf.FeeId, sf.StudentId, sf.DueDateUtc })
                .ToListAsync(ct))
            .Where(x => x.DueDateUtc < now)
            .ToList();
        if (candidates.Count == 0) return 0;

        // Which of those fees opt in to late fees.
        var feeIds = candidates.Select(x => x.FeeId).Distinct().ToList();
        var appliesByFee = (await db.Fees.IgnoreQueryFilters()
                .Where(f => feeIds.Contains(f.Id))
                .Select(f => new { f.Id, f.AppliesLateFee })
                .ToListAsync(ct))
            .ToDictionary(f => f.Id, f => f.AppliesLateFee);

        var applied = 0;
        foreach (var cand in candidates)
        {
            if (!appliesByFee.TryGetValue(cand.FeeId, out var opt) || !opt) continue;
            var (bps, grace) = policy[cand.SchoolId];
            if (now < cand.DueDateUtc.AddDays(grace)) continue;               // still within grace
            await db.RunSerializedAsync(LockKey(cand.StudentId), async c =>
            {
                var sf = await db.StudentFees.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == cand.Id, c); // fresh, under lock (global job)
                if (sf is null || sf.LateFeeAppliedKobo > 0 || sf.OutstandingKobo <= 0) return;
                var surcharge = (long)Math.Round(sf.OutstandingKobo * (bps / 10_000.0), MidpointRounding.AwayFromZero);
                if (sf.ApplyLateFee(surcharge, now)) { await db.SaveChangesAsync(c); applied++; }
            }, ct);
        }
        return applied;
    }

    private static long LockKey(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
