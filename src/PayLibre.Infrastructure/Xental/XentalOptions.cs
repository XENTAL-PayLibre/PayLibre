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

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
