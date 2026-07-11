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

    public void Suspend() => Status = SchoolStatus.Suspended;
    public void Activate() => Status = SchoolStatus.Active;
}
