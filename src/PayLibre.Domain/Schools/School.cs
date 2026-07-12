using PayLibre.Domain.Common;

namespace PayLibre.Domain.Schools;

public enum SchoolStatus { Pending = 1, Active = 2, Suspended = 3 }

/// <summary>
/// A school — the tenant. Every other entity is owned by a school (its <c>Id</c> is the tenant id).
/// A school maps to a Xental <b>sub-merchant</b>: PayLibre provisions the sub-merchant + payout
/// account so Xental settles collected fees to the school's own bank.
/// </summary>
public sealed class School : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string OfficialEmail { get; private set; } = null!;   // unique; the login identity of the owner
    public string Phone { get; private set; } = null!;
    public SchoolStatus Status { get; private set; }

    // Settlement destination (the school's bank), pushed to Xental as the sub-merchant payout account.
    // Set from inside the app after registration (not collected at sign-up), so these stay null until
    // the school configures where fees should be paid out.
    public string? SettlementBankName { get; private set; }
    public string? SettlementBankCode { get; private set; }
    public string? SettlementAccountNumber { get; private set; }
    public string? SettlementAccountName { get; private set; }

    /// <summary>True once the school has configured a payout account (via the in-app settlement settings).</summary>
    public bool SettlementConfigured => !string.IsNullOrWhiteSpace(SettlementAccountNumber);

    // Late-fee policy: a percentage (basis points) of the outstanding balance, applied once a fee is
    // overdue past the grace period. 0 bps = no late fees.
    public int LateFeeBps { get; private set; }
    public int LateFeeGraceDays { get; private set; }
    public bool LateFeesEnabled => LateFeeBps > 0;

    // Xental handles, cached after provisioning so we never re-derive them.
    public string? XentalSubMerchantRef { get; private set; }
    public Guid? XentalSubMerchantId { get; private set; }

    /// <summary>Short code parents use to self-enrol their child (no login required).</summary>
    public string? JoinCode { get; private set; }
    public void SetJoinCode(string code) => JoinCode = DomainException.Require(code, nameof(code)).ToUpperInvariant();

    private School() { }

    public School(string name, string officialEmail, string phone)
    {
        Name = DomainException.Require(name, nameof(name));
        OfficialEmail = DomainException.Require(officialEmail, nameof(officialEmail)).ToLowerInvariant();
        Phone = DomainException.Require(phone, nameof(phone));
        Status = SchoolStatus.Pending;
    }

    /// <summary>Record the Xental sub-merchant this school was linked to, and its resolved account name.</summary>
    public void LinkXentalSubMerchant(string reference, Guid id, string? settlementAccountName)
    {
        XentalSubMerchantRef = DomainException.Require(reference, nameof(reference));
        XentalSubMerchantId = id;
        if (!string.IsNullOrWhiteSpace(settlementAccountName))
            SettlementAccountName = settlementAccountName.Trim();
        Status = SchoolStatus.Active;
    }

    /// <summary>Set (or change) the payout account, after it has been configured with Xental.</summary>
    public void SetSettlement(string bankName, string bankCode, string accountNumber, string? accountName)
    {
        SettlementBankName = DomainException.Require(bankName, nameof(bankName));
        SettlementBankCode = DomainException.Require(bankCode, nameof(bankCode));
        SettlementAccountNumber = DomainException.Require(accountNumber, nameof(accountNumber));
        if (!string.IsNullOrWhiteSpace(accountName))
            SettlementAccountName = accountName.Trim();
    }

    /// <summary>Set the late-fee policy. <paramref name="bps"/> is a percentage of outstanding in basis
    /// points (500 = 5%); 0 disables late fees. <paramref name="graceDays"/> is days after due before it applies.</summary>
    public void ConfigureLateFees(int bps, int graceDays)
    {
        if (bps < 0 || bps > 10_000) throw new DomainException("Late-fee rate must be between 0 and 10000 bps (0–100%).");
        if (graceDays < 0 || graceDays > 365) throw new DomainException("Grace period must be between 0 and 365 days.");
        LateFeeBps = bps;
        LateFeeGraceDays = graceDays;
    }

    public void Suspend() => Status = SchoolStatus.Suspended;
    public void Activate() => Status = SchoolStatus.Active;
}
