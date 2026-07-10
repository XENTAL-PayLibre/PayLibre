using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Enrolment;

namespace PayLibre.Application.Enrolment;

public sealed record ClassInput(string Name, string Session);

/// <summary>Manages a school's classes (tenant-scoped).</summary>
public sealed class ClassService(IApplicationDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<Class>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return await db.Classes.AsNoTracking().OrderBy(c => c.Session).ThenBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<Class> GetAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return await db.Classes.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Class '{id}' not found.");
    }

    public async Task<Class> CreateAsync(ClassInput input, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var name = (input.Name ?? string.Empty).Trim();
        var session = (input.Session ?? string.Empty).Trim();
        if (name.Length == 0 || session.Length == 0)
            throw new ValidationException("Class name and session are required.");
        if (await db.Classes.AnyAsync(c => c.Name == name && c.Session == session, ct))
            throw new ConflictException($"Class '{name}' already exists for {session}.");

        var klass = new Class(tenantId, name, session);
        db.Classes.Add(klass);
        await db.SaveChangesAsync(ct);
        return klass;
    }

    public async Task<Class> UpdateAsync(Guid id, ClassInput input, CancellationToken ct = default)
    {
        var klass = await GetAsync(id, ct);
        klass.Rename(input.Name);
        klass.SetSession(input.Session);
        await db.SaveChangesAsync(ct);
        return klass;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var klass = await GetAsync(id, ct);
        if (await db.Students.AnyAsync(s => s.ClassId == id && s.SchoolId == tenantId, ct))
            throw new ConflictException("Cannot delete a class that still has students.");
        db.Classes.Remove(klass);
        await db.SaveChangesAsync(ct);
    }
}
