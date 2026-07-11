using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Fees;

namespace PayLibre.Application.Fees;

public sealed record FeeCategoryInput(string Name);

/// <summary>Manages a school's fee categories (Tuition, PTA, …). Tenant-scoped.</summary>
public sealed class FeeCategoryService(IApplicationDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<FeeCategory>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return await db.FeeCategories.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
    }

    public async Task<FeeCategory> CreateAsync(FeeCategoryInput input, CancellationToken ct = default)
    {
        var tenantId = tenant.RequireTenantId();
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0) throw new ValidationException("A category name is required.");
        if (await db.FeeCategories.AnyAsync(c => c.Name == name, ct))
            throw new ConflictException($"A category named '{name}' already exists.");
        var category = new FeeCategory(tenantId, name);
        db.FeeCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<FeeCategory> UpdateAsync(Guid id, FeeCategoryInput input, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var category = await db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Category '{id}' not found.");
        category.Rename(input.Name);
        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var category = await db.FeeCategories.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Category '{id}' not found.");
        if (await db.Fees.AnyAsync(f => f.FeeCategoryId == id, ct))
            throw new ConflictException("Cannot delete a category that still has fees.");
        db.FeeCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }
}
