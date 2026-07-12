using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Payments;

namespace PayLibre.Application.Payments;

public sealed record WebhookIngestResult(Guid EventId, WebhookStatus Status, string Detail);

/// <summary>
/// Ingests inbound webhooks with an audit trail. Every (authentic) event is persisted as a
/// <see cref="WebhookEvent"/> before processing, and the outcome recorded — so nothing is lost, and
/// any event that <see cref="WebhookStatus.Failed"/> keeps its raw payload for an operator to
/// <see cref="ReplayAsync"/> once the cause is fixed. Signature verification happens at the edge
/// (the controller has the raw body + secret); only authentic events reach here.
/// </summary>
public sealed class WebhookService(IApplicationDbContext db, ReconciliationService reconciliation, IClock clock)
{
    /// <summary>Record + process a Xental webhook. Returns the audit record id + outcome.</summary>
    public async Task<WebhookIngestResult> IngestXentalAsync(string rawPayload, bool signatureVerified, CancellationToken ct = default)
    {
        var evt = new WebhookEvent("xental", rawPayload, signatureVerified, clock.UtcNow);
        db.WebhookEvents.Add(evt);
        await ProcessXentalAsync(evt, ct);
        await db.SaveChangesAsync(ct);
        return new(evt.Id, evt.Status, evt.Detail ?? evt.Status.ToString());
    }

    /// <summary>Reprocess a stored event by id (operator action; bypasses signature — the event was
    /// already authenticated when first received). Safe to call repeatedly: reconciliation is idempotent.</summary>
    public async Task<WebhookIngestResult> ReplayAsync(Guid eventId, CancellationToken ct = default)
    {
        var evt = await db.WebhookEvents.FirstOrDefaultAsync(e => e.Id == eventId, ct)
            ?? throw new NotFoundException("Webhook event not found.");
        await ProcessXentalAsync(evt, ct);
        await db.SaveChangesAsync(ct);
        return new(evt.Id, evt.Status, evt.Detail ?? evt.Status.ToString());
    }

    private async Task ProcessXentalAsync(WebhookEvent evt, CancellationToken ct)
    {
        var now = clock.UtcNow;
        JsonElement root;
        try { using var doc = JsonDocument.Parse(evt.Payload); root = doc.RootElement.Clone(); }
        catch { evt.RecordResult(null, null, WebhookStatus.Ignored, "malformed-body", now); return; }

        var eventType = root.TryGetProperty("event", out var e) ? e.GetString() : null;
        if (eventType != "deposit.reconciled" || !root.TryGetProperty("data", out var data))
        {
            evt.RecordResult(eventType, null, WebhookStatus.Ignored, "unhandled-event", now);
            return;
        }

        string? Str(string n) => data.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        long Num(string n) => data.TryGetProperty(n, out var v) && v.TryGetInt64(out var l) ? l : 0;
        var occurred = data.TryGetProperty("occurredAt", out var o) && o.TryGetDateTimeOffset(out var d) ? d : now;
        var reference = Str("transactionRef");

        try
        {
            var result = await reconciliation.ProcessDepositAsync(
                Str("accountRef") ?? string.Empty, Num("amountKobo"), Num("netCreditKobo"),
                reference ?? string.Empty, Str("transferName"), occurred, ct);

            // Map reconciliation outcome onto the audit status. "unknown-account" is a dead-letter
            // (the student may be provisioned later, then the event replayed).
            var status = result.Status switch
            {
                "processed" => WebhookStatus.Processed,
                "duplicate" => WebhookStatus.Duplicate,
                "unknown-account" => WebhookStatus.Failed,
                _ => WebhookStatus.Ignored,
            };
            evt.RecordResult(eventType, reference, status, result.Status, now);
        }
        catch (Exception ex)
        {
            // Processing threw — keep the payload as a dead-letter for replay.
            evt.RecordResult(eventType, reference, WebhookStatus.Failed, ex.Message, now);
        }
    }
}
