using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PayLibre.Application.Common.Exceptions;
using PayLibre.Application.Common.Interfaces;
using PayLibre.Domain.Webhooks;

namespace PayLibre.Application.Webhooks;

public sealed record IssuedSubscription(WebhookSubscription Subscription, string SigningSecret);

/// <summary>
/// Manages a school's outbound-webhook subscriptions and delivers events to them. Deliveries are queued
/// (one per active subscription) and sent by the background worker with retry/backoff, signed with the
/// subscription's secret. Subscription management is tenant-scoped; enqueue + delivery run globally.
/// </summary>
public sealed class OutboundWebhookService(IApplicationDbContext db, ITenantContext tenant, IClock clock, IOutboundWebhookSender sender)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ---- Subscription management (tenant-scoped) ----

    public async Task<IssuedSubscription> CreateAsync(string url, CancellationToken ct = default)
    {
        var schoolId = tenant.RequireTenantId();
        url = (url ?? string.Empty).Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ValidationException("A valid https URL is required.");

        var secret = "whsec_" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', 'a').Replace('/', 'b').TrimEnd('=');
        var sub = new WebhookSubscription(schoolId, url, secret);
        db.WebhookSubscriptions.Add(sub);
        await db.SaveChangesAsync(ct);
        return new IssuedSubscription(sub, secret);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        return (await db.WebhookSubscriptions.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(s => s.CreatedAtUtc).ToList();
    }

    public async Task RevokeAsync(Guid id, CancellationToken ct = default)
    {
        _ = tenant.RequireTenantId();
        var sub = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == id, ct)
            ?? throw new NotFoundException("Subscription not found.");
        sub.Revoke(clock.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    // ---- Enqueue + deliver (global / no tenant) ----

    /// <summary>Queue an event for delivery to every active subscription of a school. Best-effort caller.</summary>
    public async Task EnqueueAsync(Guid schoolId, string eventType, object payload, CancellationToken ct = default)
    {
        var subs = await db.WebhookSubscriptions.IgnoreQueryFilters()
            .Where(s => s.SchoolId == schoolId && s.Active).ToListAsync(ct);
        if (subs.Count == 0) return;

        var body = JsonSerializer.Serialize(new { @event = eventType, data = payload, occurredAtUtc = clock.UtcNow }, Json);
        foreach (var sub in subs)
            db.WebhookDeliveries.Add(new WebhookDelivery(schoolId, sub.Id, eventType, body, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Deliver all due pending events (worker). Returns the number attempted.</summary>
    public async Task<int> DeliverDueAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var due = (await db.WebhookDeliveries.IgnoreQueryFilters()
                .Where(d => d.Status == DeliveryStatus.Pending)
                .ToListAsync(ct))
            .Where(d => d.NextAttemptAtUtc <= now)
            .OrderBy(d => d.NextAttemptAtUtc)
            .Take(100)
            .ToList();
        if (due.Count == 0) return 0;

        var subIds = due.Select(d => d.SubscriptionId).Distinct().ToList();
        var subs = (await db.WebhookSubscriptions.IgnoreQueryFilters()
                .Where(s => subIds.Contains(s.Id)).ToListAsync(ct))
            .ToDictionary(s => s.Id);

        foreach (var d in due)
        {
            if (!subs.TryGetValue(d.SubscriptionId, out var sub) || !sub.Active)
            {
                d.MarkAttemptFailed(null, "subscription inactive", now);
                continue;
            }
            try
            {
                var (ok, status, error) = await sender.PostAsync(sub.Url, d.Payload, sub.SigningSecret, ct);
                if (ok) d.MarkDelivered(status ?? 200, now);
                else d.MarkAttemptFailed(status, error ?? "delivery failed", now);
            }
            catch (Exception ex) { d.MarkAttemptFailed(null, ex.Message, now); }
        }
        await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
