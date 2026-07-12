using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Audit;

namespace PayLibre.Application.Audit;

/// <summary>
/// Writes and reads the per-school audit trail. <see cref="RecordAsync"/> attributes the action to the
/// current dashboard user (from the tenant context) and persists it immediately. Reads are tenant-scoped
/// by the global query filter, so a school only ever sees its own events.
/// </summary>
public sealed class AuditService(IApplicationDbContext db, ITenantContext tenant)
{
    public async Task RecordAsync(string action, string? entityType, Guid? entityId, string summary, CancellationToken ct = default)
    {
        var schoolId = tenant.RequireTenantId();
        db.AuditEvents.Add(new AuditEvent(schoolId, tenant.UserId, tenant.UserEmail, action, entityType, entityId, summary));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Most-recent events first, capped at <paramref name="take"/> (1–500).</summary>
    public async Task<IReadOnlyList<AuditEvent>> ListAsync(int take = 100, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        take = Math.Clamp(take, 1, 500);
        var events = await db.AuditEvents.AsNoTracking().ToListAsync(ct);
        return events.OrderByDescending(e => e.CreatedAtUtc).Take(take).ToList();
    }
}
