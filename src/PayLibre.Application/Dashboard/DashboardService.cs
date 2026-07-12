using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Enrolment;
using PayLibre.Domain.Fees;

namespace PayLibre.Application.Dashboard;

public sealed record RevenuePoint(string Month, long AmountKobo);
public sealed record RecentPayment(Guid StudentId, string StudentName, string AdmissionNo, long AmountKobo, DateTimeOffset OccurredAtUtc);
public sealed record DashboardOverview(
    int TotalStudents, long InvoicedKobo, long CollectedKobo, long OutstandingKobo, double CollectionRatePct,
    int Fees, int Payments, int PaidInvoices, int OverdueInvoices,
    IReadOnlyList<RevenuePoint> RevenueSeries, IReadOnlyList<RecentPayment> Recent);

public sealed record OutstandingRow(Guid StudentId, string StudentName, string AdmissionNo, string ClassName, long OutstandingKobo);
public sealed record ClassCollection(Guid ClassId, string ClassName, long InvoicedKobo, long CollectedKobo, long OutstandingKobo);
public sealed record TrendPoint(string Month, long CollectedKobo, int Payments);
public sealed record CollectionTrends(IReadOnlyList<TrendPoint> Series, long ForecastNextMonthKobo);

/// <summary>Dashboard aggregation + collection reports for a school (tenant-scoped).</summary>
public sealed class DashboardService(IApplicationDbContext db, ITenantContext tenant, IClock clock)
{
    public async Task<DashboardOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var students = await db.Students.CountAsync(s => s.Status == StudentStatus.Active, ct);
        var invoiced = await db.StudentFees.SumAsync(x => (long?)x.AmountKobo, ct) ?? 0;
        var collected = await db.StudentFees.SumAsync(x => (long?)x.AmountPaidKobo, ct) ?? 0;
        var fees = await db.Fees.CountAsync(ct);
        var payments = await db.Payments.CountAsync(ct);
        var paid = await db.StudentFees.CountAsync(x => x.Status == FeeStatus.Paid, ct);
        var overdue = await db.StudentFees.CountAsync(x => x.Status == FeeStatus.Overdue, ct);

        // Recent payments + revenue series (order/group in memory — SQLite can't ORDER BY DateTimeOffset).
        var allPayments = await db.Payments.AsNoTracking().ToListAsync(ct);
        var recentRaw = allPayments.OrderByDescending(p => p.OccurredAtUtc).Take(10).ToList();
        var ids = recentRaw.Select(p => p.StudentId).Distinct().ToList();
        var names = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => new { s.FullName, s.AdmissionNo }, ct);
        var recent = recentRaw.Select(p =>
        {
            names.TryGetValue(p.StudentId, out var n);
            return new RecentPayment(p.StudentId, n?.FullName ?? "—", n?.AdmissionNo ?? "—", p.AmountKobo, p.OccurredAtUtc);
        }).ToList();

        var series = BuildRevenueSeries(allPayments, clock.UtcNow, months: 6);
        var rate = invoiced > 0 ? Math.Round(100.0 * collected / invoiced, 1) : 0;
        return new DashboardOverview(students, invoiced, collected, Math.Max(0, invoiced - collected), rate,
            fees, payments, paid, overdue, series, recent);
    }

    public async Task<IReadOnlyList<OutstandingRow>> OutstandingAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var byStudent = (await db.StudentFees.AsNoTracking().Where(sf => sf.AmountPaidKobo < sf.AmountKobo).ToListAsync(ct))
            .GroupBy(sf => sf.StudentId)
            .Select(g => new { StudentId = g.Key, Outstanding = g.Sum(x => x.AmountKobo - x.AmountPaidKobo) })
            .Where(x => x.Outstanding > 0).ToList();
        var ids = byStudent.Select(x => x.StudentId).ToList();
        var students = await db.Students.AsNoTracking().Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        var classes = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return byStudent.Select(x =>
        {
            var s = students.First(y => y.Id == x.StudentId);
            return new OutstandingRow(s.Id, s.FullName, s.AdmissionNo, classes.GetValueOrDefault(s.ClassId, "—"), x.Outstanding);
        }).OrderByDescending(r => r.OutstandingKobo).ToList();
    }

    public async Task<IReadOnlyList<ClassCollection>> CollectionsByClassAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var feeClass = await db.Fees.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.ClassId, ct);
        var classes = await db.Classes.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var invoices = await db.StudentFees.AsNoTracking().ToListAsync(ct);
        return invoices
            .Where(sf => feeClass.ContainsKey(sf.FeeId))
            .GroupBy(sf => feeClass[sf.FeeId])
            .Select(g => new ClassCollection(g.Key, classes.GetValueOrDefault(g.Key, "—"),
                g.Sum(x => x.AmountKobo), g.Sum(x => x.AmountPaidKobo),
                Math.Max(0, g.Sum(x => x.AmountKobo) - g.Sum(x => x.AmountPaidKobo))))
            .OrderBy(c => c.ClassName).ToList();
    }

    /// <summary>Monthly collection trend over the last <paramref name="months"/> months + a simple
    /// next-month forecast (average of the last up-to-3 months).</summary>
    public async Task<CollectionTrends> GetTrendsAsync(int months = 12, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        months = Math.Clamp(months, 1, 24);
        var payments = await db.Payments.AsNoTracking().ToListAsync(ct);
        var byMonth = payments
            .GroupBy(p => new DateTime(p.OccurredAtUtc.Year, p.OccurredAtUtc.Month, 1))
            .ToDictionary(g => g.Key, g => (Sum: g.Sum(x => x.AmountKobo), Count: g.Count()));

        var now = clock.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));
        var series = new List<TrendPoint>();
        for (var i = 0; i < months; i++)
        {
            var m = start.AddMonths(i);
            var v = byMonth.GetValueOrDefault(m);
            series.Add(new TrendPoint(m.ToString("yyyy-MM", CultureInfo.InvariantCulture), v.Sum, v.Count));
        }
        var tail = series.TakeLast(3).Select(s => s.CollectedKobo).ToList();
        var forecast = tail.Count > 0 ? tail.Sum() / tail.Count : 0;
        return new CollectionTrends(series, forecast);
    }

    private static List<RevenuePoint> BuildRevenueSeries(IEnumerable<Domain.Payments.Payment> payments, DateTimeOffset now, int months)
    {
        var buckets = payments
            .GroupBy(p => new DateTime(p.OccurredAtUtc.Year, p.OccurredAtUtc.Month, 1))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.AmountKobo));
        var series = new List<RevenuePoint>();
        var start = new DateTime(now.Year, now.Month, 1).AddMonths(-(months - 1));
        for (var i = 0; i < months; i++)
        {
            var m = start.AddMonths(i);
            series.Add(new RevenuePoint(m.ToString("yyyy-MM", CultureInfo.InvariantCulture), buckets.GetValueOrDefault(m)));
        }
        return series;
    }
}
