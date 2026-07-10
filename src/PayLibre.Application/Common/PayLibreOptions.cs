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
}
