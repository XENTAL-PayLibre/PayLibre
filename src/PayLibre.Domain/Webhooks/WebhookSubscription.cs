using PayLibre.Domain.Common;

namespace PayLibre.Domain.Webhooks;

/// <summary>
/// A school-registered endpoint that PayLibre POSTs signed events to (e.g. <c>payment.received</c>), so
/// the school's own systems stay in sync. The signing secret is shared with the school (shown once) and
/// used to sign every delivery (HMAC-SHA256, header <c>x-paylibre-signature</c>). Tenant-owned.
/// </summary>
public sealed class WebhookSubscription : BaseEntity, ITenantOwned
{
    public Guid SchoolId { get; private set; }
    public Guid TenantId => SchoolId;

    public string Url { get; private set; } = null!;
    public string SigningSecret { get; private set; } = null!;
    public bool Active { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    private WebhookSubscription() { }

    public WebhookSubscription(Guid schoolId, string url, string signingSecret)
    {
        SchoolId = schoolId;
        Url = DomainException.Require(url, nameof(url));
        SigningSecret = DomainException.Require(signingSecret, nameof(signingSecret));
        Active = true;
    }

    public void Revoke(DateTimeOffset now)
    {
        Active = false;
        RevokedAtUtc ??= now;
    }
}
