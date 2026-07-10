using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Schools;

/// <summary>Reads/updates the current school's profile (tenant-scoped).</summary>
public sealed class SchoolService(IApplicationDbContext db, ITenantContext tenant)
{
    public async Task<School> GetCurrentAsync(CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        return await db.Schools.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == tenantId, ct)
            ?? throw new NotFoundException("School not found.");
    }

    public async Task<SchoolUser> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        return await db.SchoolUsers.FirstOrDefaultAsync(u => u.Id == userId && u.SchoolId == tenantId, ct)
            ?? throw new NotFoundException("User not found.");
    }
}
