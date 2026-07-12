using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Schools;

namespace PayLibre.Application.Schools;

/// <summary>The school's settlement position at Xental (net kobo) + its configured payout account.</summary>
public sealed record SettlementReport(
    bool Configured, long CollectedKobo, long SettledKobo, long PendingKobo, int VirtualAccounts,
    string? BankName, string? AccountNumber, string? AccountName);

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

    /// <summary>The school's settlement position at Xental (collected/settled/pending) + its payout account.</summary>
    public async Task<SettlementReport> GetSettlementReportAsync(CancellationToken ct = default)
    {
        var school = await GetCurrentAsync(ct);
        if (school.XentalSubMerchantId is not Guid subId)
            return new SettlementReport(false, 0, 0, 0, 0, null, null, null);

        XentalSubMerchantBalance balance;
        try { balance = await xental.GetSubMerchantBalanceAsync(subId, ct); }
        catch (Exception ex) when (ex is not ValidationException)
        {
            throw new UpstreamException($"Could not read the settlement balance from the payment provider: {ex.Message}");
        }
        return new SettlementReport(
            school.SettlementConfigured, balance.CollectedKobo, balance.SettledKobo, balance.PendingKobo,
            balance.VirtualAccounts, school.SettlementBankName, school.SettlementAccountNumber, school.SettlementAccountName);
    }

    /// <summary>Set the school's late-fee policy: a percentage (basis points) of the outstanding balance,
    /// applied once a fee is overdue past <paramref name="graceDays"/>. <c>bps = 0</c> turns late fees off.</summary>
    public async Task<School> UpdateLateFeesAsync(int bps, int graceDays, CancellationToken ct = default)
    {
        var school = await GetCurrentAsync(ct);
        school.ConfigureLateFees(bps, graceDays);
        await db.SaveChangesAsync(ct);
        return school;
    }

    /// <summary>Replace a class teacher's assigned classes. Takes effect on their next sign-in (the class
    /// ids are carried in the session token).</summary>
    public async Task<int> SetUserClassesAsync(Guid userId, IReadOnlyList<Guid> classIds, CancellationToken ct = default)
    {
        var schoolId = tenant.RequireTenantId();
        var user = await db.SchoolUsers.FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new NotFoundException("User not found.");
        if (user.Role != SchoolRole.ClassTeacher)
            throw new ValidationException("Only a class teacher has class assignments.");
        var ids = (classIds ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new ValidationException("Assign at least one class.");
        if (await db.Classes.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count)
            throw new ValidationException("One or more classes do not exist.");

        var existing = await db.SchoolUserClasses.Where(uc => uc.SchoolUserId == userId).ToListAsync(ct);
        db.SchoolUserClasses.RemoveRange(existing);
        foreach (var id in ids) db.SchoolUserClasses.Add(new SchoolUserClass(schoolId, userId, id));
        await db.SaveChangesAsync(ct);
        return ids.Count;
    }
}
