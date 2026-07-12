using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PayLibre.Application.Maintenance;
using PayLibre.Infrastructure.Maintenance;
using PayLibre.Infrastructure.Persistence;

namespace PayLibre.Api.Maintenance;

/// <summary>
/// Periodic background sweep that applies overdue late fees and sends fee reminders across all schools.
/// Uses a Postgres session-level advisory lock so that, if more than one instance runs, only one does
/// the work each tick (the others skip). Day-granular work, so an hourly cadence is plenty.
/// </summary>
public sealed class FeeMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceOptions> options,
    ILogger<FeeMaintenanceWorker> logger) : BackgroundService
{
    private const long AdvisoryLockKey = 0x5041_594C_4645_454D; // "PAYLFEEM"

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled) { logger.LogInformation("Fee-maintenance worker disabled."); return; }
        var interval = TimeSpan.FromMinutes(Math.Max(1, opts.IntervalMinutes));

        // Let startup (incl. EF migrations) settle before the first run.
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { logger.LogError(ex, "Fee-maintenance run failed."); }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PayLibreDbContext>();
        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

        if (!isPostgres) { await RunJobsAsync(scope, ct); return; }

        var conn = db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        if (!await TryAdvisoryLockAsync(conn, ct))
        {
            logger.LogInformation("Fee-maintenance: another instance holds the lock; skipping this tick.");
            return;
        }
        try { await RunJobsAsync(scope, ct); }
        finally { await AdvisoryUnlockAsync(conn, ct); }
    }

    private static async Task<bool> TryAdvisoryLockAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_try_advisory_lock(@k)";
        var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = AdvisoryLockKey; cmd.Parameters.Add(p);
        return (bool)(await cmd.ExecuteScalarAsync(ct))!;
    }

    private static async Task AdvisoryUnlockAsync(System.Data.Common.DbConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_unlock(@k)";
        var p = cmd.CreateParameter(); p.ParameterName = "k"; p.Value = AdvisoryLockKey; cmd.Parameters.Add(p);
        await cmd.ExecuteScalarAsync(ct);
    }

    private async Task RunJobsAsync(IServiceScope scope, CancellationToken ct)
    {
        var applied = await scope.ServiceProvider.GetRequiredService<LateFeeService>().ApplyDueAsync(ct);
        var sent = await scope.ServiceProvider.GetRequiredService<ReminderService>().SendDueAsync(ct);
        var delivered = await scope.ServiceProvider.GetRequiredService<PayLibre.Application.Webhooks.OutboundWebhookService>().DeliverDueAsync(ct);
        if (applied > 0 || sent > 0 || delivered > 0)
            logger.LogInformation("Maintenance: {Applied} late fees, {Sent} reminders, {Delivered} webhooks delivered.", applied, sent, delivered);
    }
}
