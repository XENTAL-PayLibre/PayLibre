using PayLibre.Domain.Common;

namespace PayLibre.Domain.Payments;

/// <summary>Lifecycle of an inbound webhook. <see cref="Failed"/> events are the dead-letter queue — they
/// keep their raw payload so an operator can replay them once the cause is fixed.</summary>
public enum WebhookStatus
{
    Received = 1,   // stored, not yet processed
    Processed = 2,  // applied successfully
    Duplicate = 3,  // already seen (idempotent no-op)
    Ignored = 4,    // valid but not actionable (unknown event type / no reference)
    Failed = 5,     // processing threw or produced an error — replayable
}

/// <summary>
/// An audit record of every inbound webhook PayLibre receives (currently Xental's
/// <c>deposit.reconciled</c>). Platform-global (not tenant-owned): events arrive for any school's
/// student. Stores the raw payload and signature-validity so events can be inspected, and any that
/// <see cref="WebhookStatus.Failed"/> can be replayed by an operator without Xental re-sending.
/// </summary>
public sealed class WebhookEvent : BaseEntity
{
    public string Provider { get; private set; } = null!;      // e.g. "xental"
    public string? EventType { get; private set; }             // e.g. "deposit.reconciled"
    public string? Reference { get; private set; }             // provider transaction ref (lookup/idempotency)
    public bool SignatureValid { get; private set; }
    public WebhookStatus Status { get; private set; }
    public string? Detail { get; private set; }                // result status or error message
    public string Payload { get; private set; } = null!;       // raw body — retained for replay
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public int Attempts { get; private set; }

    private WebhookEvent() { }

    public WebhookEvent(string provider, string payload, bool signatureValid, DateTimeOffset receivedAtUtc)
    {
        Provider = DomainException.Require(provider, nameof(provider));
        Payload = payload ?? string.Empty;
        SignatureValid = signatureValid;
        Status = WebhookStatus.Received;
        ReceivedAtUtc = receivedAtUtc;
        Attempts = 0;
    }

    /// <summary>Record the outcome of a processing attempt (also used on replay — increments Attempts).</summary>
    public void RecordResult(string? eventType, string? reference, WebhookStatus status, string? detail, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(eventType)) EventType = eventType;
        if (!string.IsNullOrWhiteSpace(reference)) Reference = reference;
        Status = status;
        Detail = detail;
        ProcessedAtUtc = now;
        Attempts++;
    }
}
