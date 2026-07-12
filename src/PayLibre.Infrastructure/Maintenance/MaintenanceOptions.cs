namespace PayLibre.Infrastructure.Maintenance;

/// <summary>Configuration for the background fee-maintenance worker (section "Maintenance").</summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>Run the periodic late-fee + reminder sweep. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Minutes between sweeps. Reminders/late fees are day-granular, so hourly is ample.</summary>
    public int IntervalMinutes { get; set; } = 60;
}
