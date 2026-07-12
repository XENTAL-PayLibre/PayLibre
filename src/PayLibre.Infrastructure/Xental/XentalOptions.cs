namespace PayLibre.Infrastructure.Xental;

/// <summary>Configuration for the Xental integration (section "Xental"). PayLibre's only external dependency.</summary>
public sealed class XentalOptions
{
    public const string SectionName = "Xental";

    /// <summary>Xental API base URL. Sandbox: https://api.staging.xental.online ; live: https://api.xental.online</summary>
    public string BaseUrl { get; set; } = "https://api.xental.online";

    /// <summary>Client id of the Xental API key PayLibre uses (from the Xental dashboard).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Client secret of that key (shown once). Supplied via secret/env.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Signing secret of PayLibre's Xental webhook endpoint (HMAC-SHA256, header
    /// <c>x-xental-signature</c>). Obtained when the endpoint is registered; verifies inbound events.
    /// When empty, signature verification is skipped (log-only) — set it in real environments.</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>Operator secret for replaying stored webhook events (header <c>x-replay-secret</c>) — used
    /// to reprocess dead-lettered events. When empty, the replay endpoint is disabled (returns 404).</summary>
    public string ReplaySecret { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
