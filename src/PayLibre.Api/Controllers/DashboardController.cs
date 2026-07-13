using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayLibre.Api.Authorization;
using PayLibre.Application.Dashboard;

namespace PayLibre.Api.Controllers;

/// <summary>Dashboard overview + collection reports for the school (all money in kobo).</summary>
[ApiController]
[Route("api/v1")]
[Authorize(Policy = AuthPolicies.StaffRead)] // school-wide financials; ClassTeacher is scoped elsewhere
public sealed class DashboardController(DashboardService dashboard) : ControllerBase
{
    /// <summary>Headline metrics: students, invoiced/collected/outstanding, collection rate,
    /// paid/overdue counts, a monthly revenue series, and recent payments.</summary>
    [HttpGet("dashboard/overview")]
    [ProducesResponseType(typeof(DashboardOverview), StatusCodes.Status200OK)]
    public async Task<ActionResult<DashboardOverview>> Overview(CancellationToken ct) =>
        Ok(await dashboard.GetOverviewAsync(ct));

    /// <summary>Students with an outstanding balance, largest first.</summary>
    [HttpGet("reports/outstanding")]
    [ProducesResponseType(typeof(IEnumerable<OutstandingRow>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OutstandingRow>>> Outstanding(CancellationToken ct) =>
        Ok(await dashboard.OutstandingAsync(ct));

    /// <summary>Invoiced / collected / outstanding rolled up per class.</summary>
    [HttpGet("reports/collections")]
    [ProducesResponseType(typeof(IEnumerable<ClassCollection>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ClassCollection>>> Collections(CancellationToken ct) =>
        Ok(await dashboard.CollectionsByClassAsync(ct));

    /// <summary>Monthly collection trend (default last 12 months) + a next-month forecast.</summary>
    [HttpGet("reports/trends")]
    [ProducesResponseType(typeof(CollectionTrends), StatusCodes.Status200OK)]
    public async Task<ActionResult<CollectionTrends>> Trends([FromQuery] int months = 12, CancellationToken ct = default) =>
        Ok(await dashboard.GetTrendsAsync(months, ct));

    /// <summary>Outstanding balances as a CSV file.</summary>
    [HttpGet("reports/outstanding/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportOutstanding(CancellationToken ct)
    {
        var rows = await dashboard.OutstandingAsync(ct);
        var sb = new StringBuilder("AdmissionNo,Student,Class,OutstandingKobo\n");
        foreach (var r in rows)
            sb.Append($"{Csv(r.AdmissionNo)},{Csv(r.StudentName)},{Csv(r.ClassName)},{r.OutstandingKobo}\n");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "outstanding.csv");
    }

    /// <summary>Collections-by-class as a CSV file.</summary>
    [HttpGet("reports/collections/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportCollections(CancellationToken ct)
    {
        var rows = await dashboard.CollectionsByClassAsync(ct);
        var sb = new StringBuilder("Class,InvoicedKobo,CollectedKobo,OutstandingKobo\n");
        foreach (var r in rows)
            sb.Append($"{Csv(r.ClassName)},{r.InvoicedKobo},{r.CollectedKobo},{r.OutstandingKobo}\n");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "collections.csv");
    }

    private static string Csv(string? v)
    {
        v ??= "";
        if (v.Length > 0 && (v[0] is '=' or '+' or '-' or '@' or '\t' or '\r')) v = "'" + v; // formula-injection guard
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }
}
