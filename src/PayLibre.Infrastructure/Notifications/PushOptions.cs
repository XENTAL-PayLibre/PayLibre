namespace PayLibre.Infrastructure.Notifications;

/// <summary>Push-notification (FCM) configuration (section "Push"). Empty until credentials are set —
/// pushes are logged and skipped, so the app works without them (same pattern as SMS).</summary>
public sealed class PushOptions
{
    public const string SectionName = "Push";

    /// <summary>Firebase Cloud Messaging legacy server key. When empty, push is disabled (log-only).</summary>
    public string FcmServerKey { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(FcmServerKey);
}
