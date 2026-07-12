using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Api.Contracts;
using PayLibre.Application.Audit;
using PayLibre.Application.Webhooks;
using PayLibre.Domain.Webhooks;

namespace PayLibre.Api.Controllers;

/// <summary>
/// Outbound webhook subscriptions — endpoints PayLibre POSTs signed events to (e.g.
/// <c>payment.received</c>) so a school's own systems stay in sync. Owner/Admin only. Each delivery is
/// signed HMAC-SHA256 over the raw body (header <c>x-paylibre-signature</c>) with the subscription secret.
/// </summary>
[ApiController]
[Route("api/v1/webhook-subscriptions")]
public sealed class WebhookSubscriptionsController(OutboundWebhookService subscriptions, AuditService audit) : ControllerBase
{
    /// <summary>List the school's webhook subscriptions (secrets are never returned).</summary>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(IEnumerable<WebhookSubscriptionResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<WebhookSubscriptionResponse>>> List(CancellationToken ct) =>
        Ok((await subscriptions.ListAsync(ct)).Select(ToResponse));

    /// <summary>Register an https endpoint. The signing secret is returned once in this response.</summary>
    [HttpPost]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(typeof(CreatedWebhookSubscriptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CreatedWebhookSubscriptionResponse>> Create(CreateWebhookSubscriptionRequest request, CancellationToken ct)
    {
        var issued = await subscriptions.CreateAsync(request.Url, ct);
        await audit.RecordAsync("webhook_subscription.created", "WebhookSubscription", issued.Subscription.Id,
            $"Registered webhook endpoint {issued.Subscription.Url}.", ct);
        return Created($"/api/v1/webhook-subscriptions/{issued.Subscription.Id}",
            new CreatedWebhookSubscriptionResponse(ToResponse(issued.Subscription), issued.SigningSecret));
    }

    /// <summary>Revoke a subscription (stops future deliveries).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthPolicies.ManageSchool)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await subscriptions.RevokeAsync(id, ct);
        await audit.RecordAsync("webhook_subscription.revoked", "WebhookSubscription", id, $"Revoked webhook subscription {id}.", ct);
        return NoContent();
    }

    private static WebhookSubscriptionResponse ToResponse(WebhookSubscription s) => new(
        s.Id, s.Url, s.Active, s.CreatedAtUtc, s.RevokedAtUtc);
}
