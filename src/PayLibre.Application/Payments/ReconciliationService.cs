using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Fees;
using PayLibre.Domain.Payments;

namespace PayLibre.Application.Payments;

public sealed record DepositResult(string Status, Guid? PaymentId, int InvoicesSettled, long AllocatedKobo);

/// <summary>
/// Applies a reconciled deposit (from Xental's webhook) to a student's outstanding fee invoices,
/// <b>oldest-due-first</b>. Platform-global (deposits arrive for any school's student), so it resolves
/// the student across tenants and writes tenant-owned rows scoped to that student's school. Idempotent
/// on the Xental transaction reference. Any surplus beyond outstanding fees is left as unattributed
/// (student credit) — the payment is still recorded in full.
/// </summary>
public sealed class ReconciliationService(
    IApplicationDbContext db, IClock clock, INotificationSender notifier,
    Webhooks.OutboundWebhookService? outbound = null, Parents.PushService? push = null)
{
    public async Task<DepositResult> ProcessDepositAsync(
        string accountRef, long amountKobo, long netCreditKobo, string xentalRef, string? payerName,
        DateTimeOffset occurredAt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(xentalRef)) return new("ignored:no-reference", null, 0, 0);
        if (amountKobo <= 0) return new("ignored:invalid-amount", null, 0, 0); // never burn the ref on a bad amount

        var student = await db.Students.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.XentalAccountRef == accountRef, ct);
        if (student is null) return new("unknown-account", null, 0, 0);

        // Serialize all processing for THIS student so concurrent deposits (or a duplicate of the same
        // deposit) can't lost-update the invoices or race the idempotency check. Lock released at commit.
        DepositResult result = new("duplicate", null, 0, 0);
        long outstanding = 0;
        var settled = 0;
        await db.RunSerializedAsync(LockKey(student.Id), async c =>
        {
            if (await db.Payments.IgnoreQueryFilters().AnyAsync(p => p.XentalTransactionRef == xentalRef, c))
            {
                result = new("duplicate", null, 0, 0);
                return;
            }

            var payment = new Payment(student.SchoolId, student.Id, xentalRef, amountKobo, netCreditKobo, payerName, occurredAt);
            db.Payments.Add(payment);

            var now = clock.UtcNow;
            var open = (await db.StudentFees.IgnoreQueryFilters()
                    .Where(sf => sf.StudentId == student.Id && sf.AmountPaidKobo < sf.AmountKobo)
                    .ToListAsync(c))
                .OrderBy(sf => sf.DueDateUtc).ThenBy(sf => sf.CreatedAtUtc)   // oldest-due-first
                .ToList();

            long remaining = amountKobo, allocated = 0;
            foreach (var sf in open)
            {
                if (remaining <= 0) break;
                var applied = sf.Allocate(remaining, now);
                if (applied <= 0) continue;
                db.FeeAllocations.Add(new FeeAllocation(student.SchoolId, payment.Id, sf.Id, applied));
                remaining -= applied;
                allocated += applied;
                if (sf.Status == FeeStatus.Paid) settled++;
            }

            await db.SaveChangesAsync(c);
            outstanding = open.Sum(sf => Math.Max(0, sf.AmountKobo - sf.AmountPaidKobo));
            result = new("processed", payment.Id, settled, allocated);
        }, ct);

        if (result.Status != "processed") return result;

        // Receipt to the guardian (best-effort — never fail the webhook on a notification error).
        try
        {
            await notifier.SendPaymentReceiptAsync(
                student.GuardianName, student.GuardianEmail, student.GuardianPhone, student.FullName,
                amountKobo, settled, outstanding, occurredAt, ct);
        }
        catch { /* best-effort */ }

        // Notify the school's own systems (best-effort; queued + delivered with retries by the worker).
        if (outbound is not null)
        {
            try
            {
                await outbound.EnqueueAsync(student.SchoolId, "payment.received", new
                {
                    studentId = student.Id,
                    admissionNo = student.AdmissionNo,
                    amountKobo,
                    netCreditKobo,
                    invoicesSettled = settled,
                    outstandingKobo = outstanding,
                    transactionRef = xentalRef,
                    occurredAtUtc = occurredAt,
                }, ct);
            }
            catch { /* best-effort */ }
        }

        // Push the guardian(s) — best-effort, config-gated (no-op until FCM is configured).
        if (push is not null)
        {
            try
            {
                var emails = new List<string>();
                if (!string.IsNullOrWhiteSpace(student.GuardianEmail)) emails.Add(student.GuardianEmail!);
                emails.AddRange(await db.StudentGuardians.IgnoreQueryFilters()
                    .Where(g => g.StudentId == student.Id).Select(g => g.Email).ToListAsync(ct));
                await push.NotifyAsync(emails,
                    $"Payment received for {student.FullName}",
                    $"₦{(amountKobo / 100m):N2} received. Outstanding: ₦{(outstanding / 100m):N2}.", ct);
            }
            catch { /* best-effort */ }
        }

        return result;
    }

    // Derive a stable 64-bit advisory-lock key from a Guid (collisions merely serialize two unrelated ids).
    private static long LockKey(Guid id) => BitConverter.ToInt64(id.ToByteArray(), 0);
}
