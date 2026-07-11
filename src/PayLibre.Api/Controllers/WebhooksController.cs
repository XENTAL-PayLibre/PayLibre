using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PayLibre.Application.Payments;
using PayLibre.Infrastructure.Xental;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Inbound webhook from Xental (PayLibre's only external dependency). Receives
/// <c>deposit.reconciled</c> events, verifies the HMAC-SHA256 signature (<c>x-xental-signature</c>
/// over the raw body), and applies each deposit to the student's outstanding fees (oldest-due-first).
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
[AllowAnonymous]
[EnableRateLimiting("webhook")]
public sealed class WebhooksController(
    ReconciliationService reconciliation,
    IOptions<XentalOptions> xental,
    ILogger<WebhooksController> logger) : ControllerBase
{
    /// <summary>Receive a Xental deposit event. Returns 200 quickly; 401 on a bad signature.</summary>
    [HttpPost("xental")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Xental(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(ct);

        var secret = xental.Value.WebhookSecret;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var provided = Request.Headers["x-xental-signature"].FirstOrDefault() ?? string.Empty;
            var expected = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
                return Unauthorized(new { error = "Invalid signature." });
        }
        else
        {
            logger.LogWarning("Xental webhook secret not configured — skipping signature verification.");
        }

        JsonElement root;
        try { using var doc = JsonDocument.Parse(raw); root = doc.RootElement.Clone(); }
        catch { return Ok(); } // malformed body — ack so Xental doesn't retry forever

        var evt = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (evt != "deposit.reconciled" || !root.TryGetProperty("data", out var data))
            return Ok(); // ignore other event types

        string? Str(string n) => data.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        long Num(string n) => data.TryGetProperty(n, out var v) && v.TryGetInt64(out var l) ? l : 0;
        var occurred = data.TryGetProperty("occurredAt", out var o) && o.TryGetDateTimeOffset(out var d) ? d : DateTimeOffset.UtcNow;

        var result = await reconciliation.ProcessDepositAsync(
            Str("accountRef") ?? string.Empty, Num("amountKobo"), Num("netCreditKobo"),
            Str("transactionRef") ?? string.Empty, Str("transferName"), occurred, ct);

        logger.LogInformation("Xental deposit {Ref}: {Status} ({Settled} invoices, {Allocated} kobo)",
            Str("transactionRef"), result.Status, result.InvoicesSettled, result.AllocatedKobo);
        return Ok(new { result.Status });
    }
}
