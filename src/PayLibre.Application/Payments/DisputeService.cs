using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Payments;

namespace PayLibre.Application.Payments;

/// <summary>
/// Payment disputes. Parents raise them (global, keyed by guardian email); school staff list + resolve
/// them (tenant-scoped). One open dispute per payment.
/// </summary>
public sealed class DisputeService(IApplicationDbContext db, ITenantContext tenant, IClock clock)
{
    // ---- Parent side (global) ----
    public async Task<PaymentDispute> RaiseAsync(string parentEmail, Guid paymentId, string reason, CancellationToken ct = default)
    {
        var email = (parentEmail ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationException("A reason is required.");
        var payment = await db.Payments.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException("Payment not found.");
        var owns = await db.Students.IgnoreQueryFilters()
            .AnyAsync(s => s.Id == payment.StudentId && s.GuardianEmail == email, ct);
        if (!owns) throw new NotFoundException("Payment not found.");

        if (await db.PaymentDisputes.IgnoreQueryFilters().AnyAsync(d => d.PaymentId == paymentId && d.Status == DisputeStatus.Open, ct))
            throw new ConflictException("There is already an open dispute for this payment.");

        var dispute = new PaymentDispute(payment.SchoolId, paymentId, email, reason);
        db.PaymentDisputes.Add(dispute);
        await db.SaveChangesAsync(ct);
        return dispute;
    }

    // ---- Staff side (tenant-scoped) ----
    public async Task<IReadOnlyList<PaymentDispute>> ListAsync(bool openOnly, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var all = await db.PaymentDisputes.AsNoTracking().ToListAsync(ct);
        if (openOnly) all = all.Where(d => d.Status == DisputeStatus.Open).ToList();
        return all.OrderByDescending(d => d.CreatedAtUtc).ToList();
    }

    public async Task<PaymentDispute> ResolveAsync(Guid id, bool accepted, string? resolution, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var dispute = await db.PaymentDisputes.FirstOrDefaultAsync(d => d.Id == id, ct)
            ?? throw new NotFoundException("Dispute not found.");
        dispute.Resolve(accepted, resolution, tenant.UserEmail, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return dispute;
    }
}
