namespace PayLibre.Application.Common;

/// <summary>Application-level settings bound from configuration (section "PayLibre").</summary>
public sealed class PayLibreOptions
{
    public const string SectionName = "PayLibre";

    /// <summary>PayLibre's platform fee on each school's settlement, in basis points (1% = 100). Default 0.</summary>
    public int PlatformFeeBps { get; set; } = 0;

    /// <summary>Days a dashboard refresh token stays valid.</summary>
    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>Max rows accepted in a single student CSV import.</summary>
    public int MaxCsvRows { get; set; } = 2000;

    /// <summary>Minutes a password-reset link stays valid.</summary>
    public int PasswordResetTtlMinutes { get; set; } = 30;

    /// <summary>Hours a staff invitation link stays valid.</summary>
    public int InviteTtlHours { get; set; } = 72;

    /// <summary>Frontend base URL, used to build password-reset links (e.g. https://app.paylibre.xental.online).</summary>
    public string FrontendUrl { get; set; } = "";
}
