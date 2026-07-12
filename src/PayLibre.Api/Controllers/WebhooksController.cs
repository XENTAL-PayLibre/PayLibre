using System.Security.Cryptography;
using System.Text;
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
/// Every authentic event is recorded (audit trail); failures are dead-lettered for replay.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
[AllowAnonymous]
[EnableRateLimiting("webhook")]
public sealed class WebhooksController(
    WebhookService webhooks,
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
        var signatureVerified = false;
        if (!string.IsNullOrWhiteSpace(secret))
        {
            var provided = Request.Headers["x-xental-signature"].FirstOrDefault() ?? string.Empty;
            var expected = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected)))
                return Unauthorized(new { error = "Invalid signature." });
            signatureVerified = true;
        }
        else
        {
            logger.LogWarning("Xental webhook secret not configured — skipping signature verification.");
        }

        var result = await webhooks.IngestXentalAsync(raw, signatureVerified, ct);
        logger.LogInformation("Xental webhook {EventId}: {Status} ({Detail})", result.EventId, result.Status, result.Detail);
        return Ok(new { eventId = result.EventId, status = result.Status.ToString() });
    }

    /// <summary>Operator-only: replay a stored webhook event (e.g. one that failed because the student
    /// wasn't provisioned yet). Requires the <c>x-replay-secret</c> header; returns 404 when replay is
    /// not configured. Idempotent — reconciliation won't double-apply a payment.</summary>
    [HttpPost("xental/{eventId:guid}/replay")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replay(Guid eventId, CancellationToken ct)
    {
        var replaySecret = xental.Value.ReplaySecret;
        if (string.IsNullOrWhiteSpace(replaySecret)) return NotFound();

        var provided = Request.Headers["x-replay-secret"].FirstOrDefault() ?? string.Empty;
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(replaySecret)))
            return Unauthorized(new { error = "Invalid replay secret." });

        var result = await webhooks.ReplayAsync(eventId, ct);
        logger.LogInformation("Replayed webhook {EventId}: {Status} ({Detail})", result.EventId, result.Status, result.Detail);
        return Ok(new { eventId = result.EventId, status = result.Status.ToString(), detail = result.Detail });
    }
}
