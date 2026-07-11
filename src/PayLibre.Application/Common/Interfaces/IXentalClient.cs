namespace PayLibre.Application.Common.Interfaces;

// PayLibre's only external dependency. Talks to the Xental public API (client-credentials auth,
// bearer token cached + refreshed). Xental owns Nomba, DVAs, reconciliation and settlement.

public sealed record XentalSubMerchant(Guid Id, string Reference, string Status, string? SettlementAccountName);
public sealed record XentalVirtualAccount(Guid Id, string AccountRef, string AccountNumber, string BankName, string AccountName);
public sealed record XentalWebhookEndpoint(Guid Id, string Url, string? SigningSecret);
public sealed record XentalBank(string Name, string Code);

public interface IXentalClient
{
    /// <summary>Create a sub-merchant (one per school) so settlement can route to the school's bank.</summary>
    Task<XentalSubMerchant> CreateSubMerchantAsync(string name, string reference, CancellationToken ct = default);

    /// <summary>Set the sub-merchant's payout account (the school's bank) + PayLibre's platform fee (bps).
    /// The account name is resolved by Xental from the bank. Returns the resolved name.</summary>
    Task<XentalSubMerchant> SetSubMerchantPayoutAsync(
        Guid subMerchantId, string bankName, string bankCode, string accountNumber, int platformFeeBps, CancellationToken ct = default);

    /// <summary>Provision a persistent dedicated virtual account (NUBAN) for a student under a school.</summary>
    Task<XentalVirtualAccount> CreateVirtualAccountAsync(
        string accountRef, string name, string subMerchantRef, string? email, string? phone, long? expectedAmountKobo, CancellationToken ct = default);

    /// <summary>Register (idempotently) the PayLibre webhook receiver with Xental. Returns the signing secret on first create.</summary>
    Task<XentalWebhookEndpoint> EnsureWebhookEndpointAsync(string url, CancellationToken ct = default);

    /// <summary>List payout banks (name + code) so registration can resolve a selected bank to its code.</summary>
    Task<IReadOnlyList<XentalBank>> ListBanksAsync(CancellationToken ct = default);

    /// <summary>Name-enquiry: resolve the account holder name for a bank account.</summary>
    Task<string> LookupBankAccountAsync(string accountNumber, string bankCode, CancellationToken ct = default);
}
