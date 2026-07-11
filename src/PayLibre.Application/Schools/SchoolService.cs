using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Schools;

/// <summary>Reads/updates the current school's profile (tenant-scoped).</summary>
public sealed class SchoolService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IXentalClient xental,
    IOptions<PayLibreOptions> options)
{
    private readonly PayLibreOptions _options = options.Value;

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

    /// <summary>
    /// Set (or change) the school's payout account. This is where collected fees settle. We push the
    /// account to the school's Xental sub-merchant as its payout destination (Xental resolves + returns
    /// the account holder name), then persist it. Replaces asking for these details at registration.
    /// </summary>
    public async Task<School> UpdateSettlementAsync(string bankName, string bankCode, string accountNumber, CancellationToken ct = default)
    {
        var school = await GetCurrentAsync(ct);
        if (school.XentalSubMerchantId is not Guid subId)
            throw new ValidationException("This school is not yet linked to the payment provider.");

        XentalSubMerchant sub;
        try
        {
            sub = await xental.SetSubMerchantPayoutAsync(subId, bankName, bankCode, accountNumber, _options.PlatformFeeBps, ct);
        }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new UpstreamException($"Could not set the payout account with the payment provider: {ex.Message}");
        }

        school.SetSettlement(bankName, bankCode, accountNumber, sub.SettlementAccountName);
        await db.SaveChangesAsync(ct);
        return school;
    }
}
