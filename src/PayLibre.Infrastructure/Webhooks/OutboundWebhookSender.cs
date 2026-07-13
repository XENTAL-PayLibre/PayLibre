using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Webhooks;

/// <summary>
/// Delivers outbound webhooks over HTTP, signing the raw body with HMAC-SHA256 (header
/// <c>x-paylibre-signature</c>) keyed by the subscription's secret. Hardened against SSRF: the target
/// host is resolved and every resolved IP is checked against private/loopback/link-local ranges before
/// sending, redirects are disabled, and only https is allowed. Uses a dedicated named client so the
/// no-redirect handler doesn't affect other callers.
/// </summary>
public sealed class OutboundWebhookSender(IHttpClientFactory httpFactory, ILogger<OutboundWebhookSender> logger) : IOutboundWebhookSender
{
    public const string HttpClientName = "outbound-webhook";

    public async Task<(bool Ok, int? StatusCode, string? Error)> PostAsync(string url, string payload, string signingSecret, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return (false, null, "invalid or non-https url");

        // SSRF guard: reject if the host resolves to any internal/loopback/link-local/private address.
        var (safe, reason) = await IsPublicHostAsync(uri.Host, ct);
        if (!safe)
        {
            logger.LogWarning("Blocked outbound webhook to {Host}: {Reason}", uri.Host, reason);
            return (false, null, $"blocked destination: {reason}");
        }

        try
        {
            var signature = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

            var http = httpFactory.CreateClient(HttpClientName);
            using var req = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("x-paylibre-signature", signature);

            using var res = await http.SendAsync(req, ct);
            var status = (int)res.StatusCode;
            // A 3xx (redirects disabled) is treated as a failure rather than silently followed.
            if (res.IsSuccessStatusCode) return (true, status, null);
            return (false, status, $"HTTP {status}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbound webhook to {Host} failed", uri.Host);
            return (false, null, ex.Message);
        }
    }

    private static async Task<(bool Ok, string Reason)> IsPublicHostAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal)) addresses = new[] { literal };
        else
        {
            try { addresses = await Dns.GetHostAddressesAsync(host, ct); }
            catch (Exception ex) { return (false, $"dns failed: {ex.Message}"); }
        }
        if (addresses.Length == 0) return (false, "no dns records");
        foreach (var ip in addresses)
            if (IsBlocked(ip)) return (false, $"non-public address {ip}");
        return (true, "ok");
    }

    /// <summary>True for loopback / link-local (incl. cloud metadata 169.254.169.254) / private / ULA / CGNAT.</summary>
    private static bool IsBlocked(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 => true,                      // "this network", private, loopback
                169 when b[1] == 254 => true,                // link-local (incl. 169.254.169.254 metadata)
                172 when b[1] >= 16 && b[1] <= 31 => true,   // private
                192 when b[1] == 168 => true,                // private
                100 when b[1] >= 64 && b[1] <= 127 => true,  // CGNAT 100.64/10
                _ => false,
            };
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            if (IPAddress.IPv6Loopback.Equals(ip) || IPAddress.IPv6Any.Equals(ip)) return true;
            if ((ip.GetAddressBytes()[0] & 0xFE) == 0xFC) return true; // ULA fc00::/7
        }
        return false;
    }
}
