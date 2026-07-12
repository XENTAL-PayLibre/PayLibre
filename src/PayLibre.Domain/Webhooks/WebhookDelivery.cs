using PayLibre.Domain.Common;

namespace PayLibre.Domain.Webhooks;

public enum DeliveryStatus { Pending = 1, Delivered = 2, Failed = 3 }

/// <summary>
/// A single outbound webhook delivery attempt-set to a school's <see cref="WebhookSubscription"/>.
/// Retried with backoff by the background worker until delivered or the attempt cap is reached.
/// </summary>
public sealed class WebhookDelivery : BaseEntity, ITenantOwned
{
    public const int MaxAttempts = 6;

    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;
    public Guid SubscriptionId { get; private set; }

    public string EventType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;      // raw JSON that gets signed + sent
    public DeliveryStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTimeOffset NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public int? LastResponseStatus { get; private set; }
    public string? LastError { get; private set; }

    private WebhookDelivery() { }

    public WebhookDelivery(Guid schoolId, Guid subscriptionId, string eventType, string payload, DateTimeOffset now)
    {
        SchoolId = schoolId;
        SubscriptionId = subscriptionId;
        EventType = DomainException.Require(eventType, nameof(eventType));
        Payload = payload ?? string.Empty;
        Status = DeliveryStatus.Pending;
        NextAttemptAtUtc = now;
    }

    public void MarkDelivered(int responseStatus, DateTimeOffset now)
    {
        Status = DeliveryStatus.Delivered;
        Attempts++;
        LastAttemptAtUtc = now;
        LastResponseStatus = responseStatus;
        LastError = null;
    }

    /// <summary>Record a failed attempt and either reschedule (exponential backoff) or give up.</summary>
    public void MarkAttemptFailed(int? responseStatus, string error, DateTimeOffset now)
    {
        Attempts++;
        LastAttemptAtUtc = now;
        LastResponseStatus = responseStatus;
        LastError = error;
        if (Attempts >= MaxAttempts) { Status = DeliveryStatus.Failed; return; }
        // Backoff: 1, 2, 4, 8, 16 minutes.
        var delayMinutes = (int)Math.Pow(2, Attempts - 1);
        NextAttemptAtUtc = now.AddMinutes(delayMinutes);
    }
}
