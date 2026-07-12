using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PayLibre.Application.Common.Interfaces;

namespace PayLibre.Infrastructure.Webhooks;

/// <summary>
/// Delivers outbound webhooks over HTTP, signing the raw body with HMAC-SHA256 (header
/// <c>x-paylibre-signature</c>) keyed by the subscription's secret — the same scheme PayLibre verifies
/// on Xental's inbound webhooks, so schools can verify us the same way.
/// </summary>
public sealed class OutboundWebhookSender(IHttpClientFactory httpFactory, ILogger<OutboundWebhookSender> logger) : IOutboundWebhookSender
{
    public async Task<(bool Ok, int? StatusCode, string? Error)> PostAsync(string url, string payload, string signingSecret, CancellationToken ct = default)
    {
        try
        {
            var signature = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingSecret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(15);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            };
            req.Headers.TryAddWithoutValidation("x-paylibre-signature", signature);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var res = await http.SendAsync(req, ct);
            var status = (int)res.StatusCode;
            if (res.IsSuccessStatusCode) return (true, status, null);
            return (false, status, $"HTTP {status}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Outbound webhook to {Url} failed", url);
            return (false, null, ex.Message);
        }
    }
}
