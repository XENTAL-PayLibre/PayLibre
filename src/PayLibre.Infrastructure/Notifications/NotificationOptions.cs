namespace PayLibre.Infrastructure.Notifications;

/// <summary>Email delivery via Resend (same provider Xental uses). Section "Resend".</summary>
public sealed class ResendOptions
{
    public const string SectionName = "Resend";
    public string ApiKey { get; set; } = "";
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "PayLibre";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(FromEmail);
}

/// <summary>SMS delivery. Section "Sms". Default provider Termii (Nigeria); Twilio also supported.</summary>
public sealed class SmsOptions
{
    public const string SectionName = "Sms";
    /// <summary>"Termii" (default) or "Twilio".</summary>
    public string Provider { get; set; } = "Termii";
    public string ApiKey { get; set; } = "";
    /// <summary>Termii approved sender id, or the Twilio "from" number.</summary>
    public string SenderId { get; set; } = "";
    /// <summary>Twilio only: Account SID (ApiKey holds the auth token when using Twilio).</summary>
    public string AccountSid { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SenderId);
}
