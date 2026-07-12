using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Application.Maintenance;

/// <summary>
/// Sends fee reminders (dunning) across all schools: 3 days before due, on the due date, then weekly
/// while overdue (capped). Each invoice advances through named stages and never repeats one, so the
/// job is safe to run on any cadence. Platform-global — runs with no tenant context (bypasses the
/// tenant filter). Only invoices with a guardian contact are reminded.
/// </summary>
public sealed class ReminderService(IApplicationDbContext db, IClock clock, INotificationSender notifier)
{
    private const int MaxOverdueReminders = 4; // weekly overdue reminders cap (od0..od4)

    /// <summary>The reminder stage due for an invoice at <paramref name="now"/>, or null if none is due yet.</summary>
    public static string? StageFor(DateTimeOffset dueDateUtc, DateTimeOffset now)
    {
        if (now < dueDateUtc)
        {
            var daysToDue = (dueDateUtc - now).TotalDays;
            return daysToDue <= 3 ? "pre" : null;         // remind from 3 days before
        }
        var overdueDays = (int)(now - dueDateUtc).TotalDays;
        var idx = Math.Min(overdueDays / 7, MaxOverdueReminders); // od0 (due..+6d), od1 (+7d), … capped
        return $"od{idx}";
    }

    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var open = await db.StudentFees.IgnoreQueryFilters()
            .Where(sf => sf.AmountPaidKobo < sf.AmountKobo)
            .ToListAsync(ct);
        if (open.Count == 0) return 0;

        var studentIds = open.Select(sf => sf.StudentId).Distinct().ToList();
        var feeIds = open.Select(sf => sf.FeeId).Distinct().ToList();
        var students = (await db.Students.IgnoreQueryFilters()
                .Where(s => studentIds.Contains(s.Id)).ToListAsync(ct))
            .ToDictionary(s => s.Id);
        var feeNames = (await db.Fees.IgnoreQueryFilters()
                .Where(f => feeIds.Contains(f.Id)).Select(f => new { f.Id, f.Name }).ToListAsync(ct))
            .ToDictionary(f => f.Id, f => f.Name);

        var sent = 0;
        foreach (var sf in open)
        {
            var stage = StageFor(sf.DueDateUtc, now);
            if (stage is null || stage == sf.LastReminderStage) continue;      // nothing new to send
            if (!students.TryGetValue(sf.StudentId, out var student)) continue;
            if (string.IsNullOrWhiteSpace(student.GuardianEmail) && string.IsNullOrWhiteSpace(student.GuardianPhone))
                continue;                                                       // no contact — try again once one exists

            var feeName = feeNames.TryGetValue(sf.FeeId, out var n) ? n : "School fee";
            var overdue = now >= sf.DueDateUtc;
            try
            {
                await notifier.SendFeeReminderAsync(
                    student.GuardianName, student.GuardianEmail, student.GuardianPhone,
                    student.FullName, feeName, sf.OutstandingKobo, sf.DueDateUtc, overdue, ct);
                sf.RecordReminder(stage, now);
                sent++;
            }
            catch { /* best-effort; retry on the next run */ }
        }

        if (sent > 0) await db.SaveChangesAsync(ct);
        return sent;
    }
}
