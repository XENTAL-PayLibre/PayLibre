using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Xental;

/// <summary>
/// HTTP adapter for the Xental public API. Exchanges client credentials for a bearer token,
/// caches it, and refreshes on expiry / a 401. This is the ONLY external service PayLibre talks to.
/// </summary>
public sealed class XentalClient(HttpClient http, IOptions<XentalOptions> options, IClock clock) : IXentalClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly XentalOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public async Task<XentalSubMerchant> CreateSubMerchantAsync(string name, string reference, CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/v1/sub-merchants", new { name, reference }, ct);
        return ParseSubMerchant(res);
    }

    public async Task<XentalSubMerchant> SetSubMerchantPayoutAsync(
        Guid subMerchantId, string bankName, string bankCode, string accountNumber, int platformFeeBps, CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Put, $"/api/v1/sub-merchants/{subMerchantId}/payout",
            new { bankName, bankCode, accountNumber, platformFeeBps }, ct);
        return ParseSubMerchant(res);
    }

    public async Task<XentalVirtualAccount> CreateVirtualAccountAsync(
        string accountRef, string name, string subMerchantRef, string? email, string? phone, long? expectedAmountKobo, CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/v1/virtual-accounts",
            new { accountRef, name, email, phone, expectedAmountKobo, subMerchantRef }, ct);
        return new XentalVirtualAccount(
            GetGuid(res, "id"),
            GetString(res, "accountRef") ?? accountRef,
            GetString(res, "accountNumber") ?? throw Bad("virtual account: missing accountNumber"),
            GetString(res, "bankName") ?? "",
            GetString(res, "accountName") ?? name);
    }

    public async Task<XentalWebhookEndpoint> EnsureWebhookEndpointAsync(string url, CancellationToken ct = default)
    {
        // Idempotent: reuse an existing endpoint with the same URL, else create one.
        var list = await SendAsync(HttpMethod.Get, "/api/v1/webhook-endpoints", null, ct);
        if (list.ValueKind == JsonValueKind.Array)
            foreach (var e in list.EnumerateArray())
                if (string.Equals(GetString(e, "url"), url, StringComparison.OrdinalIgnoreCase))
                    return new XentalWebhookEndpoint(GetGuid(e, "id"), url, null);

        var created = await SendAsync(HttpMethod.Post, "/api/v1/webhook-endpoints", new { url }, ct);
        return new XentalWebhookEndpoint(GetGuid(created, "id"), url, GetString(created, "signingSecret"));
    }

    public async Task<IReadOnlyList<XentalBank>> ListBanksAsync(CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Get, "/api/v1/transfers/banks", null, ct);
        var banks = new List<XentalBank>();
        if (res.ValueKind == JsonValueKind.Array)
            foreach (var b in res.EnumerateArray())
                banks.Add(new XentalBank(GetString(b, "name") ?? "", GetString(b, "code") ?? ""));
        return banks;
    }

    public async Task<string> LookupBankAccountAsync(string accountNumber, string bankCode, CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Post, "/api/v1/transfers/bank/lookup", new { accountNumber, bankCode }, ct);
        return GetString(res, "accountName") ?? throw Bad("bank lookup: missing accountName");
    }

    public async Task<XentalSubMerchantBalance> GetSubMerchantBalanceAsync(Guid subMerchantId, CancellationToken ct = default)
    {
        var res = await SendAsync(HttpMethod.Get, $"/api/v1/sub-merchants/{subMerchantId}/balance", null, ct);
        return new XentalSubMerchantBalance(
            GetLong(res, "collectedKobo"), GetLong(res, "settledKobo"), GetLong(res, "pendingKobo"),
            (int)GetLong(res, "virtualAccounts"));
    }

    public async Task<XentalRefundResult> RefundTransactionAsync(
        string transactionRef, string? accountNumber, string? bankCode, string? accountName, CancellationToken ct = default)
    {
        object? body = accountNumber is null && bankCode is null && accountName is null
            ? null
            : new { accountNumber, bankCode, accountName };
        var res = await SendAsync(HttpMethod.Post, $"/api/v1/transactions/{transactionRef}/refund", body, ct);
        return new XentalRefundResult(
            GetString(res, "status") ?? "unknown",
            GetString(res, "transferRef"),
            GetLong(res, "amountKobo"),
            GetString(res, "providerReference"));
    }

    // ---- HTTP + auth plumbing ------------------------------------------------

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var response = await SendOnceAsync(method, path, body, await GetTokenAsync(false, ct), ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            response = await SendOnceAsync(method, path, body, await GetTokenAsync(true, ct), ct); // refresh once

        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new UpstreamException($"Xental {method} {path} -> {(int)response.StatusCode}: {Truncate(payload)}");
        if (string.IsNullOrWhiteSpace(payload)) return default;
        using var doc = JsonDocument.Parse(payload);
        return doc.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> SendOnceAsync(HttpMethod method, string path, object? body, string token, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) req.Content = JsonContent.Create(body, options: Json);
        return await http.SendAsync(req, ct);
    }

    private async Task<string> GetTokenAsync(bool force, CancellationToken ct)
    {
        if (!force && _token is not null && _tokenExpiresAt.AddSeconds(-30) > clock.UtcNow) return _token;
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!force && _token is not null && _tokenExpiresAt.AddSeconds(-30) > clock.UtcNow) return _token;
            if (!_options.IsConfigured)
                throw new UpstreamException("Xental credentials are not configured (Xental:ClientId/ClientSecret).");

            using var res = await http.PostAsJsonAsync("/api/v1/auth/token",
                new { clientId = _options.ClientId, clientSecret = _options.ClientSecret }, Json, ct);
            var payload = await res.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
                throw new UpstreamException($"Xental auth failed ({(int)res.StatusCode}). Check Xental credentials.");
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            _token = GetString(root, "accessToken") ?? throw Bad("auth: missing accessToken");
            var ttl = root.TryGetProperty("expiresIn", out var e) && e.TryGetInt32(out var s) ? s : 3600;
            _tokenExpiresAt = clock.UtcNow.AddSeconds(ttl);
            return _token;
        }
        finally { _tokenLock.Release(); }
    }

    private static XentalSubMerchant ParseSubMerchant(JsonElement res) => new(
        GetGuid(res, "id"),
        GetString(res, "reference") ?? "",
        GetString(res, "status") ?? "",
        GetString(res, "settlementAccountName"));

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static long GetLong(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt64(out var l) ? l : 0;

    private static Guid GetGuid(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && Guid.TryParse(v.GetString(), out var g)
            ? g : Guid.Empty;

    private static UpstreamException Bad(string what) => new($"Unexpected Xental response ({what}).");
    private static string Truncate(string s) => s.Length > 300 ? s[..300] : s;
}
