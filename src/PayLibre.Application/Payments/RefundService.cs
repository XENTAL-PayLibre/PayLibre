using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Payments;

namespace PayLibre.Application.Payments;

/// <summary>
/// Maker-checker refunds. A dashboard user requests a refund for a received payment; a different
/// Owner/Admin approves it, which executes the refund against Xental (idempotent per deposit ref).
/// Tenant-scoped: a school can only refund its own payments.
/// </summary>
public sealed class RefundService(IApplicationDbContext db, ITenantContext tenant, IXentalClient xental, IClock clock)
{
    public async Task<RefundRequest> RequestAsync(Guid paymentId, string? reason, CancellationToken ct = default)
    {
        var schoolId = tenant.RequireTenantId();
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException("Payment not found.");

        var open = await db.RefundRequests.AnyAsync(
            r => r.PaymentId == paymentId && (r.Status == RefundStatus.Requested || r.Status == RefundStatus.Approved || r.Status == RefundStatus.Executed), ct);
        if (open) throw new ConflictException("A refund for this payment is already in progress or completed.");

        var refund = new RefundRequest(schoolId, payment.Id, payment.XentalTransactionRef, tenant.UserId, tenant.UserEmail, reason);
        db.RefundRequests.Add(refund);
        await db.SaveChangesAsync(ct);
        return refund;
    }

    public async Task<RefundRequest> ApproveAsync(Guid refundId, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var refund = await db.RefundRequests.FirstOrDefaultAsync(r => r.Id == refundId, ct)
            ?? throw new NotFoundException("Refund request not found.");

        refund.Approve(tenant.UserId, tenant.UserEmail, clock.UtcNow); // enforces approver != requester

        try
        {
            var result = await xental.RefundTransactionAsync(refund.XentalTransactionRef, null, null, null, ct);
            refund.MarkExecuted(result.AmountKobo, result.TransferRef, result.ProviderReference, clock.UtcNow);
        }
        catch (Exception ex)
        {
            refund.MarkFailed(ex.Message, clock.UtcNow);
            await db.SaveChangesAsync(ct);
            throw new UpstreamException($"Refund approved but the payment provider rejected it: {ex.Message}");
        }

        await db.SaveChangesAsync(ct);
        return refund;
    }

    public async Task<RefundRequest> RejectAsync(Guid refundId, string? note, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var refund = await db.RefundRequests.FirstOrDefaultAsync(r => r.Id == refundId, ct)
            ?? throw new NotFoundException("Refund request not found.");
        refund.Reject(tenant.UserId, tenant.UserEmail, note, clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return refund;
    }

    public async Task<IReadOnlyList<RefundRequest>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return (await db.RefundRequests.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(r => r.CreatedAtUtc).ToList();
    }
}
